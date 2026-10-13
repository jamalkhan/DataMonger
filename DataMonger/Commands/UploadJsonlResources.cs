using bleak.Api.Rest;
using DataMonger.Common;
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
public class UploadJsonlResources(CommandContext ctx) : IConsoleCommand
{
    public string Name => "API => Upload JSONL Resources (create/update)";

    private static bool _completedThisSession;

    public async Task<(int success, int failure)> Execute()
    {
        if (_completedThisSession)
        {
            Console.WriteLine("[skip] UploadJsonlResources already ran this session (it picks its own site).");
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
            return (0, 1);
        }

        var site = PromptForSite(ctx.AllSites);
        if (site is null)
        {
            Console.WriteLine("[FAIL] No site selected. Aborting.");
            return (0, 1);
        }

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

        var lines = await File.ReadAllLinesAsync(filePath);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var successCount = 0;
        var failureCount = 0;
        var completed = 0;
        var consoleLock = new object();

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
                }
                return;
            }

            var uri = new Uri($"{baseUrl}/ver2/{siteId}/resources/{Uri.EscapeDataString(resourceId)}");

            var result = await ctx.Rest.ExecuteRestMethodAsync<string, string>(
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
            }
        });

        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"=== {Name} summary ===  Site: {siteId}   Success: {successCount:N0}   Failed: {failureCount:N0}   Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");

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
}
