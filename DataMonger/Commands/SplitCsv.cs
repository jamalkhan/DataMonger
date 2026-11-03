using System.Diagnostics;
using System.Globalization;
using bleak.Data.InfiniteDataLoader;
using bleak.Data.Transformer;
using DataMonger.Common;
using DataMonger.Logging;
using Microsoft.Extensions.Logging;

namespace DataMonger.Commands;

/// <summary>
/// Splits a large CSV into a series of smaller files, each with at most a fixed number of data rows
/// (default 100,000), dropped into an output folder. The header is repeated at the top of every part.
///
/// Like <see cref="ExtractTransformLoad"/> and <see cref="AnonymizePii"/>, this is a site-independent
/// local file transform: it prompts only for the source file, the output folder, and the rows-per-file,
/// then streams the input via <see cref="CsvSplitter"/> so it scales to multi-GB inputs. The original is
/// never modified or deleted.
/// </summary>
public partial class SplitCsv(
    CommandContext ctx,
    ILoggerFactory loggerFactory,
    ILogger<SplitCsv> logger) : IConsoleCommand
{
    public string Name => "Split CSV into N-row files";
    public bool RunsPerSite => false;

    private const int DefaultRowsPerFile = 100_000;

    // Site-independent local transform; guard against the per-site run loop re-running it.
    private static bool _completedThisSession;

    private readonly ILogger _logger = logger;

    public Task<(int success, int failure)> Execute()
    {
        _ = ctx; // local file transform: no site/API context needed.
        using var scope = _logger.BeginCommandScope(Name, CommandLogging.LocalSiteId);

        if (_completedThisSession)
        {
            Console.WriteLine("[skip] SplitCsv already ran this session (it is site-independent).");
            Log.SkippedAlreadyRan(_logger);
            return Task.FromResult((0, 0));
        }

        try
        {
            var inputPath = PromptForInput();
            if (inputPath is null)
            {
                Console.WriteLine("[FAIL] No source CSV provided or it does not exist. Aborting.");
                Log.NoInput(_logger);
                return Task.FromResult((0, 1));
            }

            var defaultFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputPath))!, "split");
            var outputFolder = PromptForOutputFolder(defaultFolder);
            var rowsPerFile = PromptForRowsPerFile();

            Console.WriteLine();
            Console.WriteLine($"Source        : {inputPath}");
            Console.WriteLine($"Output folder : {outputFolder}");
            Console.WriteLine($"Rows per file : {rowsPerFile:N0}");
            Console.WriteLine();

            var inputBytes = new FileInfo(inputPath).Length;
            Log.Started(_logger, Path.GetFileName(inputPath), inputBytes, rowsPerFile);
            Log.Paths(_logger, inputPath, outputFolder);

            var overall = Stopwatch.StartNew();
            var result = CsvSplitter.Split(inputPath, outputFolder, rowsPerFile, loggerFactory: loggerFactory);
            overall.Stop();
            // bleak.Data.InfiniteDataLoader 1.x printed these lines through its console callback; 2.x logs them
            // instead (category bleak.Data.InfiniteDataLoader.CsvSplitter), so rebuild them from the result.
            if (result.Rows == 0)
            {
                Console.WriteLine($"[split] {Path.GetFileName(inputPath)} has no data rows — nothing written.");
            }
            foreach (var part in result.Files)
            {
                Console.WriteLine($"[split] writing {Path.GetFileName(part)}");
            }

            Console.WriteLine();
            Console.WriteLine("=== Done ===");
            Console.WriteLine($"{result.Rows:N0} data rows split across {result.Files.Count} file(s):");
            foreach (var f in result.Files)
            {
                Console.WriteLine($"    {f}");
            }
            Console.WriteLine($"Total elapsed: {overall.Elapsed:hh\\:mm\\:ss\\.fff}");
            var durationMs = overall.Elapsed.TotalMilliseconds;
            Log.Completed(_logger, Path.GetFileName(inputPath), result.Rows, result.Files.Count, inputBytes, rowsPerFile, durationMs,
                CommandLogging.RecordsPerSecond(result.Rows, durationMs));

            _completedThisSession = true;
            return Task.FromResult((result.Files.Count, 0));
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
    /// Prompts for the source CSV. Accepts a direct path, a <c>~</c>-relative path, or a glob such as
    /// <c>/path/to/exports/customers_*.csv</c>: a single glob match is used
    /// directly, multiple matches are listed for selection by ordinal. Returns null if cancelled.
    /// </summary>
    private static string? PromptForInput()
    {
        Console.WriteLine();
        while (true)
        {
            Console.Write("Source CSV path (a glob like .../customers_*.csv is allowed), or 'q' to cancel: ");
            var line = Console.ReadLine();
            if (line is null)
            {
                return null; // EOF / no console input -> cancel (don't spin)
            }

            var input = line.Trim().Trim('"');
            if (input.Length == 0)
            {
                continue;
            }
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var path = PathUtil.ExpandHome(input);

            if (path.Contains('*') || path.Contains('?'))
            {
                var chosen = ResolveGlob(path);
                if (chosen is not null)
                {
                    return chosen;
                }
                continue;
            }

            if (File.Exists(path))
            {
                return path;
            }

            Console.WriteLine($"  [warn] file not found: {input}");
        }
    }

    /// <summary>
    /// Resolves a glob to a single file: uses it when exactly one file matches, lists the matches for an
    /// ordinal pick when several do, and returns null (re-prompt) when none match or the user cancels.
    /// </summary>
    private static string? ResolveGlob(string globPath)
    {
        var dir = Path.GetDirectoryName(globPath);
        var pattern = Path.GetFileName(globPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Console.WriteLine($"  [warn] directory not found: {dir}");
            return null;
        }

        var matches = Directory.GetFiles(dir, pattern)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
        {
            Console.WriteLine($"  [warn] no files match: {globPath}");
            return null;
        }
        if (matches.Count == 1)
        {
            Console.WriteLine($"  [match] {matches[0]}");
            return matches[0];
        }

        Console.WriteLine($"  {matches.Count} files match:");
        for (var i = 0; i < matches.Count; i++)
        {
            Console.WriteLine($"    {i + 1}) {Path.GetFileName(matches[i])}");
        }
        while (true)
        {
            Console.Write($"  Select 1-{matches.Count}, or 'b' to re-enter: ");
            var sel = Console.ReadLine()?.Trim();
            if (sel is null || string.Equals(sel, "b", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (int.TryParse(sel, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && n >= 1 && n <= matches.Count)
            {
                return matches[n - 1];
            }
        }
    }

    private static string PromptForOutputFolder(string def)
    {
        Console.Write($"Output folder for the split files [{def}]: ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(input) ? def : PathUtil.ExpandHome(input);
    }

    private static int PromptForRowsPerFile()
    {
        while (true)
        {
            Console.Write($"Rows per file [{DefaultRowsPerFile:N0}]: ");
            var input = Console.ReadLine()?.Trim().Replace(",", "");
            if (string.IsNullOrEmpty(input))
            {
                return DefaultRowsPerFile;
            }
            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1)
            {
                return n;
            }
            Console.WriteLine("  [warn] enter a positive whole number.");
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1700, EventName = "SplitCsvStarted", Level = LogLevel.Information,
            Message = "Split CSV started: {FileName} ({ByteCount} bytes), {BatchSize} rows per file")]
        public static partial void Started(ILogger logger, string fileName, long byteCount, int batchSize);

        [LoggerMessage(EventId = 1701, EventName = "SplitCsvPaths", Level = LogLevel.Debug,
            Message = "Split CSV input {FilePath}, output folder {OutputFolder}")]
        public static partial void Paths(ILogger logger, string filePath, string outputFolder);

        [LoggerMessage(EventId = 1702, EventName = "SplitCsvCompleted", Level = LogLevel.Information,
            Message = "Split CSV completed: {FileName}, {RowCount} rows into {FileCount} file(s) ({ByteCount} input bytes, {BatchSize} rows per file) in {DurationMs} ms ({RecordsPerSecond} rows/s)")]
        public static partial void Completed(ILogger logger, string fileName, long rowCount, int fileCount, long byteCount, int batchSize, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 1703, EventName = "SplitCsvNoInput", Level = LogLevel.Warning,
            Message = "No source CSV selected; aborting")]
        public static partial void NoInput(ILogger logger);

        [LoggerMessage(EventId = 1704, EventName = "SplitCsvFailed", Level = LogLevel.Error,
            Message = "Split CSV failed")]
        public static partial void Failed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1705, EventName = "SplitCsvSkippedAlreadyRan", Level = LogLevel.Information,
            Message = "Skipped: already ran once this session")]
        public static partial void SkippedAlreadyRan(ILogger logger);
    }
}
