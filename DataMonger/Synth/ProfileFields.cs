using System.Text.Json;

namespace DataMonger.Synth;

/// <summary>
/// Reads the handful of profile fields the event commands need (see Documentation/SynthesizeSessionEvents/InputFormat.md).
/// Every accessor is tolerant: a missing or wrongly-typed field yields null/defaults rather than an exception,
/// so hand-written or older profile files still work.
/// </summary>
public static class ProfileFields
{
    public const string DefaultStorefrontId = "acme-us";
    public const string DefaultLocale = "en-US";

    /// <summary>
    /// The profile's email: the first <c>identifiers[]</c> entry whose type is "email", else <c>contact.email</c>,
    /// else a top-level <c>email</c>. Null when none is a non-blank string.
    /// </summary>
    public static string? ResolveEmail(JsonElement profile)
    {
        if (profile.TryGetProperty("identifiers", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in ids.EnumerateArray())
            {
                if (id.ValueKind == JsonValueKind.Object &&
                    string.Equals(Str(id, "type"), "email", StringComparison.OrdinalIgnoreCase) &&
                    Str(id, "value") is { } v && !string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }
        }
        if (profile.TryGetProperty("contact", out var contact) && contact.ValueKind == JsonValueKind.Object &&
            Str(contact, "email") is { } ce && !string.IsNullOrWhiteSpace(ce))
        {
            return ce;
        }
        var top = Str(profile, "email");
        return string.IsNullOrWhiteSpace(top) ? null : top;
    }

    /// <summary>The profile's phone: the first <c>identifiers[]</c> entry whose type is "phone", else <c>contact.phone</c>.</summary>
    public static string? ResolvePhone(JsonElement profile)
    {
        if (profile.TryGetProperty("identifiers", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in ids.EnumerateArray())
            {
                if (id.ValueKind == JsonValueKind.Object &&
                    string.Equals(Str(id, "type"), "phone", StringComparison.OrdinalIgnoreCase) &&
                    Str(id, "value") is { } v && !string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }
        }
        return profile.TryGetProperty("contact", out var contact) && contact.ValueKind == JsonValueKind.Object
            ? NullIfBlank(Str(contact, "phone"))
            : null;
    }

    /// <summary>
    /// A one-line postal address ("line1, city, region postal_code") from the first <c>addresses[]</c> entry whose
    /// type is "billing" (else the first entry); null when there is none.
    /// </summary>
    public static string? ResolvePostalAddress(JsonElement profile)
    {
        if (!profile.TryGetProperty("addresses", out var addresses) || addresses.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        JsonElement? chosen = null;
        foreach (var a in addresses.EnumerateArray())
        {
            if (a.ValueKind != JsonValueKind.Object) continue;
            chosen ??= a;
            if (string.Equals(Str(a, "type"), "billing", StringComparison.OrdinalIgnoreCase))
            {
                chosen = a;
                break;
            }
        }
        if (chosen is not { } addr) return null;
        var parts = new[] { Str(addr, "line1"), Str(addr, "city"), $"{Str(addr, "region")} {Str(addr, "postal_code")}".Trim() }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var text = string.Join(", ", parts);
        return text.Length == 0 ? null : text;
    }

    public static string? ResolveUserId(JsonElement profile) => NullIfBlank(Str(profile, "user_id"));

    public static string? ResolveTimezone(JsonElement profile) => NullIfBlank(Str(profile, "timezone"));

    public static string ResolveLocale(JsonElement profile) => NullIfBlank(Str(profile, "locale")) ?? DefaultLocale;

    /// <summary>First membership's storefront_id, else <see cref="DefaultStorefrontId"/>.</summary>
    public static string ResolveStorefrontId(JsonElement profile)
    {
        if (profile.TryGetProperty("memberships", out var ms) && ms.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in ms.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.Object && NullIfBlank(Str(m, "storefront_id")) is { } s)
                {
                    return s;
                }
            }
        }
        return DefaultStorefrontId;
    }

    /// <summary>ISO currency for the profile's country (<c>country</c>, else the region of its locale): CA -> CAD, otherwise USD.</summary>
    public static string ResolveCurrency(JsonElement profile)
    {
        var country = NullIfBlank(Str(profile, "country")) ?? UserPropsSynthesizer.CountryOf(ResolveLocale(profile));
        return string.Equals(country, "CA", StringComparison.OrdinalIgnoreCase) ? "CAD" : "USD";
    }

    /// <summary>
    /// The consent channels (email, sms, push, postal) whose <c>consent.&lt;channel&gt;</c> flag is true.
    /// A profile with no consent object at all is treated as consenting to email only.
    /// </summary>
    public static IReadOnlyList<string> ConsentedChannels(JsonElement profile)
    {
        if (!profile.TryGetProperty("consent", out var consent) || consent.ValueKind != JsonValueKind.Object)
        {
            return new[] { "email" };
        }
        return new[] { "email", "sms", "push", "postal" }
            .Where(c => consent.TryGetProperty(c, out var v) && v.ValueKind == JsonValueKind.True)
            .ToList();
    }

    /// <summary>The profile's created_at (ISO 8601), or null if absent or unparseable.</summary>
    public static DateTimeOffset? ResolveCreatedAt(JsonElement profile) =>
        DateTimeOffset.TryParse(Str(profile, "created_at"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
