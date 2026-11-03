using System.Diagnostics;
using bleak.Api.Rest;
using DataMonger.Common;
using DataMonger.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace DataMonger.Commands;

// Uploads records from a LocalData profile JSONL feed to the "identify a user" API:
//   POST {baseUrl}/ver2/{siteId}/subscribers/identify
// Each JSONL line is sent through as-is as the request body — no reshaping, no wrapping under
// "subscriber", no renaming of fields (e.g. user_id stays user_id). Auth is HTTP Basic using the
// Username/Password of whichever Sites entry (appsettings.json) the user picks by number.
// Requests are fired concurrently up to ctx.MaxParallelism, since this is a plain feed upload with
// no per-record ordering dependency.
//
// This command runs its own site picker rather than relying on the outer Program.cs per-site loop
// (which iterates every Sites entry), so it must guard against being invoked once per configured
// site and instead run exactly once per process, the same way UploadJsonlResources/Events do.
public partial class UploadJsonlIdentifies(CommandContext ctx) : IConsoleCommand
{
    public string Name => "API => Upload JSONL Identifies (identify user)";

    private static bool _completedThisSession;

    /// <summary>At most this many file names go into the Information "started" line (every file gets a Debug line).</summary>
    private const int MaxFileNamesInStartLine = 20;

    private readonly ILogger _logger = ctx.LoggerFactory.CreateLogger<UploadJsonlIdentifies>();

    public Task<(int success, int failure)> Execute() => Execute(filePaths: null);

    /// <summary>
    /// Runs the upload, optionally against a pre-selected set of files instead of prompting for one.
    /// The site is still prompted for once regardless of how many files are uploaded.
    /// </summary>
    public async Task<(int success, int failure)> Execute(IReadOnlyList<string>? filePaths)
    {
        using var scope = _logger.BeginCommandScope(Name, ctx.SiteId);
        var runStart = Stopwatch.GetTimestamp();
        if (filePaths is null)
        {
            if (_completedThisSession)
            {
                Console.WriteLine("[skip] UploadJsonlIdentifies already ran this session (it picks its own site).");
                Log.SkippedAlreadyRan(_logger);
                return (0, 0);
            }
            _completedThisSession = true;

            var jsonlDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LocalData", "jsonl", "profile");
            jsonlDir = Path.GetFullPath(jsonlDir);
            if (!Directory.Exists(jsonlDir))
            {
                jsonlDir = Path.Combine(Directory.GetCurrentDirectory(), "LocalData", "jsonl", "profile");
            }

            filePaths = PromptForPaths(jsonlDir);
            if (filePaths is null)
            {
                Console.WriteLine("[FAIL] No file(s) selected. Aborting.");
                Log.Aborted(_logger, "no file(s) selected");
                return (0, 1);
            }
        }
        else if (filePaths.Count == 0)
        {
            Console.WriteLine("[FAIL] No files to upload. Aborting.");
            Log.Aborted(_logger, "caller passed an empty file list");
            return (0, 1);
        }

        var site = PromptForSite(ctx.AllSites);
        if (site is null)
        {
            Console.WriteLine("[FAIL] No site selected. Aborting.");
            Log.Aborted(_logger, "no site selected");
            return (0, 1);
        }
        using var siteScope = _logger.BeginTargetSiteScope(site.SiteId);

        var siteId = site.SiteId;
        var username = site.Username;
        var password = site.Password;
        var baseUrl = site.BaseUrl.TrimEnd('/');

        var uri = new Uri($"{baseUrl}/ver2/{siteId}/subscribers/identify");

        Console.WriteLine();
        Console.WriteLine($"Site ID: {siteId}");
        Console.WriteLine($"Target:  {uri}");
        Console.WriteLine($"Parallelism: {ctx.MaxParallelism}");
        if (_logger.IsEnabled(LogLevel.Information))
        {
            Log.Started(_logger, filePaths.Count,
                string.Join(",", filePaths.Take(MaxFileNamesInStartLine).Select(Path.GetFileName)) + (filePaths.Count > MaxFileNamesInStartLine ? ",..." : ""),
                CommandLogging.UrlPath(uri), ctx.MaxParallelism);
        }

        var totalSuccess = 0;
        var totalFailure = 0;
        for (var fileIndex = 0; fileIndex < filePaths.Count; fileIndex++)
        {
            var filePath = filePaths[fileIndex];
            Console.WriteLine();
            Console.WriteLine($"--- File {fileIndex + 1}/{filePaths.Count}: {filePath} ---");
            Log.FileStarted(_logger, Path.GetFileName(filePath), filePath, fileIndex + 1, filePaths.Count);
            var (fileSuccess, fileFailure) = await UploadFile(filePath, uri, siteId, username, password);
            totalSuccess += fileSuccess;
            totalFailure += fileFailure;
        }

        if (filePaths.Count > 1)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {Name} grand total ===  Site: {siteId}   Success: {totalSuccess:N0}   Failed: {totalFailure:N0}");
        }

