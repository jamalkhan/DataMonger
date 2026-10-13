using System.Text.Json;

namespace DataMonger.Common;

/// <summary>Thrown when a command config cannot be read or fails validation; <see cref="Errors"/> lists every problem.</summary>
public sealed class CommandConfigException(IReadOnlyList<string> errors)
    : Exception("Invalid config: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Shared by <see cref="PurchaseConfig"/> and <see cref="EmailConversionConfig"/>: JSON with comments and trailing commas
/// allowed, key names matched case-insensitively. Relative file paths resolve against the config file's own folder
/// (a leading <c>~</c> is expanded); omitted paths fall back to the <c>LocalData</c> defaults.
/// </summary>
public static class CommandConfigFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Load<T>(string path) where T : class, IValidatableConfig
    {
        T config;
        try
        {
            config = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                ?? throw new CommandConfigException(new[] { "The config is empty (null)." });
        }
        catch (JsonException ex)
        {
            throw new CommandConfigException(new[] { $"Could not parse JSON: {ex.Message}" });
        }
        var errors = config.Validate();
        if (errors.Count > 0)
        {
            throw new CommandConfigException(errors);
        }
        return config;
    }

    /// <summary>Resolves a configured path: blank -> <paramref name="fallback"/>; <c>~</c> expanded; relative -> under <paramref name="configDir"/>.</summary>
    public static string ResolvePath(string? value, string configDir, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }
        var v = value.Trim();
        if (v == "~" || v.StartsWith("~/", StringComparison.Ordinal) || v.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            v = v == "~" ? home : Path.Combine(home, v[2..]);
        }
        return Path.GetFullPath(v, configDir);
    }
}

public interface IValidatableConfig
{
    IReadOnlyList<string> Validate();
}

/// <summary>Config for the Anonymous / Known purchase commands.</summary>
public sealed class PurchaseConfig : IValidatableConfig
{
    /// <summary>Products CSV (9 columns, header skipped). Default <c>LocalData/products.csv</c>.</summary>
    public string? ProductsFile { get; set; }

    /// <summary>Customers CSV. Default <c>LocalData/anonymous.csv</c> or <c>LocalData/identified.csv</c> depending on the command.</summary>
    public string? CustomersFile { get; set; }

    /// <summary>Each order gets a random number of distinct products between <see cref="MinItems"/> and <see cref="MaxItems"/> inclusive.</summary>
    public int MinItems { get; set; } = 1;

    public int MaxItems { get; set; } = 3;

    /// <summary>Prefix of the generated order id; null keeps the command's default (<c>ORD-</c> / <c>TST-ORD-</c>).</summary>
    public string? OrderIdPrefix { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MinItems < 1)
        {
            errors.Add("minItems must be at least 1.");
        }
        if (MaxItems < MinItems)
        {
            errors.Add("maxItems must be greater than or equal to minItems.");
        }
        return errors;
    }
}

/// <summary>Config for Generate Email Conversion data.</summary>
public sealed class EmailConversionConfig : IValidatableConfig
{
    /// <summary>Products CSV. Default <c>LocalData/products.csv</c>.</summary>
    public string? ProductsFile { get; set; }

    /// <summary>Identified-customers CSV. Default <c>LocalData/identified.csv</c>.</summary>
    public string? CustomersFile { get; set; }

    /// <summary>Output JSONL. Default <c>LocalData/EmailConversion.jsonl</c>.</summary>
    public string? OutputFile { get; set; }

    /// <summary>Optional seed; same seed gives byte-identical output. When omitted the command still asks for one.</summary>
    public int? Seed { get; set; }

    public IReadOnlyList<string> Validate() => Array.Empty<string>();
}
