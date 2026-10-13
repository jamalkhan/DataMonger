using System.Globalization;
using System.Text.Json;

namespace DataMonger.Synth;

/// <summary>
/// Builds one fake customer profile for the invented retailer "Acme Outfitters" (see Documentation/SynthesizeUserProps/Schema.md):
/// identity fields, identifiers, addresses, marketing consent, preferences, an order-history summary,
/// one membership per configured brand, and a flat contact section with per-brand prefixed keys.
/// Every enumerated value comes from the picklists in <see cref="SynthConfig"/>, and every random draw comes from
/// the injected <see cref="Random"/> and anchor time, so a seeded run with a fixed anchor is fully reproducible.
/// </summary>
public sealed class UserPropsSynthesizer(SynthConfig config, Random rng, DateTimeOffset now)
{
    public const string SourceSystem = "acme_synth";

    private readonly DateTime _now = now.UtcDateTime;
    private readonly ProbabilityConfig _p = config.Probabilities;

    /// <summary>One brand's membership, generated once and reused in both the memberships array and the flat contact.</summary>
    private sealed record Membership(
        BrandConfig Brand, string MemberId, string Tier, string? PreviousTier, int Points,
        DateTime JoinedAt, DateTime TierSince, DateTime TierExpiresAt, string Status);

    public void Write(Utf8JsonWriter w, int seq)
    {
        var userId = $"CUST_{seq}";
        var firstName = Pick(config.FirstNames);
        var lastName = Pick(config.LastNames);
        var email = BuildEmail(firstName, lastName, seq);
        var locale = Pick("locale");
        var country = CountryOf(locale);
        var phone = BuildPhone();
        var createdAt = RandomPastDate(365 * 4, 30);
        var dob = RandomPastDate(365 * 60, 365 * 20).Date;

        w.WriteStartObject();
        w.WriteString("user_id", userId);
        w.WriteString("first_name", firstName);
        w.WriteString("last_name", lastName);
        w.WriteString("full_name", $"{firstName} {lastName}");
        w.WriteString("dob", FormatDate(dob));
        w.WriteString("gender", Pick("gender"));
        w.WriteString("timezone", Pick("timezone"));
        w.WriteString("locale", locale);
        w.WriteString("country", country);
        w.WriteString("created_at", FormatTimestamp(createdAt));
        w.WriteString("source_system", SourceSystem);
        w.WriteString("acquisition_channel", Pick("acquisitionChannel"));
        w.WriteBoolean("is_employee", Bool(_p.Employee));
        w.WriteBoolean("is_active", Bool(_p.Active));

        w.WriteStartArray("identifiers");
        WriteIdentifier(w, "email", email);
        WriteIdentifier(w, "phone", phone);
        w.WriteEndArray();

        WriteAddresses(w, country);

        w.WriteStartObject("consent");
        w.WriteBoolean("email", Bool(_p.EmailConsent));
        w.WriteBoolean("sms", Bool(_p.SmsConsent));
        w.WriteBoolean("push", Bool(_p.PushConsent));
        w.WriteBoolean("postal", Bool(_p.PostalConsent));
        w.WriteEndObject();

        w.WriteStartObject("preferences");
        w.WriteString("favorite_channel", Pick("favoriteChannel"));
        w.WriteString("favorite_category", Pick("favoriteCategory"));
        w.WriteString("contact_frequency", Pick("contactFrequency"));
        w.WriteEndObject();

        WriteOrderSummary(w, createdAt);

        var memberships = config.Brands.Select((b, i) => BuildMembership(b, seq, i, createdAt)).ToList();
        w.WriteStartArray("memberships");
        foreach (var m in memberships)
        {
            w.WriteStartObject();
            w.WriteString("brand_code", m.Brand.Code);
            w.WriteString("brand_name", m.Brand.Name);
            w.WriteString("storefront_id", m.Brand.StorefrontId);
            w.WriteString("member_id", m.MemberId);
            w.WriteString("tier", m.Tier);
            if (m.PreviousTier is null) w.WriteNull("previous_tier"); else w.WriteString("previous_tier", m.PreviousTier);
            w.WriteNumber("points", m.Points);
            w.WriteString("joined_at", FormatDate(m.JoinedAt));
            w.WriteString("tier_since", FormatDate(m.TierSince));
            w.WriteString("tier_expires_at", FormatDate(m.TierExpiresAt));
            w.WriteString("status", m.Status);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartObject("contact");
        w.WriteString("email", email);
        w.WriteString("phone", phone);
        w.WriteString("email_domain", email[(email.IndexOf('@') + 1)..]);
        foreach (var m in memberships)
        {
            var prefix = m.Brand.ContactPrefix;
            w.WriteString($"{prefix}_member_id", m.MemberId);
            w.WriteString($"{prefix}_tier", m.Tier);
            w.WriteNumber($"{prefix}_points", m.Points);
            w.WriteString($"{prefix}_status", m.Status);
        }
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static void WriteIdentifier(Utf8JsonWriter w, string type, string value)
    {
        w.WriteStartObject();
        w.WriteString("type", type);
        w.WriteString("value", value);
        w.WriteEndObject();
    }

    private void WriteAddresses(Utf8JsonWriter w, string country)
    {
        w.WriteStartArray("addresses");
        WriteAddress(w, "billing", country);
        if (Bool(_p.SecondAddress))
        {
            WriteAddress(w, "shipping", country);
        }
        w.WriteEndArray();
    }

    private void WriteAddress(Utf8JsonWriter w, string type, string country)
    {
        var parts = Pick("cityRegionPostal").Split('|', StringSplitOptions.TrimEntries);
        w.WriteStartObject();
        w.WriteString("type", type);
        w.WriteString("line1", $"{rng.Next(1, 9999).ToString(CultureInfo.InvariantCulture)} {Pick("streetName")}");
        w.WriteString("city", parts[0]);
        w.WriteString("region", parts[1]);
        w.WriteString("postal_code", parts[2]);
        w.WriteString("country", country);
        w.WriteEndObject();
    }

    private void WriteOrderSummary(Utf8JsonWriter w, DateTime createdAt)
    {
        var hasOrders = Bool(_p.HasOrders);
        w.WriteStartObject("order_summary");
        if (!hasOrders)
        {
            w.WriteNumber("order_count", 0);
            w.WriteNull("first_order_at");
            w.WriteNull("last_order_at");
            w.WriteNumber("lifetime_spend", 0m);
            w.WriteNumber("average_order_value", 0m);
            w.WriteNumber("returns_count", 0);
            w.WriteEndObject();
            return;
        }

        var orderCount = rng.Next(1, 41);
        var averageOrder = Math.Round(25m + (decimal)rng.NextDouble() * 155m, 2);
        var firstOrder = createdAt.AddSeconds(rng.NextInt64(0, Math.Max(1, (long)(_now - createdAt).TotalSeconds)));
        var lastOrder = orderCount == 1
            ? firstOrder
            : firstOrder.AddSeconds(rng.NextInt64(0, Math.Max(1, (long)(_now - firstOrder).TotalSeconds)));
        w.WriteNumber("order_count", orderCount);
        w.WriteString("first_order_at", FormatTimestamp(firstOrder));
        w.WriteString("last_order_at", FormatTimestamp(lastOrder));
        w.WriteNumber("lifetime_spend", Math.Round(averageOrder * orderCount, 2));
        w.WriteNumber("average_order_value", averageOrder);
        w.WriteNumber("returns_count", rng.Next(0, orderCount / 4 + 1));
        w.WriteEndObject();
    }

    private Membership BuildMembership(BrandConfig brand, int seq, int index, DateTime createdAt)
    {
        var tier = Pick("membershipTier");
        var changed = Bool(_p.TierChanged);
        var previous = changed ? (rng.Next(2) == 0 ? MembershipTiers.Previous(tier) : MembershipTiers.Next(tier)) : null;
        if (previous is not null && string.Equals(previous, tier, StringComparison.OrdinalIgnoreCase))
        {
            previous = null; // Bronze can't go below Bronze, Gold can't go above Gold
        }
        var (min, max) = MembershipTiers.PointsRange(tier);
        var joinedAt = createdAt.AddSeconds(rng.NextInt64(0, Math.Max(1, (long)(_now - createdAt).TotalSeconds))).Date;
        var tierSince = joinedAt.AddDays(rng.Next(0, Math.Max(1, (int)(_now.Date - joinedAt).TotalDays) + 1));
        return new Membership(
            brand, $"M{seq}{index:D2}", tier, previous, rng.Next(min, max),
            joinedAt, tierSince, tierSince.AddYears(1), Pick("membershipStatus"));
    }

    // ------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------
    private string Pick(string picklistName)
    {
        if (!config.Picklists.TryGetValue(picklistName, out var values) || values.Count == 0)
        {
            throw new InvalidOperationException($"Picklist '{picklistName}' is missing or empty in the synth config.");
        }
        return values[rng.Next(values.Count)];
    }

    private string Pick(List<string> values)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("A name pool in the synth config is empty.");
        }
        return values[rng.Next(values.Count)];
    }

