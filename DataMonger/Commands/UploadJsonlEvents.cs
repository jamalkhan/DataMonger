using bleak.Api.Rest;
using DataMonger.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace DataMonger.Commands;

// Uploads records from a LocalData events JSONL feed to the "track an event" API:
//   POST {baseUrl}/ver2/{siteId}/activities
// Each JSONL line is already the full request body and is sent through as-is — no reshaping,
// no per-line resource id in the URL. Auth is HTTP Basic using the Username/Password of whichever
// Sites entry (appsettings.json) the user picks by number.
// Requests are fired concurrently up to ctx.MaxParallelism, since this is a plain feed upload with
// no per-record ordering dependency.
//
// This command runs its own site picker rather than relying on the outer Program.cs per-site loop
// (which iterates every Sites entry), so it must guard against being invoked once per configured
// site and instead run exactly once per process, the same way UploadJsonlResources does.
public class UploadJsonlEvents(CommandContext ctx) : IConsoleCommand
{
    public string Name => "API => Upload JSONL Events (track event)";

    private static bool _completedThisSession;

    private readonly ILogger _logger = ctx.LoggerFactory.CreateLogger<UploadJsonlEvents>();

    private static readonly object ProcessedFilesLogLock = new();

    /// <summary>Cumulative record of every file this command has ever finished processing (one line
    /// per file: timestamp, path, and a 1-line success/failure/elapsed summary) — appended to across
    /// runs, so "which files already ran" can be answered by reading it instead of console scrollback.</summary>
    private static string ProcessedFilesLogPath => DataPaths.Combine("logs", "events_upload", "processed_files.log");

    /// <summary>Per-run failure detail log: one line per failed request (file, line number, status,
    /// reason). Created lazily — only if at least one request in the run actually fails.</summary>
    private static string FailuresLogPath(string runTimestamp) =>
        DataPaths.Combine("logs", "events_upload", $"failures_{runTimestamp}.log");

    public Task<(int success, int failure)> Execute() => Execute(filePaths: null);

    /// <summary>
    /// Runs the upload, optionally against a pre-selected set of files (e.g. handed off by
    /// Synthesize Session Events right after it generates them) instead of prompting for one.
    /// The site is still prompted for once regardless of how many files are uploaded.
    /// </summary>
    public async Task<(int success, int failure)> Execute(IReadOnlyList<string>? filePaths)
    {
        var skipPreviews = false;

        if (filePaths is null)
        {
            if (_completedThisSession)
            {
                Console.WriteLine("[skip] UploadJsonlEvents already ran this session (it picks its own site).");
                _logger.LogInformation("Skipped: already ran once this session.");
                return (0, 0);
            }
            _completedThisSession = true;

            var jsonlDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LocalData", "jsonl", "events", "Loads");
            jsonlDir = Path.GetFullPath(jsonlDir);
            if (!Directory.Exists(jsonlDir))
            {
                jsonlDir = Path.Combine(Directory.GetCurrentDirectory(), "LocalData", "jsonl", "events", "Loads");
            }

            var resolved = PromptForPaths(jsonlDir);
            if (resolved is null)
            {
                Console.WriteLine("[FAIL] No file(s) selected. Aborting.");
                _logger.LogWarning("Aborting: no file(s) selected.");
                return (0, 1);
            }
            filePaths = resolved.Paths;
            skipPreviews = resolved.FromFolder;
        }
        else if (filePaths.Count == 0)
        {
            Console.WriteLine("[FAIL] No files to upload. Aborting.");
            _logger.LogWarning("Aborting: caller passed an empty file list.");
            return (0, 1);
        }

        var site = PromptForSite(ctx.AllSites);
        if (site is null)
        {
            Console.WriteLine("[FAIL] No site selected. Aborting.");
            _logger.LogWarning("Aborting: no site selected.");
            return (0, 1);
        }

        var siteId = site.SiteId;
        var username = site.Username;
        var password = site.Password;
        var baseUrl = site.BaseUrl.TrimEnd('/');

        var uri = new Uri($"{baseUrl}/ver2/{siteId}/activities");
        var runTimestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        Console.WriteLine();
        Console.WriteLine($"Site ID: {siteId}");
        Console.WriteLine($"Target:  {uri}");
        Console.WriteLine($"Parallelism: {ctx.MaxParallelism}");
        if (skipPreviews)
        {
            Console.WriteLine("(folder upload: skipping the first-request/first-response preview prompts for every file)");
        }

        _logger.LogInformation(
            "Starting run: {FileCount} file(s), site {SiteId}, target {Uri}, skipPreviews={SkipPreviews}, runTimestamp={RunTimestamp}.",
            filePaths.Count, siteId, uri, skipPreviews, runTimestamp);

        using var failuresLog = new FailureLogWriter(FailuresLogPath(runTimestamp));

        var totalSuccess = 0;
        var totalFailure = 0;
        for (var fileIndex = 0; fileIndex < filePaths.Count; fileIndex++)
        {
            var filePath = filePaths[fileIndex];
            Console.WriteLine();
            Console.WriteLine($"--- File {fileIndex + 1}/{filePaths.Count}: {filePath} ---");
            var (fileSuccess, fileFailure) = await UploadFile(filePath, uri, siteId, username, password, skipPreviews, failuresLog);
            totalSuccess += fileSuccess;
            totalFailure += fileFailure;
        }

        if (filePaths.Count > 1)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {Name} grand total ===  Site: {siteId}   Success: {totalSuccess:N0}   Failed: {totalFailure:N0}");
        }

