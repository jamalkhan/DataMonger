using bleak.Api.Rest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using DataMonger.Common;
using DataMonger.Logging;
using Microsoft.Extensions.Logging;

namespace DataMonger.Commands;

// Stress-test runner. Reads massive_apis.jsonl where each line fully describes ONE API call
// (url + verb + payload) and fires them all at once. No inter-request delay and no rate limiter:
// the point is to hammer the target as hard as the local machine allows.
public partial class ApiBulkRunner(CommandContext ctx) : IConsoleCommand
{
    public string Name => "API => Bulk Runner (stress test)";

    // --- Tunable knobs --------------------------------------------------------------
    // Dedicated parallelism cap for this command, intentionally independent of the global
    // MaxParallelism (50) so the sledgehammer here doesn't change how other commands behave.
    private const int BulkParallelism = 1000;
    // --------------------------------------------------------------------------------

    private sealed record Call(Uri Uri, HttpVerbs Verb, string Payload, string Label);

    private const int MaxExampleLineNumbers = 10;

    private readonly ILogger _logger = ctx.LoggerFactory.CreateLogger<ApiBulkRunner>();

    public async Task<(int success, int failure)> Execute()
    {
        using var scope = _logger.BeginCommandScope(Name, ctx.SiteId);
        var commandStart = Stopwatch.GetTimestamp();
        var inputPath = DataPaths.Combine("massive_apis.jsonl");
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"[FAIL] {inputPath} not found.");
            Log.InputMissing(_logger, Path.GetFileName(inputPath), inputPath);
            return (0, 1);
        }

        var calls = new List<Call>();
        var parseStart = Stopwatch.GetTimestamp();
        var lineCount = 0;
        int missingUrl = 0, unknownVerb = 0, malformed = 0;
        var skippedLines = new List<int>(MaxExampleLineNumbers);
        void NoteSkipped(int n)
        {
            if (skippedLines.Count < MaxExampleLineNumbers)
            {
                skippedLines.Add(n);
            }
        }
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

                    var rawUrl = row["url"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(rawUrl))
                    {
                        Console.WriteLine($"[WARN] Skipping line {lineNum}: missing 'url'.");
                        missingUrl++;
                        NoteSkipped(lineNum);
                        continue;
                    }

                    var verbStr = row["verb"]?.Value<string>() ?? "POST";
                    if (!Enum.TryParse<HttpVerbs>(verbStr, ignoreCase: true, out var verb))
                    {
                        Console.WriteLine($"[WARN] Skipping line {lineNum}: unknown verb '{verbStr}'.");
                        unknownVerb++;
                        NoteSkipped(lineNum);
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
                    malformed++;
                    NoteSkipped(lineNum);
                }
            }
        }

        var skippedTotal = missingUrl + unknownVerb + malformed;
        Log.ParseFinished(_logger, Path.GetFileName(inputPath), lineCount, calls.Count, skippedTotal, new FileInfo(inputPath).Length,
            Stopwatch.GetElapsedTime(parseStart).TotalMilliseconds);
        if (skippedTotal > 0)
        {
            Log.LinesSkipped(_logger, Path.GetFileName(inputPath), skippedTotal, malformed, missingUrl, unknownVerb);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                Log.SkippedLineExamples(_logger, Path.GetFileName(inputPath), string.Join(",", skippedLines));
            }
        }

        if (calls.Count == 0)
        {
            Console.WriteLine("[FAIL] No valid API calls parsed from massive_apis.jsonl.");
            Log.NoValidCalls(_logger, Path.GetFileName(inputPath));
            return (0, 1);
        }

        var total = calls.Count;
        Console.WriteLine($"Loaded {total} API calls from {inputPath}");
        Console.WriteLine($"Firing with MaxDegreeOfParallelism = {BulkParallelism}. No delay, no rate limit.");
        Console.WriteLine($"Progress refreshes every ~3 seconds. Failures will scroll past the progress line.");
        Console.WriteLine();
        Log.Started(_logger, Path.GetFileName(inputPath), total, skippedTotal, BulkParallelism);

        var stats = new HttpFanOutStats(BulkParallelism);
        var fanOutStart = Stopwatch.GetTimestamp();
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
            Log.Progress(_logger, done, total, ok, failed, elapsed.TotalMilliseconds);
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
            var requestStart = stats.Begin();
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
            stats.End(requestStart, status);
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

        stats.LogSummary(_logger, Path.GetFileName(inputPath), Stopwatch.GetElapsedTime(fanOutStart).TotalMilliseconds);
        var durationMs = Stopwatch.GetElapsedTime(commandStart).TotalMilliseconds;
        Log.Completed(_logger, Path.GetFileName(inputPath), total, ok, failed, durationMs, CommandLogging.RecordsPerSecond(total, durationMs));

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

    private static partial class Log
    {
        [LoggerMessage(EventId = 1100, EventName = "BulkRunnerStarted", Level = LogLevel.Information,
            Message = "API bulk runner started: {FileName}, {RecordCount} call(s) ({SkippedCount} line(s) skipped), max parallelism {MaxParallelism}")]
        public static partial void Started(ILogger logger, string fileName, int recordCount, int skippedCount, int maxParallelism);

        [LoggerMessage(EventId = 1101, EventName = "BulkRunnerCompleted", Level = LogLevel.Information,
            Message = "API bulk runner completed: {FileName}, {RecordCount} call(s), {SuccessCount} succeeded, {ErrorCount} failed in {DurationMs} ms ({RecordsPerSecond} calls/s)")]
        public static partial void Completed(ILogger logger, string fileName, int recordCount, int successCount, int errorCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 1102, EventName = "BulkRunnerParseFinished", Level = LogLevel.Debug,
            Message = "Parse phase finished: {FileName}, {RowCount} line(s), {RecordCount} valid call(s), {SkippedCount} skipped, {ByteCount} bytes in {DurationMs} ms")]
        public static partial void ParseFinished(ILogger logger, string fileName, int rowCount, int recordCount, int skippedCount, long byteCount, double durationMs);

        [LoggerMessage(EventId = 1103, EventName = "BulkRunnerLinesSkipped", Level = LogLevel.Warning,
            Message = "{FileName}: {SkippedCount} line(s) skipped ({MalformedCount} malformed JSON, {MissingUrlCount} missing url, {UnknownVerbCount} unknown verb)")]
        public static partial void LinesSkipped(ILogger logger, string fileName, int skippedCount, int malformedCount, int missingUrlCount, int unknownVerbCount);

        [LoggerMessage(EventId = 1104, EventName = "BulkRunnerSkippedLineExamples", Level = LogLevel.Debug,
            Message = "{FileName}: first skipped line numbers [{LineNumbers}]")]
        public static partial void SkippedLineExamples(ILogger logger, string fileName, string lineNumbers);

        [LoggerMessage(EventId = 1105, EventName = "BulkRunnerInputMissing", Level = LogLevel.Warning,
            Message = "Input {FileName} not found ({FilePath}); aborting")]
        public static partial void InputMissing(ILogger logger, string fileName, string filePath);

        [LoggerMessage(EventId = 1106, EventName = "BulkRunnerNoValidCalls", Level = LogLevel.Warning,
            Message = "No valid API calls parsed from {FileName}; aborting")]
        public static partial void NoValidCalls(ILogger logger, string fileName);

        [LoggerMessage(EventId = 1107, EventName = "BulkRunnerProgress", Level = LogLevel.Trace,
            Message = "Progress: {CompletedCount}/{RecordCount} done, {SuccessCount} ok, {ErrorCount} failed after {DurationMs} ms")]
        public static partial void Progress(ILogger logger, int completedCount, int recordCount, int successCount, int errorCount, double durationMs);
    }
}