    private static string Slug(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());

    private string BuildEmail(string firstName, string lastName, int seq)
    {
        // "+seq" keeps addresses unique even when two customers draw the same name pair.
        var domain = config.EmailDomainTemplate.Replace("{lastName}", Slug(lastName), StringComparison.OrdinalIgnoreCase);
        return $"{Slug(firstName)}.{Slug(lastName)}+{seq.ToString(CultureInfo.InvariantCulture)}@{domain}".ToLowerInvariant();
    }

    /// <summary>"en-US" -> "US"; a locale with no region part falls back to "US".</summary>
    public static string CountryOf(string locale)
    {
        var i = locale.IndexOf('-');
        return i >= 0 && i < locale.Length - 1 ? locale[(i + 1)..].ToUpperInvariant() : "US";
    }

    /// <summary>Fictional NANP numbers in the reserved 555-01xx range.</summary>
    private string BuildPhone() =>
        $"+1212555{rng.Next(100, 200).ToString("D4", CultureInfo.InvariantCulture)}";

    private bool Bool(double probabilityTrue) => rng.NextDouble() < probabilityTrue;

    private DateTime RandomPastDate(int maxDaysAgo, int minDaysAgo = 0)
    {
        var days = rng.Next(minDaysAgo, Math.Max(minDaysAgo + 1, maxDaysAgo));
        return _now.AddDays(-days).AddSeconds(-rng.Next(0, 86400));
    }

    public static string FormatTimestamp(DateTime dt) =>
        dt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string FormatDate(DateTime dt) =>
        dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
