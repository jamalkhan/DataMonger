using System.Text.Json;

namespace DataMonger.Synth;

/// <summary>Thrown when a config cannot be parsed or fails validation; <see cref="Errors"/> lists every problem.</summary>
public sealed class SynthConfigException(IReadOnlyList<string> errors)
    : Exception("Invalid synth config: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Loads a <see cref="SynthConfig"/> from JSON. Comments and trailing commas are allowed so the
/// config can be annotated (jsonc-style). Property names are matched case-insensitively.</summary>
public static class SynthConfigLoader
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses and validates. Throws <see cref="SynthConfigException"/> on any problem.</summary>
    public static SynthConfig FromJson(string json)
    {
        SynthConfig config;
        try
        {
            config = JsonSerializer.Deserialize<SynthConfig>(json, Options)
                ?? throw new SynthConfigException(new[] { "The config is empty (null)." });
        }
        catch (JsonException ex)
        {
            throw new SynthConfigException(new[] { $"Could not parse JSON: {ex.Message}" });
        }

        var errors = config.Validate();
        if (errors.Count > 0)
        {
            throw new SynthConfigException(errors);
        }
        return config;
    }

    public static SynthConfig FromFile(string path) => FromJson(File.ReadAllText(path));
}
