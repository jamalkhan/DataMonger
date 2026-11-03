using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DataMonger.Synth;

/// <summary>Thrown when a config cannot be parsed or fails validation; <see cref="Errors"/> lists every problem.</summary>
public sealed class SynthConfigException(IReadOnlyList<string> errors)
    : Exception("Invalid synth config: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Loads a <see cref="SynthConfig"/> from JSON. Comments and trailing commas are allowed so the
/// config can be annotated (jsonc-style). Property names are matched case-insensitively.</summary>
public static partial class SynthConfigLoader
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

    /// <summary>Reads, parses and validates a config file. The optional logger (category
    /// <c>DataMonger.Synth.SynthConfigLoader</c>) gets one Debug line with size and load time, or a Warning with the
    /// error count when the config is invalid.</summary>
    public static SynthConfig FromFile(string path, ILogger? logger = null)
    {
        var start = Stopwatch.GetTimestamp();
        var json = File.ReadAllText(path);
        try
        {
            var config = FromJson(json);
            if (logger is not null)
            {
                Log.Loaded(logger, Path.GetFileName(path), path, json.Length, config.RecordCount, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            return config;
        }
        catch (SynthConfigException ex)
        {
            if (logger is not null)
            {
                Log.Invalid(logger, Path.GetFileName(path), ex.Errors.Count);
            }
            throw;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 3100, EventName = "SynthConfigLoaded", Level = LogLevel.Debug,
            Message = "Synth config {FileName} loaded: {CharCount} chars, record count {RecordCount}, in {DurationMs} ms ({FilePath})")]
        public static partial void Loaded(ILogger logger, string fileName, string filePath, int charCount, int recordCount, double durationMs);

        [LoggerMessage(EventId = 3101, EventName = "SynthConfigInvalid", Level = LogLevel.Warning,
            Message = "Synth config {FileName} is invalid: {ErrorCount} error(s)")]
        public static partial void Invalid(ILogger logger, string fileName, int errorCount);
    }
}
