using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using bleak.Data.Transformer.Output;
using DataMonger.Common;
using DataMonger.Jsonl;
using DataMonger.Logging;
using DataMonger.Synth;
using Microsoft.Extensions.Logging;

namespace DataMonger.Commands;

/// <summary>
/// Generates fake customer-profile JSONL for the invented retailer "Acme Outfitters" (see Documentation/SynthesizeUserProps/Schema.md), without
/// reading any real CSVs. Output is split via <see cref="JsonlSplitWriter"/>; the source of truth is a config file
/// (*.synthconfig) rather than CSV exports: record count and every picklist value pool live there, so running this
/// repeatedly for new test scenarios means editing the config, not this command.
/// </summary>
public partial class SynthesizeUserProps(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Synthesize User Props (fake data)";
    public bool RunsPerSite => false;

    // Site-independent local generator; guard against the per-site run loop re-running it.
    private static bool _completedThisSession;

    /// <summary>Generation progress is logged at Trace once per this many records.</summary>
    private const int TraceChunkRecords = 100_000;

    private readonly ILogger _logger = ctx.LoggerFactory.CreateLogger<SynthesizeUserProps>();

    public Task<(int success, int failure)> Execute()
    {
        _ = ctx; // local file generator: no site/API context needed.
        using var scope = _logger.BeginCommandScope(Name, CommandLogging.LocalSiteId);

        if (_completedThisSession)
        {
            Console.WriteLine("[skip] SynthesizeUserProps already ran this session (it is site-independent).");
            Log.SkippedAlreadyRan(_logger);
            return Task.FromResult((0, 0));
        }

        try
        {
            var configPath = new ConfigFolders(logger: ctx.LoggerFactory.CreateLogger<ConfigFolders>()).PromptForConfig(ConfigKind.Synth);
            if (configPath is null)
            {
                Console.WriteLine("[FAIL] No config file provided or it does not exist. Aborting.");
                Log.NoConfig(_logger);
                return Task.FromResult((0, 1));
            }

            SynthConfig config;
            try
            {
                config = SynthConfigLoader.FromFile(configPath, ctx.LoggerFactory.CreateLogger(typeof(SynthConfigLoader)));
            }
            catch (SynthConfigException ex)
            {
                Console.WriteLine($"[FAIL] Invalid config {configPath}:");
                foreach (var error in ex.Errors)
                {
                    Console.WriteLine($"  - {error}");
                }
                Log.ConfigInvalid(_logger, Path.GetFileName(configPath), ex.Errors.Count);
                return Task.FromResult((0, 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[FAIL] Could not read config: {ex.Message}");
                Log.ConfigUnreadable(_logger, Path.GetFileName(configPath), ex);
                return Task.FromResult((0, 1));
            }

            var recordCount = PromptForRecordCount(config.RecordCount);
            var outputFolder = PromptForOutputFolder();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var rng = SynthPrompts.CreateRandom(config.Seed);
            var now = config.ResolveAnchor(DateTimeOffset.UtcNow);

            Console.WriteLine();
            Console.WriteLine($"Config          : {configPath}");
            Console.WriteLine($"Records         : {recordCount:N0}");
            Console.WriteLine($"Brands          : {string.Join(", ", config.Brands.Select(b => b.Code))}");
            Console.WriteLine($"Seed            : {(config.Seed is { } s ? s.ToString(CultureInfo.InvariantCulture) : "(random)")}");
            Console.WriteLine($"Anchor (now)    : {now:yyyy-MM-dd HH:mm:ss}Z{(config.AnchorDate is null ? " (real clock)" : "")}");
            Console.WriteLine($"Output folder   : {outputFolder}");
            Console.WriteLine($"Split every     : {config.SplitEvery:N0} records");
            Console.WriteLine($"Run timestamp   : {timestamp}");
            if (config.Seed is not null && config.AnchorDate is null)
            {
                Console.WriteLine("[note] A seed without an anchorDate is only reproducible within the same day: dates are relative to the current time.");
            }
            Console.WriteLine();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                Log.Started(_logger, Path.GetFileName(configPath), recordCount, string.Join(",", config.Brands.Select(b => b.Code)),
                    config.Seed is not null, config.AnchorDate is not null, config.SplitEvery, timestamp);
            }

            var sw = Stopwatch.StartNew();
            var synthesizer = new UserPropsSynthesizer(config, rng, now);

            // Step 1: generate exactly one record, rendered compact (for the eventual .jsonl line)
            // and prettified (for the preview file), and let the user sanity-check it before paying
            // for the full run.
            var buffer = new ArrayBufferWriter<byte>();
            using (var compact = new Utf8JsonWriter(buffer, JsonlRender.Options))
            {
                synthesizer.Write(compact, config.StartUserSeq);
                compact.Flush();
            }
            var firstRecordBytes = buffer.WrittenSpan.ToArray();

            var previewPath = Path.Combine(outputFolder, $"{config.FileBaseName}_{timestamp}.json");
            Directory.CreateDirectory(outputFolder);
            using (var doc = JsonDocument.Parse(firstRecordBytes))
            using (var previewStream = new FileStream(previewPath, FileMode.Create, FileAccess.Write))
            using (var pretty = new Utf8JsonWriter(previewStream, new JsonWriterOptions
            {
                Indented = true,
                Encoder = JsonlRender.Options.Encoder,
            }))
            {
                doc.RootElement.WriteTo(pretty);
            }

            Console.WriteLine($"Preview record written: {previewPath}");
            Console.WriteLine();
            Log.PreviewWritten(_logger, Path.GetFileName(previewPath), previewPath, sw.Elapsed.TotalMilliseconds);

            if (!PromptContinueWithFullSet(recordCount))
            {
                Console.WriteLine("Stopped after the preview record. No .jsonl file was written.");
                Log.StoppedAfterPreview(_logger, Path.GetFileName(configPath));
                _completedThisSession = true;
                return Task.FromResult((1, 0));
            }

            // Step 2: the full run. Record #1 is replayed verbatim from the preview so the .jsonl's
            // first line matches what the user just reviewed; records 2..N continue from the same rng.
            var generateStart = Stopwatch.GetTimestamp();
            var traceChunks = _logger.IsEnabled(LogLevel.Trace);
            var chunkStart = generateStart;
            long bytesGenerated = firstRecordBytes.Length;
            using var split = new JsonlSplitWriter(outputFolder, config.FileBaseName, timestamp, config.SplitEvery, ctx.LoggerFactory.CreateLogger<JsonlSplitWriter>());
            split.WriteLine(firstRecordBytes);
            using (var json = new Utf8JsonWriter(buffer, JsonlRender.Options))
            {
                for (var i = 1; i < recordCount; i++)
                {
                    buffer.ResetWrittenCount();
                    json.Reset();
                    synthesizer.Write(json, config.StartUserSeq + i);
                    json.Flush();
                    bytesGenerated += buffer.WrittenCount;
                    split.WriteLine(buffer.WrittenSpan);
                    if (traceChunks && (i + 1) % TraceChunkRecords == 0)
                    {
                        Log.ChunkGenerated(_logger, TraceChunkRecords, i + 1, Stopwatch.GetElapsedTime(chunkStart).TotalMilliseconds);
                        chunkStart = Stopwatch.GetTimestamp();
                    }
                }
            }
            split.Dispose();
            sw.Stop();
            var generateMs = Stopwatch.GetElapsedTime(generateStart).TotalMilliseconds;
            Log.GenerateFinished(_logger, split.RecordsWritten, bytesGenerated, split.FinalPaths.Count, generateMs,
                CommandLogging.RecordsPerSecond(split.RecordsWritten, generateMs));
            foreach (var f in split.FinalPaths)
            {
                Log.OutputFileWritten(_logger, Path.GetFileName(f), f, new FileInfo(f).Length);
            }

            Console.WriteLine("=== Done ===");
            Console.WriteLine($"Synthesized: {split.RecordsWritten:N0} records across {split.FinalPaths.Count} file(s):");
            foreach (var f in split.FinalPaths)
            {
                Console.WriteLine($"    {f}");
            }
            Console.WriteLine($"Elapsed: {sw.Elapsed:hh\\:mm\\:ss\\.fff}");
            Log.Completed(_logger, Path.GetFileName(configPath), split.RecordsWritten, split.FinalPaths.Count, bytesGenerated,
                sw.Elapsed.TotalMilliseconds, CommandLogging.RecordsPerSecond(split.RecordsWritten, sw.Elapsed.TotalMilliseconds));

            _completedThisSession = true;
            return Task.FromResult(((int)Math.Min(int.MaxValue, split.RecordsWritten), 0));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Log.Failed(_logger, ex);
            return Task.FromResult((0, 1));
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Prompts
    // ----------------------------------------------------------------------------------------------
    private static int PromptForRecordCount(int def)
    {
        Console.Write($"Number of customers to synthesize [{def:N0}]: ");
        var input = Console.ReadLine()?.Trim().Replace(",", "");
        if (string.IsNullOrEmpty(input))
        {
            return def;
        }
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
        {
            return n;
        }
        Console.WriteLine($"  [warn] invalid number; using default {def:N0}.");
        return def;
    }

    private static bool PromptContinueWithFullSet(int recordCount)
    {
        while (true)
        {
            Console.Write($"Continue and generate the full set of {recordCount:N0} record(s) as .jsonl? [y/N]: ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input) || string.Equals(input, "n", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (string.Equals(input, "y", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            Console.WriteLine("  [warn] enter 'y' or 'n'.");
        }
    }

    private static string PromptForOutputFolder()
    {
        var def = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(def))
        {
            def = Directory.GetCurrentDirectory();
        }
        Console.Write($"Output folder for the .jsonl file(s) [{def}]: ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(input) ? def : input;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 2000, EventName = "UserPropsStarted", Level = LogLevel.Information,
            Message = "Synthesize user props started: config {JobName}, {RecordCount} record(s), brands [{Brands}], seeded {Seeded}, fixed anchor {FixedAnchor}, split every {BatchSize}, run timestamp {RunTimestamp}")]
        public static partial void Started(ILogger logger, string jobName, int recordCount, string brands, bool seeded, bool fixedAnchor, long batchSize, string runTimestamp);

        [LoggerMessage(EventId = 2001, EventName = "UserPropsCompleted", Level = LogLevel.Information,
            Message = "Synthesize user props completed: config {JobName}, {RecordCount} record(s) across {FileCount} file(s), {ByteCount} bytes in {DurationMs} ms ({RecordsPerSecond} rec/s)")]
        public static partial void Completed(ILogger logger, string jobName, long recordCount, int fileCount, long byteCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 2002, EventName = "UserPropsPreviewWritten", Level = LogLevel.Debug,
            Message = "Preview phase finished: {FileName} written after {DurationMs} ms ({FilePath})")]
        public static partial void PreviewWritten(ILogger logger, string fileName, string filePath, double durationMs);

        [LoggerMessage(EventId = 2003, EventName = "UserPropsGenerateFinished", Level = LogLevel.Debug,
            Message = "Generate phase finished: {RecordCount} record(s), {ByteCount} bytes, {FileCount} file(s) in {DurationMs} ms ({RecordsPerSecond} rec/s)")]
        public static partial void GenerateFinished(ILogger logger, long recordCount, long byteCount, int fileCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 2004, EventName = "UserPropsOutputFileWritten", Level = LogLevel.Debug,
            Message = "Output file {FileName}: {ByteCount} bytes ({FilePath})")]
        public static partial void OutputFileWritten(ILogger logger, string fileName, string filePath, long byteCount);

        [LoggerMessage(EventId = 2005, EventName = "UserPropsChunkGenerated", Level = LogLevel.Trace,
            Message = "Chunk generated: {BatchSize} record(s) in {DurationMs} ms ({RecordCount} so far)")]
        public static partial void ChunkGenerated(ILogger logger, int batchSize, int recordCount, double durationMs);

        [LoggerMessage(EventId = 2006, EventName = "UserPropsStoppedAfterPreview", Level = LogLevel.Information,
            Message = "Stopped after the preview record (config {JobName}); no .jsonl written")]
        public static partial void StoppedAfterPreview(ILogger logger, string jobName);

        [LoggerMessage(EventId = 2007, EventName = "UserPropsConfigInvalid", Level = LogLevel.Error,
            Message = "Config {JobName} is invalid: {ErrorCount} error(s); aborting")]
        public static partial void ConfigInvalid(ILogger logger, string jobName, int errorCount);

        [LoggerMessage(EventId = 2008, EventName = "UserPropsConfigUnreadable", Level = LogLevel.Error,
            Message = "Config {JobName} could not be read; aborting")]
        public static partial void ConfigUnreadable(ILogger logger, string jobName, Exception exception);

        [LoggerMessage(EventId = 2009, EventName = "UserPropsNoConfig", Level = LogLevel.Warning,
            Message = "No config selected; aborting")]
        public static partial void NoConfig(ILogger logger);

        [LoggerMessage(EventId = 2010, EventName = "UserPropsFailed", Level = LogLevel.Error,
            Message = "Synthesize user props failed")]
        public static partial void Failed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 2011, EventName = "UserPropsSkippedAlreadyRan", Level = LogLevel.Information,
            Message = "Skipped: already ran once this session")]
        public static partial void SkippedAlreadyRan(ILogger logger);
    }
}
