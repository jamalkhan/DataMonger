using System.Threading.RateLimiting;
using bleak.Api.Rest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using DataMonger.Common;
using DataMonger.Logging;
using Microsoft.Extensions.Logging;

namespace DataMonger.Commands;

public partial class ApiEmailConversionEvents(CommandContext ctx) : IConsoleCommand
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

    private readonly ILogger _logger = ctx.LoggerFactory.CreateLogger<ApiEmailConversionEvents>();

    public async Task<(int success, int failure)> Execute()
    {
        using var scope = _logger.BeginCommandScope(Name, ctx.SiteId);
        var inputPath = DataPaths.Combine("EmailConversion.jsonl");
        var fileName = Path.GetFileName(inputPath);
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"[FAIL] {inputPath} not found. Run 'Generate Email Conversion data' first.");
            Log.InputMissing(_logger, fileName, inputPath);
            return (0, 1);
        }

        var maxEventDelay = PromptForMaxDelay();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var rows = new List<(string Email, JArray Events)>();
        var parseStart = Stopwatch.GetTimestamp();
        var lineCount = 0;
        var malformed = 0;
        using (var reader = new StreamReader(inputPath))
        {
            string? line;
            var lineNum = 0;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNum++;
                lineCount = lineNum;
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
                    malformed++;
                }
            }
        }

        var totalEvents = rows.Sum(r => r.Events.Count);
        Log.ParseFinished(_logger, fileName, lineCount, rows.Count, totalEvents, malformed, new FileInfo(inputPath).Length,
            Stopwatch.GetElapsedTime(parseStart).TotalMilliseconds);
        if (malformed > 0)
        {
            Log.LinesSkipped(_logger, fileName, malformed);
        }
        Console.WriteLine($"Loaded {rows.Count} rows ({totalEvents} events) from {inputPath}");
        Console.WriteLine(maxEventDelay > MinEventDelay
            ? $"Posting with random 0-{maxEventDelay.TotalSeconds:0.###}s delays between events to simulate user behavior."
            : "Posting with no delay between events.");
        Console.WriteLine($"Progress refreshes every ~3 seconds. Failures will scroll past the progress line.");
        Console.WriteLine();
        Log.Started(_logger, fileName, rows.Count, totalEvents, ctx.MaxParallelism, maxEventDelay.TotalMilliseconds, RateLimitRequestsPerMinute,
            CommandLogging.UrlPath(ctx.ActivityUri));

        var stats = new HttpFanOutStats(ctx.MaxParallelism);
        var fanOutStart = Stopwatch.GetTimestamp();
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
            Log.Progress(_logger, done, totalEvents, eventsOk, eventsFailed, elapsed.TotalMilliseconds);
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

                var requestStart = stats.Begin();
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
                stats.End(requestStart, status);
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
        stats.LogSummary(_logger, fileName, Stopwatch.GetElapsedTime(fanOutStart).TotalMilliseconds);
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        Log.Completed(_logger, fileName, rows.Count, totalEvents, eventsOk, eventsFailed, durationMs,
            CommandLogging.RecordsPerSecond(totalEvents, durationMs));

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

    private static partial class Log
    {
        [LoggerMessage(EventId = 1200, EventName = "EmailEventsStarted", Level = LogLevel.Information,
            Message = "Email conversion events upload started: {FileName}, {RowCount} row(s), {RecordCount} event(s), max parallelism {MaxParallelism}, max delay {MaxEventDelayMs} ms, rate limit {RateLimitPerMinute}/min, POST {UrlPath}")]
        public static partial void Started(ILogger logger, string fileName, int rowCount, int recordCount, int maxParallelism, double maxEventDelayMs, int rateLimitPerMinute, string urlPath);

        [LoggerMessage(EventId = 1201, EventName = "EmailEventsCompleted", Level = LogLevel.Information,
            Message = "Email conversion events upload completed: {FileName}, {RowCount} row(s), {RecordCount} event(s), {SuccessCount} succeeded, {ErrorCount} failed in {DurationMs} ms ({RecordsPerSecond} events/s)")]
        public static partial void Completed(ILogger logger, string fileName, int rowCount, int recordCount, int successCount, int errorCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 1202, EventName = "EmailEventsParseFinished", Level = LogLevel.Debug,
            Message = "Parse phase finished: {FileName}, {LineCount} line(s), {RowCount} row(s), {RecordCount} event(s), {SkippedCount} malformed, {ByteCount} bytes in {DurationMs} ms")]
        public static partial void ParseFinished(ILogger logger, string fileName, int lineCount, int rowCount, int recordCount, int skippedCount, long byteCount, double durationMs);

        [LoggerMessage(EventId = 1203, EventName = "EmailEventsLinesSkipped", Level = LogLevel.Warning,
            Message = "{FileName}: {SkippedCount} malformed line(s) skipped")]
        public static partial void LinesSkipped(ILogger logger, string fileName, int skippedCount);

        [LoggerMessage(EventId = 1204, EventName = "EmailEventsInputMissing", Level = LogLevel.Warning,
            Message = "Input {FileName} not found ({FilePath}); aborting")]
        public static partial void InputMissing(ILogger logger, string fileName, string filePath);

        [LoggerMessage(EventId = 1205, EventName = "EmailEventsProgress", Level = LogLevel.Trace,
            Message = "Progress: {CompletedCount}/{RecordCount} events done, {SuccessCount} ok, {ErrorCount} failed after {DurationMs} ms")]
        public static partial void Progress(ILogger logger, int completedCount, int recordCount, int successCount, int errorCount, double durationMs);
    }
}
