namespace DataMonger.Synth;

/// <summary>
/// Models one synthetic browsing session for a single profile: a shared session_id, channel and a rolling clock,
/// so every event generated for that profile lands on the same session_id and within one 45-minute browsing
/// window, in event order. The session never extends past the injected "now".
/// </summary>
public sealed class EventSession
{
    public const int WindowMinutes = 45;
    public const string FallbackTimeZoneId = "America/New_York";

    /// <summary>Session channel values; one is picked per session.</summary>
    public static readonly IReadOnlyList<string> Channels = new[] { "desktop", "mobile_web", "mobile_app" };

    private readonly Random _rng;
    private readonly DateTimeOffset _windowEnd;

    public string SessionId { get; }

    /// <summary>One session = one channel, shared by every event in it.</summary>
    public string Channel { get; }

    /// <summary>Timestamp to use for the next event; advances (but never past the session window) each call.</summary>
    public DateTimeOffset NextTimestamp { get; private set; }

    private EventSession(Random rng, string sessionId, DateTimeOffset anchor)
    {
        _rng = rng;
        SessionId = sessionId;
        Channel = Channels[rng.Next(Channels.Count)];
        NextTimestamp = anchor;
        _windowEnd = anchor.AddMinutes(WindowMinutes);
    }

    /// <summary>
    /// Starts a new session: a random local time of day on one of the last 14 local days (in the given time zone),
    /// which becomes the timestamp of the first event. If that would put the 45-minute window after
    /// <paramref name="now"/>, the session is moved back one day.
    /// </summary>
    public static EventSession Start(Random rng, TimeZoneInfo timeZone, DateTimeOffset now)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(now, timeZone);
        var daysAgo = rng.Next(0, 14);
        var secondsOfDay = rng.Next(0, 24 * 60 * 60);
        var anchor = LocalAnchor(nowLocal.Date.AddDays(-daysAgo), secondsOfDay, timeZone);
        if (anchor.AddMinutes(WindowMinutes) > now)
        {
            anchor = LocalAnchor(nowLocal.Date.AddDays(-daysAgo - 1), secondsOfDay, timeZone);
        }
        return new EventSession(rng, $"sess-{HexId(rng)}", anchor);
    }

    private static DateTimeOffset LocalAnchor(DateTime localDate, int secondsOfDay, TimeZoneInfo timeZone) =>
        new DateTimeOffset(localDate, timeZone.GetUtcOffset(localDate)).AddSeconds(secondsOfDay);

    /// <summary>Advances the clock by a small random gap (10s-5min), clamped to the session's 45-minute window.</summary>
    public DateTimeOffset AdvanceAndGetTimestamp()
    {
        var gapSeconds = _rng.Next(10, 5 * 60);
        var candidate = NextTimestamp.AddSeconds(gapSeconds);
        NextTimestamp = candidate > _windowEnd ? _windowEnd : candidate;
        return NextTimestamp;
    }

    /// <summary>Random lowercase hex id, e.g. "f6c49f18" (4 bytes -> 8 hex chars).</summary>
    public static string HexId(Random rng)
    {
        Span<byte> bytes = stackalloc byte[4];
        rng.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Finds an IANA (or system) time zone id; blank, unknown or invalid ids fall back to <see cref="FallbackTimeZoneId"/>.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(FallbackTimeZoneId);
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(FallbackTimeZoneId);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(FallbackTimeZoneId);
        }
    }
}