        _logger.LogInformation(
            "Run complete: {FileCount} file(s), site {SiteId}, success={Success}, failed={Failed}.",
            filePaths.Count, siteId, totalSuccess, totalFailure);
        if (failuresLog.WroteAny)
        {
            Console.WriteLine($"Failure detail log: {failuresLog.Path}");
        }

        return (totalSuccess, totalFailure);
    }

    private async Task<(int success, int failure)> UploadFile(
        string filePath, Uri uri, string siteId, string username, string password, bool skipPreviews, FailureLogWriter failuresLog)
    {
        Console.WriteLine($"File: {filePath}");
        Console.WriteLine();
        _logger.LogInformation("File {FilePath}: starting.", filePath);

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Could not read file: {ex.Message}");
            _logger.LogError(ex, "File {FilePath}: could not be read.", filePath);
            AppendProcessedFile(filePath, 0, 1, TimeSpan.Zero, "could not be read");
            return (0, 1);
        }

        var firstLineIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (firstLineIndex < 0)
        {
            Console.WriteLine("[FAIL] File has no non-blank lines to send.");
            _logger.LogWarning("File {FilePath}: no non-blank lines to send.", filePath);
            AppendProcessedFile(filePath, 0, 1, TimeSpan.Zero, "no non-blank lines");
            return (0, 1);
        }
        var firstLine = lines[firstLineIndex];

        if (!skipPreviews && !PreviewRequestAndConfirm(uri, firstLine, lines.Length))
        {
            Console.WriteLine("[ABORT] Upload cancelled.");
            _logger.LogInformation("File {FilePath}: cancelled by user at the first-request preview.", filePath);
            return (0, 0);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var successCount = 0;
        var failureCount = 0;

        // Send the first record on its own, outside the parallel batch, so its response can be
        // previewed and confirmed before the rest of the file is fired off behind it.
        var firstResult = await SendLine(uri, firstLine, username, password, ctx.Rest, CancellationToken.None);
        var firstOk = (int)firstResult.Status is >= 200 and < 300;
        if (firstOk)
        {
            successCount++;
        }
        else
        {
            failureCount++;
            PrintFailure(1, firstResult);
            LogFailure(failuresLog, filePath, 1, firstResult);
        }

        var remaining = lines
            .Select((line, index) => (line, lineNum: index + 1))
            .Where(item => item.lineNum - 1 != firstLineIndex)
            .ToList();

        if (remaining.Count > 0 && !skipPreviews)
        {
            if (!PreviewResponseAndConfirm(firstResult, remaining.Count))
            {
                stopwatch.Stop();
                Console.WriteLine("[ABORT] Remaining lines not sent.");
                Console.WriteLine($"=== {Name} summary ===  Site: {siteId}   Success: {successCount:N0}   Failed: {failureCount:N0}   Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
                _logger.LogInformation("File {FilePath}: cancelled by user at the first-response preview; remaining lines not sent.", filePath);
                AppendProcessedFile(filePath, successCount, failureCount, stopwatch.Elapsed, "cancelled at first-response preview");
                return (successCount, failureCount);
            }
        }

        var completed = 1;
        var consoleLock = new object();

        await Parallel.ForEachAsync(
            remaining,
            new ParallelOptions { MaxDegreeOfParallelism = ctx.MaxParallelism },
            async (item, ct) =>
        {
            var (line, lineNum) = item;
            if (string.IsNullOrWhiteSpace(line))
            {
                Interlocked.Increment(ref completed);
                return;
            }

            try
            {
                JObject.Parse(line);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Interlocked.Increment(ref failureCount);
                Interlocked.Increment(ref completed);
                lock (consoleLock)
                {
                    Console.WriteLine($"[WARN] Skipping malformed line {lineNum}: {ex.Message}");
                }
                _logger.LogWarning("File {FilePath}: skipping malformed line {LineNum}: {Reason}", filePath, lineNum, ex.Message);
                failuresLog.Write(filePath, lineNum, status: null, reason: $"malformed JSON: {ex.Message}");
                return;
            }

            var result = await SendLine(uri, line, username, password, ctx.Rest, ct);

            var ok = (int)result.Status is >= 200 and < 300;
            if (ok)
            {
                Interlocked.Increment(ref successCount);
            }
            else
            {
                Interlocked.Increment(ref failureCount);
                lock (consoleLock)
                {
                    PrintFailure(lineNum, result);
                }
                LogFailure(failuresLog, filePath, lineNum, result);
            }

            var done = Interlocked.Increment(ref completed);
            if (done % 1000 == 0)
            {
                lock (consoleLock)
                {
                    Console.WriteLine($"  ... {done:N0}/{lines.Length:N0} lines processed (ok {successCount}, fail {failureCount})");
                }
                _logger.LogInformation("File {FilePath}: {Done}/{Total} lines processed (ok {Ok}, fail {Fail}).", filePath, done, lines.Length, successCount, failureCount);
            }
        });

        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"=== {Name} summary ===  Site: {siteId}   Success: {successCount:N0}   Failed: {failureCount:N0}   Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
        _logger.LogInformation(
            "File {FilePath}: finished. success={Success} failed={Failed} elapsed={Elapsed}.",
            filePath, successCount, failureCount, stopwatch.Elapsed);
        AppendProcessedFile(filePath, successCount, failureCount, stopwatch.Elapsed, reason: null);

        return (successCount, failureCount);
    }

    /// <summary>Builds one processed-files log line: timestamp, file path, and a 1-line summary. Pure
    /// (no I/O), so the exact line format is unit-testable without touching the real log file.</summary>
    internal static string BuildProcessedFileLine(string filePath, int success, int failure, TimeSpan elapsed, string? reason)
    {
        var summary = reason is null
            ? $"success={success} failed={failure} elapsed={elapsed:hh\\:mm\\:ss\\.fff}"
            : $"success={success} failed={failure} elapsed={elapsed:hh\\:mm\\:ss\\.fff} ({reason})";
        return $"{DateTimeOffset.UtcNow:O}\t{filePath}\t{summary}";
    }

    /// <summary>Appends one line to <see cref="ProcessedFilesLogPath"/> recording that this file has
    /// been processed — the persistent answer to "which files already ran."</summary>
    private void AppendProcessedFile(string filePath, int success, int failure, TimeSpan elapsed, string? reason)
    {
        var path = ProcessedFilesLogPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = BuildProcessedFileLine(filePath, success, failure, elapsed, reason);
            lock (ProcessedFilesLogLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not append to the processed-files log at {Path}.", path);
        }
    }

    private static void LogFailure(FailureLogWriter failuresLog, string filePath, int lineNum, RestResults<string, string> result)
    {
        var reason = result.Error ?? result.UnhandledError ?? "(no response body)";
        failuresLog.Write(filePath, lineNum, (int)result.Status, reason);
    }

    private static Task<RestResults<string, string>> SendLine(
        Uri uri, string line, string username, string password, RestClient rest, CancellationToken ct) =>
        rest.ExecuteRestMethodAsync<string, string>(
            uri: uri,
            verb: HttpVerbs.POST,
            payload: null!,
            serializedPayload: line,
            parameters: null!,
            headers: null!,
            username: username,
            password: password,
            accept: "application/json",
            contentType: "application/json",
            cancellationToken: ct);

    private static void PrintFailure(int lineNum, RestResults<string, string> result)
    {
        var detail = result.Error ?? result.UnhandledError ?? "(no response body)";
        if (detail.Length > 500)
        {
            detail = detail[..500] + "...";
        }
        Console.WriteLine($"[FAIL] L{lineNum} -> {(int)result.Status} {result.Status}");
        Console.WriteLine($"       {detail.Replace("\n", "\n       ")}");
    }

    // Shows the exact verb/URL and the first record's pretty-printed JSON body before any request
    // goes out, so a bad file/site pick can be caught before hammering the API with it. Skipped
    // entirely for a folder upload — see the "skipPreviews" plumbing in Execute/UploadFile.
    private static bool PreviewRequestAndConfirm(Uri uri, string firstLine, int totalLines)
    {
        string prettyJson;
        try
        {
            prettyJson = JObject.Parse(firstLine).ToString(Newtonsoft.Json.Formatting.Indented);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Console.WriteLine($"[FAIL] First line is not valid JSON: {ex.Message}");
            return false;
        }

        Console.WriteLine("First request preview:");
        Console.WriteLine($"POST  {uri}");
        Console.WriteLine(prettyJson);
        Console.WriteLine();
        Console.Write($"Send this first request (of {totalLines:N0} total)? [Enter to continue, Esc to abort] ");

        return WaitForContinue();
    }

    // Shows the first request's actual response (status + pretty-printed body) before firing off
    // the rest of the file, so a wrong site/auth/shape problem shows up after one call, not N. Also
    // skipped for a folder upload.
    private static bool PreviewResponseAndConfirm(RestResults<string, string> result, int remainingCount)
    {
        var statusCode = (int)result.Status;
        var body = string.IsNullOrWhiteSpace(result.SerializedResponse)
            ? result.Error ?? result.UnhandledError ?? "(no response body)"
            : result.SerializedResponse;

        string preview;
        try
        {
            preview = JToken.Parse(body).ToString(Newtonsoft.Json.Formatting.Indented);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            preview = body;
        }

        Console.WriteLine();
        Console.WriteLine("First response preview:");
        Console.WriteLine($"{statusCode} {result.Status}");
        Console.WriteLine(preview);
        Console.WriteLine();
        Console.Write($"Continue with the remaining {remainingCount:N0} line(s)? [Enter to continue, Esc to abort] ");

        return WaitForContinue();
    }

    private static bool WaitForContinue()
    {
        var key = Console.ReadKey(intercept: true).Key;
        Console.WriteLine();
        return key != ConsoleKey.Escape;
    }

    /// <summary>One resolved upload target set: the file paths to upload, and whether they came from
    /// a folder (which suppresses the interactive preview prompts — see <see cref="Execute(IReadOnlyList{string}?)"/>).</summary>
    internal sealed record ResolvedPaths(IReadOnlyList<string> Paths, bool FromFolder);

    /// <summary>
    /// Lists the .jsonl files directly in <paramref name="jsonlDir"/> for a numbered pick, but also
    /// accepts a typed path instead of a number — either a single .jsonl file, or a folder, in which
    /// case every .jsonl file found under it (recursively; non-.jsonl files are ignored) is uploaded.
    /// </summary>
    internal static ResolvedPaths? PromptForPaths(string jsonlDir)
    {
        if (!Directory.Exists(jsonlDir))
        {
            Console.Write($"JSONL directory not found ({jsonlDir}). Enter a full path to a .jsonl file, or a folder to upload every .jsonl file under it (recursively): ");
            return ResolvePathInput(Console.ReadLine());
        }

        var files = Directory.EnumerateFiles(jsonlDir, "*.jsonl")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine($"[FAIL] No .jsonl files found in {jsonlDir}.");
            return null;
        }

        Console.WriteLine($"Available JSONL files in {jsonlDir}:");
        for (var i = 0; i < files.Count; i++)
        {
            Console.WriteLine($"  {i + 1}) {Path.GetFileName(files[i])}");
        }
        Console.Write($"Choice [1-{files.Count}], or type a full path to a .jsonl file or a folder (uploads every .jsonl file under it, recursively): ");

        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out var n) && n >= 1 && n <= files.Count)
        {
            return new ResolvedPaths([files[n - 1]], FromFolder: false);
        }

        return ResolvePathInput(input);
    }

    /// <summary>Resolves a typed path to either one file or, for a folder, every .jsonl file under it
    /// (recursive, non-.jsonl files ignored). Null/blank/not-found input returns null (caller aborts).</summary>
    internal static ResolvedPaths? ResolvePathInput(string? input)
    {
        var path = input?.Trim().Trim('"');
        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("[FAIL] Invalid choice.");
            return null;
        }

        if (Directory.Exists(path))
        {
            var found = Directory.EnumerateFiles(path, "*.jsonl", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (found.Count == 0)
            {
                Console.WriteLine($"[FAIL] No .jsonl files found under {path} (searched recursively).");
                return null;
            }
            Console.WriteLine($"Found {found.Count:N0} .jsonl file(s) under {path}.");
            return new ResolvedPaths(found, FromFolder: true);
        }

        if (File.Exists(path))
        {
            return new ResolvedPaths([path], FromFolder: false);
        }

        Console.WriteLine($"[FAIL] Path not found: {path}");
        return null;
    }

    private static SiteConfig? PromptForSite(IReadOnlyList<SiteConfig> sites)
    {
        if (sites.Count == 0)
        {
            Console.WriteLine("[FAIL] No sites configured in appsettings.json.");
            return null;
        }

        Console.WriteLine("Available sites:");
        for (var i = 0; i < sites.Count; i++)
        {
            Console.WriteLine($"  {i + 1}) {sites[i].SiteId}  ({sites[i].BaseUrl})");
        }
        Console.Write($"Choice [1-{sites.Count}]: ");

        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out var n) && n >= 1 && n <= sites.Count)
        {
            return sites[n - 1];
        }

        Console.WriteLine("[FAIL] Invalid choice.");
        return null;
    }

    /// <summary>Lazily-created, thread-safe writer for the per-run failure detail log: only touches
    /// disk once a first failure actually occurs, so a clean run leaves no empty log file behind.</summary>
    internal sealed class FailureLogWriter(string path) : IDisposable
    {
        private readonly object _sync = new();
        private StreamWriter? _writer;

        public string Path => path;
        public bool WroteAny { get; private set; }

        public void Write(string filePath, int lineNum, int? status, string reason)
        {
            if (reason.Length > 500)
            {
                reason = reason[..500] + "...";
            }
            reason = reason.Replace("\r", " ").Replace("\n", " ");
            var statusText = status?.ToString() ?? "n/a";
            var line = $"{DateTimeOffset.UtcNow:O}\tfile={filePath}\tline={lineNum}\tstatus={statusText}\treason={reason}";

            lock (_sync)
            {
                if (_writer is null)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    _writer = new StreamWriter(path, append: true) { AutoFlush = true };
                }
                _writer.WriteLine(line);
                WroteAny = true;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _writer?.Dispose();
            }
        }
    }
}
