using System.Threading.RateLimiting;
using bleak.Api.Rest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DataMonger.Common;

namespace DataMonger.Commands;

public class ApiEmailConversionEvents(CommandContext ctx) : IConsoleCommand
{
    public string Name => "API => Email Conversion Events";

    // --- Tunable knobs --------------------------------------------------------------
    // Random delay window between consecutive events in a single row, simulating user behavior.
    // The upper bound is prompted for at run time (default 120 s; 0 skips the delays entirely).
    private static readonly TimeSpan MinEventDelay = TimeSpan.Zero;
    public static readonly TimeSpan DefaultMaxEventDelay = TimeSpan.FromSeconds(120);

    // Global cap on outgoing POSTs. Token bucket smooths bursts across the minute.
    private const int RateLimitRequestsPerMinute = 50_000;
    // --------------------------------------------------------------------------------

    public async Task<(int success, int failure)> Execute()
    {
        var inputPath = DataPaths.Combine("EmailConversion.jsonl");
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"[FAIL] {inputPath} not found. Run 'Generate Email Conversion data' first.");
            return (0, 1);
        }

        var maxEventDelay = PromptForMaxDelay();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var rows = new List<(string Email, JArray Events)>();
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
                    var email = row["recipient"]?["email"]?.Value<string>() ?? "(unknown)";
                    var events = row["events"] as JArray ?? new JArray();
                    rows.Add((email, events));
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[WARN] Skipping malformed line {lineNum}: {ex.Message}");
                }
            }
        }

        var totalEvents = rows.Sum(r => r.Events.Count);
        Console.WriteLine($"Loaded {rows.Count} rows ({totalEvents} events) from {inputPath}");
        Console.WriteLine(maxEventDelay > MinEventDelay
            ? $"Posting with random 0-{maxEventDelay.TotalSeconds:0.###}s delays between events to simulate user behavior."
            : "Posting with no delay between events.");
        Console.WriteLine($"Progress refreshes every ~3 seconds. Failures will scroll past the progress line.");
        Console.WriteLine();

        var eventsOk = 0;
        var eventsFailed = 0;
        var completed = 0;
        var startTime = DateTime.UtcNow;
        var consoleLock = new object();

        void WriteProgress()
        {
            var done = completed;
            var elapsed = DateTime.UtcNow - startTime;
            var rate = done / Math.Max(elapsed.TotalSeconds, 0.001);
            var remaining = totalEvents - done;
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
            var pct = totalEvents > 0 ? (double)done / totalEvents * 100 : 0;
            const int barWidth = 30;
            var fill = Math.Min((int)(pct / 100 * barWidth), barWidth);
            var bar = new string('#', fill).PadRight(barWidth, '.');
            var lineText = $"[{bar}] {done}/{totalEvents} ({pct:F1}%)  elapsed {elapsed:hh\\:mm\\:ss}  ETA {etaStr}";
            lock (consoleLock)
            {
                Console.Write("\r" + lineText.PadRight(100));
            }
        }

        void WriteFailure(string email, string eventName, int status, string statusText, string detail)
        {
            lock (consoleLock)
            {
                Console.WriteLine();
                Console.WriteLine($"[FAIL] {email} {eventName} -> {status} {statusText}");
                Console.WriteLine($"       {detail.Replace("\n", "\n       ")}");
            }
        }

        using var timer = new Timer(_ => WriteProgress(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        var tokensPerSecond = Math.Max(1, RateLimitRequestsPerMinute / 60);
        using var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = tokensPerSecond,
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = tokensPerSecond,
            AutoReplenishment = true,
        });

        await Parallel.ForEachAsync(
            rows,
            new ParallelOptions { MaxDegreeOfParallelism = ctx.MaxParallelism },
            async (rowData, ct) =>
        {
            var isFirst = true;
            foreach (var evt in rowData.Events)
            {
                if (!isFirst)
                {
                    var delay = NextDelay(Random.Shared, maxEventDelay);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, ct);
                    }
                }
                isFirst = false;

                var eventName = ExtractEventName(evt);
                var json = evt.ToString(Formatting.None);

                using var lease = await rateLimiter.AcquireAsync(1, ct);

                var result = await ctx.Rest.ExecuteRestMethodAsync<string, string>(
                    uri: ctx.ActivityUri,
                    verb: HttpVerbs.POST,
                    payload: null!,
                    serializedPayload: json,
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
                    Interlocked.Increment(ref eventsOk);
                }
                else
                {
                    Interlocked.Increment(ref eventsFailed);
                    var detail = result.Error ?? result.UnhandledError ?? "(no response body)";
                    if (detail.Length > 500)
                    {
                        detail = detail[..500] + "...";
                    }
                    WriteFailure(rowData.Email, eventName, status, result.Status.ToString(), detail);
                }
                Interlocked.Increment(ref completed);
            }
        });

        WriteProgress();
        Console.WriteLine();

        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"=== {Name} summary ===  Success: {eventsOk}   Failed: {eventsFailed}   Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");

        return (eventsOk, eventsFailed);
    }

    static TimeSpan PromptForMaxDelay()
    {
        while (true)
        {
            Console.Write($"Max random delay between events in seconds [{DefaultMaxEventDelay.TotalSeconds:0}] (0 = no delay): ");
            var line = Console.ReadLine();
            if (TryParseMaxDelay(line, out var delay))
            {
                return delay;
            }
            Console.WriteLine($"'{line?.Trim()}' is not a non-negative number of seconds. Try again, or press Enter for the default.");
        }
    }

    /// <summary>Blank input yields the default; otherwise a non-negative, finite number of seconds.</summary>
    internal static bool TryParseMaxDelay(string? input, out TimeSpan delay)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            delay = DefaultMaxEventDelay;
            return true;
        }
        if (double.TryParse(input.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds / 2)
        {
            delay = TimeSpan.FromSeconds(seconds);
            return true;
        }
        delay = TimeSpan.Zero;
        return false;
    }

    /// <summary>Uniform random delay in [MinEventDelay, max); zero when max is not above the minimum.</summary>
    internal static TimeSpan NextDelay(Random rnd, TimeSpan max) =>
        max <= MinEventDelay
            ? TimeSpan.Zero
            : MinEventDelay + TimeSpan.FromMilliseconds(rnd.NextDouble() * (max - MinEventDelay).TotalMilliseconds);

    internal static string ExtractEventName(JToken evt) =>
        evt["event_type"]?.Type == JTokenType.String ? evt["event_type"]!.Value<string>()! : "(unknown event)";
}
