using bleak.Api.Rest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DataMonger.Common;

namespace DataMonger.Commands;

// Stress-test runner. Reads massive_apis.jsonl where each line fully describes ONE API call
// (url + verb + payload) and fires them all at once. No inter-request delay and no rate limiter:
// the point is to hammer the target as hard as the local machine allows.
public class ApiBulkRunner(CommandContext ctx) : IConsoleCommand
{
    public string Name => "API => Bulk Runner (stress test)";

    // --- Tunable knobs --------------------------------------------------------------
    // Dedicated parallelism cap for this command, intentionally independent of the global
    // MaxParallelism (50) so the sledgehammer here doesn't change how other commands behave.
    private const int BulkParallelism = 1000;
    // --------------------------------------------------------------------------------

    private sealed record Call(Uri Uri, HttpVerbs Verb, string Payload, string Label);

    public async Task<(int success, int failure)> Execute()
    {
        var inputPath = DataPaths.Combine("massive_apis.jsonl");
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"[FAIL] {inputPath} not found.");
            return (0, 1);
        }

        var calls = new List<Call>();
        using (var reader = new StreamReader(inputPath))
        {
            string? line;
            var lineNum = 0;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    var row = JObject.Parse(line);

                    var rawUrl = row["url"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(rawUrl))
                    {
                        Console.WriteLine($"[WARN] Skipping line {lineNum}: missing 'url'.");
                        continue;
                    }

                    var verbStr = row["verb"]?.Value<string>() ?? "POST";
                    if (!Enum.TryParse<HttpVerbs>(verbStr, ignoreCase: true, out var verb))
                    {
                        Console.WriteLine($"[WARN] Skipping line {lineNum}: unknown verb '{verbStr}'.");
                        continue;
                    }

                    // Payload may be an inline JSON object/array or already a string; serialize compactly.
                    var payloadToken = row["payload"];
                    var payload = payloadToken == null || payloadToken.Type == JTokenType.Null
                        ? string.Empty
                        : payloadToken.Type == JTokenType.String
                            ? payloadToken.Value<string>()!
                            : payloadToken.ToString(Formatting.None);

                    calls.Add(new Call(ResolveUri(rawUrl), verb, payload, $"L{lineNum} {verb}"));
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[WARN] Skipping malformed line {lineNum}: {ex.Message}");
                }
            }
        }

        if (calls.Count == 0)
        {
            Console.WriteLine("[FAIL] No valid API calls parsed from massive_apis.jsonl.");
            return (0, 1);
        }

        var total = calls.Count;
        Console.WriteLine($"Loaded {total} API calls from {inputPath}");
        Console.WriteLine($"Firing with MaxDegreeOfParallelism = {BulkParallelism}. No delay, no rate limit.");
        Console.WriteLine($"Progress refreshes every ~3 seconds. Failures will scroll past the progress line.");
        Console.WriteLine();

        var ok = 0;
        var failed = 0;
        var completed = 0;
        var startTime = DateTime.UtcNow;
        var consoleLock = new object();

        void WriteProgress()
        {
            var done = completed;
            var elapsed = DateTime.UtcNow - startTime;
            var rate = done / Math.Max(elapsed.TotalSeconds, 0.001);
            var remaining = total - done;
            string etaStr;
            if (rate > 0 && done > 0)
            {
                var eta = TimeSpan.FromSeconds(remaining / rate);
                etaStr = $"{eta:hh\\:mm\\:ss}";
            }
            else
            {
                etaStr = "??:??:??";
            }
            var pct = total > 0 ? (double)done / total * 100 : 0;
            const int barWidth = 30;
            var fill = Math.Min((int)(pct / 100 * barWidth), barWidth);
            var bar = new string('#', fill).PadRight(barWidth, '.');
            var lineText = $"[{bar}] {done}/{total} ({pct:F1}%)  ok {ok}  fail {failed}  elapsed {elapsed:hh\\:mm\\:ss}  ETA {etaStr}";
            lock (consoleLock)
            {
                Console.Write("\r" + lineText.PadRight(110));
            }
        }

        void WriteFailure(string label, Uri uri, int status, string statusText, string detail)
        {
            lock (consoleLock)
            {
                Console.WriteLine();
                Console.WriteLine($"[FAIL] {label} {uri} -> {status} {statusText}");
                Console.WriteLine($"       {detail.Replace("\n", "\n       ")}");
            }
        }

        using var timer = new Timer(_ => WriteProgress(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        await Parallel.ForEachAsync(
            calls,
            new ParallelOptions { MaxDegreeOfParallelism = BulkParallelism },
            async (call, ct) =>
        {
            var result = await ctx.Rest.ExecuteRestMethodAsync<string, string>(
                uri: call.Uri,
                verb: call.Verb,
                payload: null!,
                serializedPayload: call.Payload,
                parameters: null!,
                headers: null!,
                username: ctx.Username,
                password: ctx.Password,
                accept: "application/json",
                contentType: "application/json",
                cancellationToken: ct);

            var status = (int)result.Status;
            if (status is >= 200 and < 300)
            {
                Interlocked.Increment(ref ok);
            }
            else
            {
                Interlocked.Increment(ref failed);
                var detail = result.Error ?? result.UnhandledError ?? "(no response body)";
                if (detail.Length > 500)
                {
                    detail = detail[..500] + "...";
                }
                WriteFailure(call.Label, call.Uri, status, result.Status.ToString(), detail);
            }
            Interlocked.Increment(ref completed);
        });

        WriteProgress();
        Console.WriteLine();

        return (ok, failed);
    }

    // Resolves a JSONL 'url' against the current site. Absolute URLs are used as-is; anything else is
    // treated as a path relative to the site base. The literal token {site} is replaced with the site id
    // so one file works across every configured site.
    private Uri ResolveUri(string url)
    {
        url = url.Replace("{site}", ctx.SiteId);
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            return abs;
        }
        var baseUrl = ctx.BaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/{url.TrimStart('/')}");
    }
}
