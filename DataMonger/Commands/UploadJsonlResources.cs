using System.Diagnostics;
using bleak.Api.Rest;
using DataMonger.Common;
using DataMonger.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace DataMonger.Commands;

// Uploads records from a LocalData products JSONL feed to the "create or update resource" API:
//   PUT {baseUrl}/ver2/{siteId}/resources/{resource_id}
// Each JSONL line is sent through as-is as the request body (it already carries resource_type,
// resource_id, and whatever other fields that record has). Auth is HTTP Basic using the Username
// and Password of whichever Sites entry (appsettings.json) the user picks by number — resource
// uploads and the activity/identify APIs share the same site credentials, so there is one config
// list, not a separate one.
// Requests are fired concurrently up to ctx.MaxParallelism, since this is a plain feed upload with
// no per-record ordering dependency.
//
// This command runs its own site picker rather than relying on the outer Program.cs per-site loop
// (which now iterates every Sites entry), so it must guard against being invoked once per
// configured site and instead run exactly once per process, like the other single-run commands.
public partial class UploadJsonlResources(
    CommandContext ctx,
    RestClient rest,
    ILogger<UploadJsonlResources> logger) : IConsoleCommand
{
    public string Name => "API => Upload JSONL Resources (create/update)";

    private static bool _completedThisSession;

    private readonly ILogger _logger = logger;

    public async Task<(int success, int failure)> Execute()
    {
        using var scope = _logger.BeginCommandScope(Name, ctx.SiteId);
        if (_completedThisSession)
        {
            Console.WriteLine("[skip] UploadJsonlResources already ran this session (it picks its own site).");
            Log.SkippedAlreadyRan(_logger);
            return (0, 0);
        }
        _completedThisSession = true;

        var jsonlDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LocalData", "jsonl", "products");
        jsonlDir = Path.GetFullPath(jsonlDir);
        if (!Directory.Exists(jsonlDir))
        {
            jsonlDir = Path.Combine(Directory.GetCurrentDirectory(), "LocalData", "jsonl", "products");
        }

        var filePath = PromptForFile(jsonlDir);
        if (filePath is null)
        {
            Console.WriteLine("[FAIL] No file selected. Aborting.");
            Log.Aborted(_logger, "no file selected");
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

        Console.WriteLine();
        Console.WriteLine($"File:    {filePath}");
        Console.WriteLine($"Site ID: {siteId}");
        Console.WriteLine($"Target:  {baseUrl}/ver2/{siteId}/resources/{{resource_id}}");
        Console.WriteLine($"Parallelism: {ctx.MaxParallelism}");
        Console.WriteLine();

        var fileName = Path.GetFileName(filePath);
        Log.Started(_logger, fileName, $"/ver2/{siteId}/resources/{{resource_id}}", ctx.MaxParallelism);
        var readStart = Stopwatch.GetTimestamp();
        var lines = await File.ReadAllLinesAsync(filePath);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            Log.ReadFinished(_logger, fileName, lines.Length, new FileInfo(filePath).Length, Stopwatch.GetElapsedTime(readStart).TotalMilliseconds);
        }
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stats = new HttpFanOutStats(ctx.MaxParallelism);

        var successCount = 0;
        var failureCount = 0;
        var completed = 0;
        var consoleLock = new object();
        var malformedCount = 0;
        var missingIdCount = 0;

        await Parallel.ForEachAsync(
            lines.Select((line, index) => (line, lineNum: index + 1)),
            new ParallelOptions { MaxDegreeOfParallelism = ctx.MaxParallelism },
            async (item, ct) =>
        {
            var (line, lineNum) = item;
            if (string.IsNullOrWhiteSpace(line))
            {
                Interlocked.Increment(ref completed);
                return;
            }

            JObject record;
            try
            {
                record = JObject.Parse(line);
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

            var resourceId = record["resource_id"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                Interlocked.Increment(ref failureCount);
                Interlocked.Increment(ref completed);
                lock (consoleLock)
                {
                    Console.WriteLine($"[WARN] Skipping line {lineNum}: missing 'resource_id'.");
                    missingIdCount++;
                }
                return;
            }

            var uri = new Uri($"{baseUrl}/ver2/{siteId}/resources/{Uri.EscapeDataString(resourceId)}");

            var requestStart = stats.Begin();
            var result = await rest.ExecuteRestMethodAsync<string, string>(
                uri: uri,
                verb: HttpVerbs.PUT,
                payload: null!,
                serializedPayload: line,
                parameters: null!,
                headers: null!,
                username: username,
                password: password,
                accept: "application/json",
                contentType: "application/json",
                cancellationToken: ct);

            var statusCode = (int)result.Status;
            stats.End(requestStart, statusCode);
            var ok = statusCode is >= 200 and < 300;
            if (ok)
            {
                Interlocked.Increment(ref successCount);
            }
            else
            {
                Interlocked.Increment(ref failureCount);
                var detail = result.Error ?? result.UnhandledError ?? "(no response body)";
                if (detail.Length > 500)
                {
                    detail = detail[..500] + "...";
                }
                lock (consoleLock)
                {
                    Console.WriteLine($"[FAIL] L{lineNum} resource_id={resourceId} -> {statusCode} {result.Status}");
                    Console.WriteLine($"       {detail.Replace("\n", "\n       ")}");
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
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        stats.LogSummary(_logger, fileName, durationMs);
        if (malformedCount + missingIdCount > 0)
        {
            Log.LinesSkipped(_logger, fileName, malformedCount + missingIdCount, malformedCount, missingIdCount);
        }
        Log.Completed(_logger, fileName, lines.Length, successCount, failureCount, durationMs,
            CommandLogging.RecordsPerSecond(successCount + failureCount, durationMs));

        return (successCount, failureCount);
    }

    private static string? PromptForFile(string jsonlDir)
    {
        if (!Directory.Exists(jsonlDir))
        {
            Console.Write($"JSONL directory not found ({jsonlDir}). Enter a full path to a .jsonl file: ");
            var manual = Console.ReadLine()?.Trim().Trim('"');
            return !string.IsNullOrEmpty(manual) && File.Exists(manual) ? manual : null;
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
        Console.Write($"Choice [1-{files.Count}]: ");

        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out var n) && n >= 1 && n <= files.Count)
        {
            return files[n - 1];
        }

        Console.WriteLine("[FAIL] Invalid choice.");
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
        [LoggerMessage(EventId = 2300, EventName = "UploadResourcesStarted", Level = LogLevel.Information,
            Message = "Upload JSONL resources started: {FileName}, PUT {UrlPath}, max parallelism {MaxParallelism}")]
        public static partial void Started(ILogger logger, string fileName, string urlPath, int maxParallelism);

        [LoggerMessage(EventId = 2301, EventName = "UploadResourcesCompleted", Level = LogLevel.Information,
            Message = "Upload JSONL resources completed: {FileName}, {RowCount} line(s), {SuccessCount} succeeded, {ErrorCount} failed in {DurationMs} ms ({RecordsPerSecond} rec/s)")]
        public static partial void Completed(ILogger logger, string fileName, int rowCount, int successCount, int errorCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 2302, EventName = "UploadResourcesReadFinished", Level = LogLevel.Debug,
            Message = "Read phase finished: {FileName}, {RowCount} line(s), {ByteCount} bytes in {DurationMs} ms")]
        public static partial void ReadFinished(ILogger logger, string fileName, int rowCount, long byteCount, double durationMs);

        [LoggerMessage(EventId = 2303, EventName = "UploadResourcesBatchProgress", Level = LogLevel.Debug,
            Message = "File {FileName}: {CompletedCount}/{RowCount} line(s) processed ({SuccessCount} ok, {ErrorCount} failed) after {DurationMs} ms")]
        public static partial void BatchProgress(ILogger logger, string fileName, int completedCount, int rowCount, int successCount, int errorCount, double durationMs);

        [LoggerMessage(EventId = 2304, EventName = "UploadResourcesLinesSkipped", Level = LogLevel.Warning,
            Message = "File {FileName}: {SkippedCount} line(s) skipped ({MalformedCount} malformed JSON, {MissingIdCount} missing resource_id)")]
        public static partial void LinesSkipped(ILogger logger, string fileName, int skippedCount, int malformedCount, int missingIdCount);

        [LoggerMessage(EventId = 2305, EventName = "UploadResourcesAborted", Level = LogLevel.Warning,
            Message = "Aborting: {Reason}")]
        public static partial void Aborted(ILogger logger, string reason);

        [LoggerMessage(EventId = 2306, EventName = "UploadResourcesSkippedAlreadyRan", Level = LogLevel.Information,
            Message = "Skipped: already ran once this session")]
        public static partial void SkippedAlreadyRan(ILogger logger);
    }
}
