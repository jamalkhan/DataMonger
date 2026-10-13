using System.Globalization;

namespace DataMonger.Synth;

/// <summary>
/// Config for <see cref="DataMonger.Commands.SynthesizeUserProps"/>: how many fake customers to generate,
/// the brand list each customer is fanned out across, the name pools, the probabilities of the yes/no
/// fields, and the picklist value pools used for every enumerated field.
/// Editing the config file, not the command's code, is how you add brands or widen a picklist.
/// </summary>
public sealed class SynthConfig
{
    /// <summary>Names of the picklists the generator reads. All must be present and non-empty.</summary>
    public static readonly IReadOnlyList<string> RequiredPicklists = new[]
    {
        "acquisitionChannel", "gender", "timezone", "locale",
        "streetName", "cityRegionPostal",
        "membershipTier", "membershipStatus",
        "favoriteChannel", "favoriteCategory", "contactFrequency",
    };

    private Dictionary<string, List<string>> _picklists = NewPicklists();

    public int RecordCount { get; set; } = 10;

    /// <summary>Null = non-deterministic (a new random seed each run).</summary>
    public int? Seed { get; set; }

    /// <summary>
    /// Date (yyyy-MM-dd, UTC midnight) treated as "today" by the generator. Null = the real current UTC date.
    /// Set it together with <see cref="Seed"/> for byte-for-byte reproducible output.
    /// </summary>
    public string? AnchorDate { get; set; }

    /// <summary>Numeric part of the first user_id (CUST_{n}); incremented per generated record.</summary>
    public int StartUserSeq { get; set; } = 1001;

    public string FileBaseName { get; set; } = "profiles_synth";

    public long SplitEvery { get; set; } = 1_000_000;

    /// <summary>First-name pool. Combined independently with <see cref="LastNames"/>.</summary>
    public List<string> FirstNames { get; set; } = new();

    /// <summary>Last-name pool combined with <see cref="FirstNames"/>.</summary>
    public List<string> LastNames { get; set; } = new();

    /// <summary>Email domain template; "{lastName}" is replaced with the picked last name (letters/digits, lowercased).</summary>
    public string EmailDomainTemplate { get; set; } = "{lastName}.example.com";

    public List<BrandConfig> Brands { get; set; } = new();

    public ProbabilityConfig Probabilities { get; set; } = new();

    /// <summary>
    /// Picklist name -> allowed values. Lookups are case-insensitive. The setter re-wraps whatever the JSON
    /// deserializer hands over (which would otherwise be a case-sensitive dictionary).
    /// </summary>
    public Dictionary<string, List<string>> Picklists
    {
        get => _picklists;
        set
        {
            var copy = NewPicklists();
            foreach (var (k, v) in value)
            {
                copy[k] = v ?? new List<string>();
            }
            _picklists = copy;
        }
    }

    private static Dictionary<string, List<string>> NewPicklists() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves <see cref="AnchorDate"/> to a UTC instant, or <paramref name="fallbackNow"/> when unset.</summary>
    public DateTimeOffset ResolveAnchor(DateTimeOffset fallbackNow) =>
        AnchorDate is null ? fallbackNow : ParseAnchor(AnchorDate)!.Value;

    /// <summary>Parses a yyyy-MM-dd anchor (culture-independent) to UTC midnight; null if blank or malformed.</summary>
    public static DateTimeOffset? ParseAnchor(string? text) =>
        DateTime.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc))
            : null;

    /// <summary>Returns every problem found; an empty list means the config is usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (RecordCount < 1) errors.Add("recordCount must be at least 1.");
        if (SplitEvery < 1) errors.Add("splitEvery must be at least 1.");
        if (string.IsNullOrWhiteSpace(FileBaseName)) errors.Add("fileBaseName must not be blank.");
        if (AnchorDate is not null && ParseAnchor(AnchorDate) is null) errors.Add($"anchorDate '{AnchorDate}' is not a yyyy-MM-dd date.");
        if (FirstNames.Count == 0) errors.Add("firstNames must contain at least one name.");
        if (LastNames.Count == 0) errors.Add("lastNames must contain at least one name.");
        if (string.IsNullOrWhiteSpace(EmailDomainTemplate)) errors.Add("emailDomainTemplate must not be blank.");

        if (Brands.Count == 0)
        {
            errors.Add("brands must contain at least one brand.");
        }
        for (var i = 0; i < Brands.Count; i++)
        {
            var b = Brands[i];
            if (string.IsNullOrWhiteSpace(b.Code)) errors.Add($"brands[{i}].code must not be blank.");
            if (string.IsNullOrWhiteSpace(b.Name)) errors.Add($"brands[{i}].name must not be blank.");
            if (string.IsNullOrWhiteSpace(b.StorefrontId)) errors.Add($"brands[{i}].storefrontId must not be blank.");
            if (string.IsNullOrWhiteSpace(b.ContactPrefix)) errors.Add($"brands[{i}].contactPrefix must not be blank.");
        }
        foreach (var dup in Brands.Where(b => !string.IsNullOrWhiteSpace(b.Code))
                     .GroupBy(b => b.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"brand code '{dup.Key}' is used more than once.");
        }
        foreach (var dup in Brands.Where(b => !string.IsNullOrWhiteSpace(b.ContactPrefix))
                     .GroupBy(b => b.ContactPrefix, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"brand contactPrefix '{dup.Key}' is used more than once.");
        }

        foreach (var (name, value) in Probabilities.Entries())
        {
            if (double.IsNaN(value) || value < 0 || value > 1)
            {
                errors.Add($"probabilities.{name} must be between 0 and 1 (got {value.ToString(CultureInfo.InvariantCulture)}).");
            }
        }

        foreach (var name in RequiredPicklists)
        {
            if (!Picklists.TryGetValue(name, out var values) || values.Count == 0)
            {
                errors.Add($"picklist '{name}' is missing or empty.");
            }
            else if (values.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"picklist '{name}' contains a blank value.");
            }
        }
        if (Picklists.TryGetValue("cityRegionPostal", out var crp))
        {
            foreach (var entry in crp.Where(e => e.Split('|').Length != 3))
            {
                errors.Add($"picklist 'cityRegionPostal' entry '{entry}' must look like 'City|Region|PostalCode'.");
            }
        }
        if (Picklists.TryGetValue("membershipTier", out var tiers))
        {
            foreach (var t in tiers.Where(t => !MembershipTiers.IsKnown(t)))
            {
                errors.Add($"picklist 'membershipTier' value '{t}' is not one of {string.Join(", ", MembershipTiers.Names)}.");
            }
        }
        return errors;
    }
}

