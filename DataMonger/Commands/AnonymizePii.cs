using System.Diagnostics;
using System.Globalization;
using bleak.Data.Anonymizer;
using bleak.Data.Anonymizer.Config;
using bleak.Data.Anonymizer.Engine;
using DataMonger.Common;
using DataMonger.Logging;
using Microsoft.Extensions.Logging;

namespace DataMonger.Commands;

/// <summary>
/// Config-driven PII anonymization. Reads an <c>.anonconfig</c> describing, per input file, which columns
/// to replace with obviously-fake values (Simpsons-based names/e-mails, 555 phone numbers, etc.), and writes
/// the results to a separate output folder with each file renamed (e.g. <c>src_</c> → <c>anonymized_</c>).
///
/// Originals are never modified or deleted. Prompts for the config (from the saved anonymization config folder, asking for the folder on first run) and (when the config omits it) the
/// output folder; everything else — input files, columns, methods — comes from the config. Mirrors
/// <see cref="ExtractTransformLoad"/>.
/// </summary>
public partial class AnonymizePii(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Anonymize PII (config-driven)";
    public bool RunsPerSite => false;

    // Site-independent local transform; guard against the per-site run loop re-running it.
    private static bool _completedThisSession;

    private readonly ILogger _logger = ctx.LoggerFactory.CreateLogger<AnonymizePii>();

    public Task<(int success, int failure)> Execute()
    {
        _ = ctx; // local file transform: no site/API context needed.
        using var scope = _logger.BeginCommandScope(Name, CommandLogging.LocalSiteId);

        if (_completedThisSession)
        {
            Console.WriteLine("[skip] AnonymizePii already ran this session (it is site-independent).");
            Log.SkippedAlreadyRan(_logger);
            return Task.FromResult((0, 0));
        }

        try
        {
            var configPath = new ConfigFolders(logger: ctx.LoggerFactory.CreateLogger<ConfigFolders>()).PromptForConfig(ConfigKind.Anon);
            if (configPath is null)
            {
                Console.WriteLine("[FAIL] No config file provided or it does not exist. Aborting.");
                Log.NoConfig(_logger);
                return Task.FromResult((0, 1));
            }

            AnonConfig config;
            var configStart = Stopwatch.GetTimestamp();
            try
            {
                config = AnonConfigLoader.FromFile(configPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not parse config: {ex.Message}");
                Log.ConfigInvalid(_logger, Path.GetFileName(configPath), ex.GetType().Name);
                return Task.FromResult((0, 1));
            }
            Log.ConfigLoaded(_logger, Path.GetFileName(configPath), configPath, Stopwatch.GetElapsedTime(configStart).TotalMilliseconds);

            var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;

            // Only prompt for a fallback output folder when the config doesn't set one.
            var needsFolder = string.IsNullOrWhiteSpace(config.OutputFolder);
            var outputFolder = needsFolder ? PromptForOutputFolder() : null;

            Console.WriteLine();
            Console.WriteLine($"Config        : {configPath}");
            Console.WriteLine($"Files         : {config.Files.Count}");
            Console.WriteLine($"On error      : {config.Run.OnError}");
            Console.WriteLine($"Output folder : {(needsFolder ? outputFolder : "(from config)")}");
            Console.WriteLine($"Rename        : {config.Run.InputPrefix}* -> {config.Run.OutputPrefix}*");
            Console.WriteLine();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                Log.Started(_logger, Path.GetFileName(configPath), config.Files.Count,
                    string.Join(",", config.Files.Select(file => Path.GetFileName(file.Input))), config.Run.OnError.ToString(), needsFolder);
            }

            var overall = Stopwatch.StartNew();
            var results = AnonymizeEngine.Run(config, configDir, outputFolder, registry: null, loggerFactory: ctx.LoggerFactory);
            overall.Stop();
            PrintFileDetails(config, results);

            Console.WriteLine();
            Console.WriteLine("=== Done ===");
            long totalRows = 0, totalCells = 0, failures = 0, totalBytesIn = 0, totalBytesOut = 0;
            foreach (var r in results)
            {
                if (r.Skipped is not null)
                {
                    failures++;
                    Console.WriteLine($"{Path.GetFileName(r.Input)}: SKIPPED ({r.Skipped})");
                    continue;
                }
                totalRows += r.Rows;
                totalCells += r.CellsAnonymized;
                totalBytesIn += r.InputBytes;
                totalBytesOut += r.OutputBytes;
                var detail = r.Copied
                    ? "copied (no PII columns)"
                    : $"{r.Rows:N0} rows, {r.CellsAnonymized:N0} cells anonymized"
                      + (r.Errors > 0 ? $", {r.Errors:N0} error(s)" : "")
                      + (r.MissingColumns.Count > 0 ? $", missing columns: {string.Join(", ", r.MissingColumns)}" : "");
                Console.WriteLine($"{Path.GetFileName(r.Output)}: {detail}");
            }
            Console.WriteLine();
            Console.WriteLine($"Files: {results.Count}, rows anonymized: {totalRows:N0}, cells: {totalCells:N0}");
            Console.WriteLine($"Total elapsed: {overall.Elapsed:hh\\:mm\\:ss\\.fff}");
            var durationMs = overall.Elapsed.TotalMilliseconds;
            // Per-file detail (rows, cells, bytes, DurationMs, skipped files, missing columns, cell errors) is logged by
            // the engine itself (category bleak.Data.Anonymizer.Engine.AnonymizeEngine); this is the command-level total.
            Log.Completed(_logger, Path.GetFileName(configPath), results.Count, (int)failures, totalRows, totalCells, totalBytesIn, totalBytesOut,
                durationMs, CommandLogging.RecordsPerSecond(totalRows, durationMs));

            _completedThisSession = true;
            return Task.FromResult(((int)Math.Min(int.MaxValue, totalRows), (int)failures));
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
    /// Prints the per-file progress lines that bleak.Data.Anonymizer 1.x wrote through its console callback
    /// (<c>[skip]</c>, <c>[warn] ... not found in header</c>, <c>[copy]</c>, <c>[anon]</c>), rebuilt from the config
    /// and the 2.x <see cref="FileResult"/>s. 1.x also printed one line per failed cell, quoting the exception
    /// message; 2.x reports only the count, so that becomes one summary line per file (the engine logs the first
    /// failed cells' row numbers and columns at Debug).
    /// </summary>
    private static void PrintFileDetails(AnonConfig config, IReadOnlyList<FileResult> results)
    {
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var input = Path.GetFileName(r.Input);
            if (r.Skipped is not null)
            {
                Console.WriteLine($"[skip] {input}: {r.Skipped}");
                continue;
            }
            foreach (var column in r.MissingColumns)
            {
                Console.WriteLine($"[warn] {input}: column '{column}' not found in header — skipped.");
            }
            if (r.Copied)
            {
                Console.WriteLine($"[copy] {input} -> {Path.GetFileName(r.Output)} (no PII columns)");
                continue;
            }
            if (r.Errors > 0)
            {
                Console.WriteLine($"[warn] {input}: {r.Errors:N0} cell(s) left unchanged after anonymizer errors.");
            }
            var configured = i < config.Files.Count ? config.Files[i].Columns.Count : r.MissingColumns.Count;
            Console.WriteLine($"[anon] {input} -> {Path.GetFileName(r.Output)}: "
                + $"{r.Rows:N0} rows, {r.CellsAnonymized:N0} cells across {configured - r.MissingColumns.Count} column(s)"
                + (r.Errors > 0 ? $", {r.Errors:N0} error(s)" : ""));
        }
    }

    private static string PromptForOutputFolder()
    {
        var def = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(def))
        {
            def = Directory.GetCurrentDirectory();
        }
        Console.Write($"Output folder for the anonymized files [{def}]: ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(input) ? def : input;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1000, EventName = "AnonymizeStarted", Level = LogLevel.Information,
            Message = "Anonymize PII started: config {JobName}, {FileCount} file(s) [{InputFiles}], on error {OnError}, output folder prompted: {OutputFolderPrompted}")]
        public static partial void Started(ILogger logger, string jobName, int fileCount, string inputFiles, string onError, bool outputFolderPrompted);

        [LoggerMessage(EventId = 1001, EventName = "AnonymizeCompleted", Level = LogLevel.Information,
            Message = "Anonymize PII completed: config {JobName}, {FileCount} file(s), {SkippedCount} skipped, {RowCount} rows, {CellCount} cells, {ByteCount} bytes in, {OutputByteCount} bytes out in {DurationMs} ms ({RecordsPerSecond} rows/s)")]
        public static partial void Completed(ILogger logger, string jobName, int fileCount, int skippedCount, long rowCount, long cellCount, long byteCount, long outputByteCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 1004, EventName = "AnonymizeConfigLoaded", Level = LogLevel.Debug,
            Message = "Config {JobName} loaded in {DurationMs} ms ({FilePath})")]
        public static partial void ConfigLoaded(ILogger logger, string jobName, string filePath, double durationMs);

        [LoggerMessage(EventId = 1005, EventName = "AnonymizeConfigInvalid", Level = LogLevel.Error,
            Message = "Config {JobName} could not be parsed ({ExceptionType}); aborting")]
        public static partial void ConfigInvalid(ILogger logger, string jobName, string exceptionType);

        [LoggerMessage(EventId = 1006, EventName = "AnonymizeNoConfig", Level = LogLevel.Warning,
            Message = "No config selected; aborting")]
        public static partial void NoConfig(ILogger logger);

        [LoggerMessage(EventId = 1007, EventName = "AnonymizeFailed", Level = LogLevel.Error,
            Message = "Anonymize PII failed")]
        public static partial void Failed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1008, EventName = "AnonymizeSkippedAlreadyRan", Level = LogLevel.Information,
            Message = "Skipped: already ran once this session")]
        public static partial void SkippedAlreadyRan(ILogger logger);
    }
}
