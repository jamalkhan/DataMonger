using System.Diagnostics;
using System.Globalization;
using bleak.Data.Transformer;
using bleak.Data.Transformer.Config;
using bleak.Data.Transformer.Engine;
using DataMonger.Common;
using DataMonger.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataMonger.Commands;

/// <summary>
/// Config-driven ETL. Instead of hardcoded sources,
/// joins, field maps, and output shapes, this command reads an ETL config (JSON) describing the sources,
/// relationships, typed fields, transform pipelines, and load settings, and runs the
/// <see cref="EtlEngine"/>. Prompts for the config (from the saved ETL config folder, asking for the folder on first run) and the output folder; everything else
/// (input files, split size, join strategy, error policy) comes from the config.
/// </summary>
public partial class ExtractTransformLoad(
    CommandContext ctx,
    ConfigFolders configFolders,
    IOptions<EtlRunOptions> etlOptions,
    ILoggerFactory loggerFactory,
    ILogger<ExtractTransformLoad> logger) : IConsoleCommand
{
    public string Name => "Extract / Transform / Load (config-driven)";
    public bool RunsPerSite => false;

    // Site-independent local transform; guard against the per-site run loop re-running it.
    private static bool _completedThisSession;

    private readonly ILogger _logger = logger;

    public Task<(int success, int failure)> Execute()
    {
        _ = ctx; // local file transform: no site/API context needed.
        using var scope = _logger.BeginCommandScope(Name, CommandLogging.LocalSiteId);

        if (_completedThisSession)
        {
            Console.WriteLine("[skip] ExtractTransformLoad already ran this session (it is site-independent).");
            Log.SkippedAlreadyRan(_logger);
            return Task.FromResult((0, 0));
        }

        try
        {
            var configPath = configFolders.PromptForConfig(ConfigKind.Etl);
            if (configPath is null)
            {
                Console.WriteLine("[FAIL] No config file provided or it does not exist. Aborting.");
                Log.NoConfig(_logger);
                return Task.FromResult((0, 1));
            }

            EtlConfig config;
            var configStart = Stopwatch.GetTimestamp();
            try
            {
                config = EtlConfigLoader.FromFile(configPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not parse config: {ex.Message}");
                Log.ConfigInvalid(_logger, Path.GetFileName(configPath), ex.GetType().Name);
                return Task.FromResult((0, 1));
            }
            Log.ConfigLoaded(_logger, Path.GetFileName(configPath), configPath, Stopwatch.GetElapsedTime(configStart).TotalMilliseconds);

            var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;

            // Outputs may carry their own load.folder; only prompt for a fallback if some output lacks one.
            var needsFolder = config.Outputs.Any(o => string.IsNullOrWhiteSpace(o.Load.Folder));
            var outputFolder = needsFolder ? PromptForOutputFolder() : "";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            Console.WriteLine();
            Console.WriteLine($"Config        : {configPath}");
            Console.WriteLine($"Sources       : {config.Sources.Count}");
            Console.WriteLine($"Outputs       : {config.Outputs.Count}");
            Console.WriteLine($"Join strategy : {config.Run.JoinStrategy}");
            Console.WriteLine($"On error      : {config.Run.OnError}");
            Console.WriteLine($"Sample        : {(config.Run.Sample is { PrimaryRows: > 0 } s
                ? $"first {s.PrimaryRows:N0} primary row(s), scoped by {s.KeyColumn} (SAMPLE RUN)"
                : "(full run)")}");
            Console.WriteLine($"Max records   : {(config.Run.MaxRecords is > 0 ? $"{config.Run.MaxRecords:N0}" : "(no cap)")}");
            Console.WriteLine($"Output folder : {(needsFolder ? outputFolder : "(from config)")}");
            Console.WriteLine($"Run timestamp : {timestamp}");
            Console.WriteLine();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                Log.Started(_logger, Path.GetFileName(configPath), config.Sources.Count,
                    string.Join(",", config.Sources.Select(source => Path.GetFileName(source.Value.File))), config.Outputs.Count,
                    config.Run.JoinStrategy.ToString(), config.Run.OnError.ToString(), config.Run.Sample is { PrimaryRows: > 0 }, timestamp);
            }

            var overall = Stopwatch.StartNew();
            var results = EtlEngine.Run(config, configDir, outputFolder, timestamp, loggerFactory, etlOptions.Value);
            overall.Stop();
            PrintOutputDetails(config, configDir, outputFolder, timestamp, results);

            Console.WriteLine();
            Console.WriteLine("=== Done ===");
            long total = 0;
            long totalRejected = 0, totalFiltered = 0, totalWarnings = 0, totalFiles = 0, totalBytes = 0;
            foreach (var r in results)
            {
                total += r.Records;
                totalRejected += r.Rejected;
                totalFiltered += r.Filtered;
                totalWarnings += r.Warnings;
                totalFiles += r.Files.Count;
                totalBytes += r.Bytes;
                Console.WriteLine($"{r.Name}: {r.Records:N0} records across {r.Files.Count} file(s)"
                    + (r.Rejected > 0 ? $", {r.Rejected:N0} skipped" : "")
                    + (r.Filtered > 0 ? $", {r.Filtered:N0} filtered out" : "")
                    + (r.Warnings > 0 ? $", {r.Warnings:N0} warnings" : "") + ":");
                foreach (var f in r.Files)
                {
                    Console.WriteLine($"    {f}");
                }
            }
            Console.WriteLine($"Total elapsed: {overall.Elapsed:hh\\:mm\\:ss\\.fff}");
            var durationMs = overall.Elapsed.TotalMilliseconds;
            // Per-output detail (and rejected/warning counts as Warnings) is logged by the engine itself
            // (category bleak.Data.Transformer.Engine.EtlEngine); this is the command-level total.
            Log.Completed(_logger, Path.GetFileName(configPath), results.Count, totalFiles, total, totalRejected, totalFiltered, totalWarnings,
                totalBytes, durationMs, CommandLogging.RecordsPerSecond(total, durationMs));

            _completedThisSession = true;
            return Task.FromResult(((int)Math.Min(int.MaxValue, total), 0));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Log.Failed(_logger, ex);
            return Task.FromResult((0, 1));
        }
    }

    /// <summary>
    /// Prints the per-output progress lines that bleak.Data.Transformer 1.x wrote through its console callback
    /// (run banners, <c>--- name (mode, strategy) ---</c>, output folder, sampling, maxRecords cap, rejects/filtered/
    /// warnings files), rebuilt from the config and the 2.x result objects. The engine's internal detail lines
    /// (lookups compiled, lookup reuse, sample/key-filter scoping, external-sort runs) now go to the log only.
    /// </summary>
    private static void PrintOutputDetails(
        EtlConfig config, string configDir, string fallbackFolder, string timestamp, IReadOnlyList<EtlOutputResult> results)
    {
        for (var i = 0; i < config.Outputs.Count; i++)
        {
            var output = config.Outputs[i];
            var result = results.FirstOrDefault(r => r.Name == output.Name) ?? (i < results.Count ? results[i] : null);
            if (result is null)
            {
                continue;
            }

            if (config.Run.Sample is { PrimaryRows: > 0 } s)
            {
                Console.WriteLine($"[SAMPLE RUN] {output.Name}: first {s.PrimaryRows:N0} '{output.Source}' row(s), "
                    + $"scoped by {s.KeyColumn}; children are complete for those parents.");
            }
            if (config.Run.KeyFilter is { Values.Count: > 0 } kf)
            {
                Console.WriteLine($"[KEY FILTER RUN] {output.Name}: '{output.Source}' scoped to {kf.Values.Count:N0} "
                    + $"explicit {kf.KeyColumn} value(s); children are complete for those parents.");
            }
            Console.WriteLine($"--- {result.Name} ({output.Mode}, {config.Run.JoinStrategy}) ---");

            var runFolder = result.Files.Count > 0
                ? Path.GetDirectoryName(result.Files[0])!
                : RunFolder(output.Load.Folder, fallbackFolder, configDir, timestamp);
            Console.WriteLine($"    output folder: {runFolder}");

            PrintSamples(runFolder, output.Load.Sampling is { Enabled: true } sampling ? sampling.Folder : null, "sampling");
            PrintSamples(runFolder, output.Load.RichSampling is { Enabled: true } rich ? rich.Folder : null, "rich sampling");

            if (config.Run.MaxRecords is > 0 and var max && result.Records >= max)
            {
                Console.WriteLine($"    stopped at run.maxRecords = {max:N0} record(s); the feed is incomplete.");
            }
            var baseName = string.IsNullOrWhiteSpace(output.Load.FileBaseName) ? output.Name : output.Load.FileBaseName;
            string SideFile(string kind) => Path.Combine(runFolder, $"{baseName}_{timestamp}_{kind}.csv");
            if (result.Rejected > 0)
            {
                Console.WriteLine($"    {result.Rejected:N0} record(s) skipped -> {SideFile("rejects")}");
            }
            if (result.Filtered > 0)
            {
                var filteredPath = SideFile("filtered");
                Console.WriteLine($"    {result.Filtered:N0} record(s) filtered out" + (File.Exists(filteredPath) ? $" -> {filteredPath}" : ""));
            }
            if (result.Warnings > 0)
            {
                Console.WriteLine($"    {result.Warnings:N0} warning(s) (record kept) -> {SideFile("warnings")}");
            }
        }
    }

    private static void PrintSamples(string runFolder, string? samplingFolder, string label)
    {
        if (string.IsNullOrWhiteSpace(samplingFolder))
        {
            return;
        }
        var dir = Path.Combine(runFolder, samplingFolder);
        var written = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Length : 0;
        if (written > 0)
        {
            Console.WriteLine($"    {label}: wrote {written:N0} record(s) to {dir}");
        }
    }

    /// <summary>Same folder rule as the engine: load.folder (or the fallback) resolved against the config folder,
    /// with {run_timestamp} replaced, or run_&lt;timestamp&gt; appended.</summary>
    private static string RunFolder(string? loadFolder, string fallback, string configDir, string timestamp)
    {
        const string token = "{run_timestamp}";
        var raw = string.IsNullOrWhiteSpace(loadFolder) ? fallback : loadFolder;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Directory.GetCurrentDirectory();
        }
        var baseFolder = PathUtil.Resolve(raw, configDir);
        return baseFolder.Contains(token, StringComparison.Ordinal)
            ? baseFolder.Replace(token, $"run_{timestamp}")
            : Path.Combine(baseFolder, $"run_{timestamp}");
    }

    private static string PromptForOutputFolder()
    {
        var def = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(def))
        {
            def = Directory.GetCurrentDirectory();
        }
        Console.Write($"Output folder for the .jsonl files [{def}]: ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(input) ? def : input;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1300, EventName = "EtlStarted", Level = LogLevel.Information,
            Message = "ETL started: config {JobName}, {SourceCount} source(s) [{InputFiles}], {OutputCount} output(s), join strategy {JoinStrategy}, on error {OnError}, sample run {SampleRun}, run timestamp {RunTimestamp}")]
        public static partial void Started(ILogger logger, string jobName, int sourceCount, string inputFiles, int outputCount, string joinStrategy, string onError, bool sampleRun, string runTimestamp);

        [LoggerMessage(EventId = 1301, EventName = "EtlCompleted", Level = LogLevel.Information,
            Message = "ETL completed: config {JobName}, {OutputCount} output(s), {FileCount} file(s), {RecordCount} records, {SkippedCount} rejected, {FilteredCount} filtered, {WarningCount} warnings, {ByteCount} bytes in {DurationMs} ms ({RecordsPerSecond} rec/s)")]
        public static partial void Completed(ILogger logger, string jobName, int outputCount, long fileCount, long recordCount, long skippedCount, long filteredCount, long warningCount, long byteCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 1304, EventName = "EtlConfigLoaded", Level = LogLevel.Debug,
            Message = "Config {JobName} loaded in {DurationMs} ms ({FilePath})")]
        public static partial void ConfigLoaded(ILogger logger, string jobName, string filePath, double durationMs);

        [LoggerMessage(EventId = 1305, EventName = "EtlConfigInvalid", Level = LogLevel.Error,
            Message = "Config {JobName} could not be parsed ({ExceptionType}); aborting")]
        public static partial void ConfigInvalid(ILogger logger, string jobName, string exceptionType);

        [LoggerMessage(EventId = 1306, EventName = "EtlNoConfig", Level = LogLevel.Warning,
            Message = "No config selected; aborting")]
        public static partial void NoConfig(ILogger logger);

        [LoggerMessage(EventId = 1307, EventName = "EtlFailed", Level = LogLevel.Error,
            Message = "ETL failed")]
        public static partial void Failed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1308, EventName = "EtlSkippedAlreadyRan", Level = LogLevel.Information,
            Message = "Skipped: already ran once this session")]
        public static partial void SkippedAlreadyRan(ILogger logger);
    }
}