        var runMs = Stopwatch.GetElapsedTime(runStart).TotalMilliseconds;
        Log.Completed(_logger, filePaths.Count, totalSuccess + totalFailure, totalSuccess, totalFailure, runMs,
            CommandLogging.RecordsPerSecond(totalSuccess + totalFailure, runMs));
        return (totalSuccess, totalFailure);
    }

    private async Task<(int success, int failure)> UploadFile(string filePath, Uri uri, string siteId, string username, string password)
    {
        Console.WriteLine($"File: {filePath}");
        Console.WriteLine();
        var fileName = Path.GetFileName(filePath);

        var readStart = Stopwatch.GetTimestamp();
        var lines = await File.ReadAllLinesAsync(filePath);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            Log.ReadFinished(_logger, fileName, lines.Length, new FileInfo(filePath).Length, Stopwatch.GetElapsedTime(readStart).TotalMilliseconds);
        }

        var firstLineIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (firstLineIndex < 0)
        {
            Console.WriteLine("[FAIL] File has no non-blank lines to send.");
            Log.FileEmpty(_logger, fileName, lines.Length);
            return (0, 1);
        }
        var firstLine = lines[firstLineIndex];

        if (!PreviewRequestAndConfirm(uri, firstLine, lines.Length))
        {
            Console.WriteLine("[ABORT] Upload cancelled.");
            Log.FileCancelled(_logger, fileName, "first-request preview");
            return (0, 0);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stats = new HttpFanOutStats(ctx.MaxParallelism);

        var successCount = 0;
        var failureCount = 0;

        // Send the first record on its own, outside the parallel batch, so its response can be
        // previewed and confirmed before the rest of the file is fired off behind it.
        var firstStart = stats.Begin();
        var firstResult = await SendLine(uri, firstLine, username, password, ctx.Rest, CancellationToken.None);
        stats.End(firstStart, (int)firstResult.Status);
        Log.FirstRequestResult(_logger, fileName, (int)firstResult.Status, stopwatch.Elapsed.TotalMilliseconds);
        var firstOk = (int)firstResult.Status is >= 200 and < 300;
        if (firstOk)
        {
            successCount++;
        }
        else
        {
            failureCount++;
            PrintFailure(1, firstResult);
        }

        var remaining = lines
            .Select((line, index) => (line, lineNum: index + 1))
            .Where(item => item.lineNum - 1 != firstLineIndex)
            .ToList();

        if (remaining.Count > 0)
        {
            if (!PreviewResponseAndConfirm(firstResult, remaining.Count))
            {
                stopwatch.Stop();
                Console.WriteLine("[ABORT] Remaining lines not sent.");
                Console.WriteLine($"=== {Name} summary ===  Site: {siteId}   Success: {successCount:N0}   Failed: {failureCount:N0}   Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
                Log.FileCancelled(_logger, fileName, "first-response preview");
                return (successCount, failureCount);
            }
        }

        var completed = 1;
        var consoleLock = new object();
        var malformedCount = 0;

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
                    malformedCount++;
                }
                return;
            }

            var requestStart = stats.Begin();
            var result = await SendLine(uri, line, username, password, ctx.Rest, ct);
            stats.End(requestStart, (int)result.Status);

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
            }

            var done = Interlocked.Increment(ref completed);
            if (done % 1000 == 0)
            {
                lock (consoleLock)
                {
                    Console.WriteLine($"  ... {done:N0}/{lines.Length:N0} lines processed (ok {successCount}, fail {failureCount})");
                }
                Log.BatchProgress(_logger, fileName, done, lines.Length, successCount, failureCount, stopwatch.Elapsed.TotalMilliseconds);
            }
        });

        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"=== {Name} summary ===  Site: {siteId}   Success: {successCount:N0}   Failed: {failureCount:N0}   Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
        stats.LogSummary(_logger, fileName, stopwatch.Elapsed.TotalMilliseconds);
        if (malformedCount > 0)
        {
            Log.MalformedLines(_logger, fileName, malformedCount);
        }
        Log.FileFinished(_logger, fileName, lines.Length, successCount, failureCount, stopwatch.Elapsed.TotalMilliseconds,
            CommandLogging.RecordsPerSecond(successCount + failureCount, stopwatch.Elapsed.TotalMilliseconds));

        return (successCount, failureCount);
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
    // goes out, so a bad file/site pick can be caught before hammering the API with it.
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
    // the rest of the file, so a wrong site/auth/shape problem shows up after one call, not N.
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

    /// <summary>
    /// Lists the .jsonl files directly in <paramref name="jsonlDir"/> for a numbered pick, but also
    /// accepts a typed path instead of a number — either a single .jsonl file, or a folder, in which
    /// case every .jsonl file found under it (recursively; non-.jsonl files are ignored) is uploaded.
    /// </summary>
    private static IReadOnlyList<string>? PromptForPaths(string jsonlDir)
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
            return [files[n - 1]];
        }

        return ResolvePathInput(input);
    }

    /// <summary>Resolves a typed path to either one file or, for a folder, every .jsonl file under it
    /// (recursive, non-.jsonl files ignored). Null/blank/not-found input returns null (caller aborts).</summary>
    private static IReadOnlyList<string>? ResolvePathInput(string? input)
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
            return found;
        }

        if (File.Exists(path))
        {
            return [path];
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

    private static partial class Log
    {
        [LoggerMessage(EventId = 2200, EventName = "UploadIdentifiesStarted", Level = LogLevel.Information,
            Message = "Upload JSONL identifies started: {FileCount} file(s) [{InputFiles}], POST {UrlPath}, max parallelism {MaxParallelism}")]
        public static partial void Started(ILogger logger, int fileCount, string inputFiles, string urlPath, int maxParallelism);

        [LoggerMessage(EventId = 2201, EventName = "UploadIdentifiesCompleted", Level = LogLevel.Information,
            Message = "Upload JSONL identifies completed: {FileCount} file(s), {RecordCount} record(s), {SuccessCount} succeeded, {ErrorCount} failed in {DurationMs} ms ({RecordsPerSecond} rec/s)")]
        public static partial void Completed(ILogger logger, int fileCount, int recordCount, int successCount, int errorCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 2202, EventName = "UploadIdentifiesFileStarted", Level = LogLevel.Debug,
            Message = "File {FileIndex}/{FileCount} {FileName} starting ({FilePath})")]
        public static partial void FileStarted(ILogger logger, string fileName, string filePath, int fileIndex, int fileCount);

        [LoggerMessage(EventId = 2203, EventName = "UploadIdentifiesFileFinished", Level = LogLevel.Debug,
            Message = "File {FileName} finished: {RowCount} line(s), {SuccessCount} succeeded, {ErrorCount} failed in {DurationMs} ms ({RecordsPerSecond} rec/s)")]
        public static partial void FileFinished(ILogger logger, string fileName, int rowCount, int successCount, int errorCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 2204, EventName = "UploadIdentifiesReadFinished", Level = LogLevel.Debug,
            Message = "Read phase finished: {FileName}, {RowCount} line(s), {ByteCount} bytes in {DurationMs} ms")]
        public static partial void ReadFinished(ILogger logger, string fileName, int rowCount, long byteCount, double durationMs);

        [LoggerMessage(EventId = 2205, EventName = "UploadIdentifiesBatchProgress", Level = LogLevel.Debug,
            Message = "File {FileName}: {CompletedCount}/{RowCount} line(s) processed ({SuccessCount} ok, {ErrorCount} failed) after {DurationMs} ms")]
        public static partial void BatchProgress(ILogger logger, string fileName, int completedCount, int rowCount, int successCount, int errorCount, double durationMs);

        [LoggerMessage(EventId = 2206, EventName = "UploadIdentifiesMalformedLines", Level = LogLevel.Warning,
            Message = "File {FileName}: {SkippedCount} malformed line(s) skipped")]
        public static partial void MalformedLines(ILogger logger, string fileName, int skippedCount);

        [LoggerMessage(EventId = 2207, EventName = "UploadIdentifiesFileEmpty", Level = LogLevel.Warning,
            Message = "File {FileName} has no non-blank lines to send ({RowCount} line(s))")]
        public static partial void FileEmpty(ILogger logger, string fileName, int rowCount);

        [LoggerMessage(EventId = 2208, EventName = "UploadIdentifiesFileCancelled", Level = LogLevel.Information,
            Message = "File {FileName}: cancelled by the user at the {Phase}")]
        public static partial void FileCancelled(ILogger logger, string fileName, string phase);

        [LoggerMessage(EventId = 2209, EventName = "UploadIdentifiesFirstRequestResult", Level = LogLevel.Debug,
            Message = "File {FileName}: first request returned {StatusCode} after {DurationMs} ms")]
        public static partial void FirstRequestResult(ILogger logger, string fileName, int statusCode, double durationMs);

        [LoggerMessage(EventId = 2210, EventName = "UploadIdentifiesAborted", Level = LogLevel.Warning,
            Message = "Aborting: {Reason}")]
        public static partial void Aborted(ILogger logger, string reason);

        [LoggerMessage(EventId = 2211, EventName = "UploadIdentifiesSkippedAlreadyRan", Level = LogLevel.Information,
            Message = "Skipped: already ran once this session")]
        public static partial void SkippedAlreadyRan(ILogger logger);
    }
}