/// <summary>Probabilities (0..1) of each yes/no field in a generated profile.</summary>
public sealed class ProbabilityConfig
{
    public double Employee { get; set; } = 0.05;
    public double Active { get; set; } = 0.90;
    public double EmailConsent { get; set; } = 0.80;
    public double SmsConsent { get; set; } = 0.50;
    public double PushConsent { get; set; } = 0.50;
    public double PostalConsent { get; set; } = 0.60;
    /// <summary>Chance a customer has placed at least one order.</summary>
    public double HasOrders { get; set; } = 0.85;
    /// <summary>Chance a customer has a second (shipping) address.</summary>
    public double SecondAddress { get; set; } = 0.40;
    /// <summary>Chance a membership has a pending tier change (previous_tier is set).</summary>
    public double TierChanged { get; set; } = 0.30;

    internal IEnumerable<(string Name, double Value)> Entries()
    {
        yield return (nameof(Employee), Employee);
        yield return (nameof(Active), Active);
        yield return (nameof(EmailConsent), EmailConsent);
        yield return (nameof(SmsConsent), SmsConsent);
        yield return (nameof(PushConsent), PushConsent);
        yield return (nameof(PostalConsent), PostalConsent);
        yield return (nameof(HasOrders), HasOrders);
        yield return (nameof(SecondAddress), SecondAddress);
        yield return (nameof(TierChanged), TierChanged);
    }
}

/// <summary>One brand a synthesized customer is fanned out across (membership, flat contact fields).</summary>
public sealed class BrandConfig
{
    /// <summary>Short brand code, e.g. "AC".</summary>
    public string Code { get; set; } = "";

    /// <summary>Display name, e.g. "Acme Kids".</summary>
    public string Name { get; set; } = "";

    /// <summary>Storefront the brand sells through; copied onto memberships and events.</summary>
    public string StorefrontId { get; set; } = "";

    /// <summary>Lowercase prefix for the flat contact keys (e.g. "ac" gives "ac_tier").</summary>
    public string ContactPrefix { get; set; } = "";
}

/// <summary>The recognised membership tiers and the points range each one implies.</summary>
public static class MembershipTiers
{
    public static readonly IReadOnlyList<string> Names = new[] { "Bronze", "Silver", "Gold" };

    public static bool IsKnown(string tier) => Names.Contains(tier, StringComparer.OrdinalIgnoreCase);

    /// <summary>Inclusive-exclusive points range for a tier; unknown tiers fall back to the Bronze range.</summary>
    public static (int Min, int Max) PointsRange(string tier) =>
        string.Equals(tier, "Gold", StringComparison.OrdinalIgnoreCase) ? (5000, 15000)
        : string.Equals(tier, "Silver", StringComparison.OrdinalIgnoreCase) ? (1000, 5000)
        : (0, 1000);

    /// <summary>The tier one step up (Gold stays Gold).</summary>
    public static string Next(string tier) =>
        string.Equals(tier, "Bronze", StringComparison.OrdinalIgnoreCase) ? "Silver" : "Gold";

    /// <summary>The tier one step down (Bronze stays Bronze).</summary>
    public static string Previous(string tier) =>
        string.Equals(tier, "Gold", StringComparison.OrdinalIgnoreCase) ? "Silver" : "Bronze";
}
