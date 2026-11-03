using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DataMonger.Logging;

/// <summary>
/// Thread-safe counters for one parallel HTTP fan-out (a Parallel.ForEachAsync over API calls): requests,
/// peak in-flight parallelism, outcomes by status class, 429 throttles and latency. Updated per request with
/// Interlocked operations only (nothing is logged per request); the totals are logged once per phase/file by
/// <see cref="LogSummary"/>. Per-request lines (method, URL, status) are the REST client's job.
/// </summary>
internal sealed partial class HttpFanOutStats(int maxParallelism)
{
    private long _requests;
    private long _success;
    private long _throttled;
    private long _clientErrors;
    private long _serverErrors;
    private long _transportErrors;
    private long _latencyTicksTotal;
    private long _latencyTicksMax;
    private int _inFlight;
    private int _peakInFlight;

    public int MaxParallelism { get; } = maxParallelism;
    public long Requests => Interlocked.Read(ref _requests);
    public long Successes => Interlocked.Read(ref _success);
    public long Throttled => Interlocked.Read(ref _throttled);
    public long Failures => Requests - Successes;

    /// <summary>Call just before sending; returns the start timestamp to pass to <see cref="End"/>.</summary>
    public long Begin()
    {
        var now = Interlocked.Increment(ref _inFlight);
        int peak;
        while (now > (peak = Volatile.Read(ref _peakInFlight)) &&
               Interlocked.CompareExchange(ref _peakInFlight, now, peak) != peak)
        {
        }
        return Stopwatch.GetTimestamp();
    }

    /// <summary>Call when the response (or failure) arrived, with the HTTP status code (0 or less = no response).</summary>
    public void End(long startTimestamp, int statusCode)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _requests);
        Interlocked.Add(ref _latencyTicksTotal, elapsed);
        long max;
        while (elapsed > (max = Interlocked.Read(ref _latencyTicksMax)) &&
               Interlocked.CompareExchange(ref _latencyTicksMax, elapsed, max) != max)
        {
        }

        switch (statusCode)
        {
            case >= 200 and < 300: Interlocked.Increment(ref _success); break;
            case 429: Interlocked.Increment(ref _throttled); break;
            case >= 400 and < 500: Interlocked.Increment(ref _clientErrors); break;
            case >= 500: Interlocked.Increment(ref _serverErrors); break;
            case < 100: Interlocked.Increment(ref _transportErrors); break;
            default: Interlocked.Increment(ref _clientErrors); break; // 1xx/3xx treated as unexpected
        }
    }

    /// <summary>
    /// Debug line with the fan-out totals for <paramref name="phase"/> (a file name or phase label), plus an
    /// aggregate Warning when any request was throttled or failed.
    /// </summary>
    public void LogSummary(ILogger logger, string phase, double durationMs)
    {
        var requests = Requests;
        var avgMs = requests > 0 ? Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _latencyTicksTotal)).TotalMilliseconds / requests : 0;
        var maxMs = Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _latencyTicksMax)).TotalMilliseconds;
        Log.FanOutFinished(logger, phase, requests, MaxParallelism, Volatile.Read(ref _peakInFlight), Successes, Failures,
            Throttled, Interlocked.Read(ref _clientErrors), Interlocked.Read(ref _serverErrors), Interlocked.Read(ref _transportErrors),
            Math.Round(avgMs, 1), Math.Round(maxMs, 1), durationMs, CommandLogging.RecordsPerSecond(requests, durationMs));

        if (Throttled > 0)
        {
            Log.Throttled(logger, phase, Throttled, requests);
        }
        if (Failures > 0)
        {
            Log.RequestsFailed(logger, phase, Failures, requests, Throttled, Interlocked.Read(ref _clientErrors),
                Interlocked.Read(ref _serverErrors), Interlocked.Read(ref _transportErrors));
        }
    }

    private static partial class Log
    {
        // EventIds 9000-9099 are reserved for this helper (shared by every HTTP command).
        [LoggerMessage(EventId = 9000, EventName = "HttpFanOutFinished", Level = LogLevel.Debug,
            Message = "HTTP fan-out {Phase} finished: {RequestCount} requests, max parallelism {MaxParallelism}, peak in-flight {PeakInFlight}, {SuccessCount} succeeded, {ErrorCount} failed ({ThrottledCount} throttled 429, {ClientErrorCount} other 4xx, {ServerErrorCount} 5xx, {TransportErrorCount} no response), latency avg {AvgLatencyMs} ms / max {MaxLatencyMs} ms, in {DurationMs} ms ({RecordsPerSecond} req/s)")]
        public static partial void FanOutFinished(ILogger logger, string phase, long requestCount, int maxParallelism, int peakInFlight,
            long successCount, long errorCount, long throttledCount, long clientErrorCount, long serverErrorCount, long transportErrorCount,
            double avgLatencyMs, double maxLatencyMs, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 9001, EventName = "HttpThrottled", Level = LogLevel.Warning,
            Message = "HTTP fan-out {Phase}: {ThrottledCount} of {RequestCount} requests were throttled (429); they were not retried")]
        public static partial void Throttled(ILogger logger, string phase, long throttledCount, long requestCount);

        [LoggerMessage(EventId = 9002, EventName = "HttpRequestsFailed", Level = LogLevel.Warning,
            Message = "HTTP fan-out {Phase}: {ErrorCount} of {RequestCount} requests failed ({ThrottledCount} throttled 429, {ClientErrorCount} other 4xx, {ServerErrorCount} 5xx, {TransportErrorCount} no response)")]
        public static partial void RequestsFailed(ILogger logger, string phase, long errorCount, long requestCount, long throttledCount,
            long clientErrorCount, long serverErrorCount, long transportErrorCount);
    }
}
