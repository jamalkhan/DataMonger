using Microsoft.Extensions.Logging;

namespace DataMonger.Logging;

/// <summary>
/// Shared logging helpers for the console commands: the per-command scope (so every line a command writes
/// carries <c>Command</c> and <c>SiteId</c>) and the throughput calculation used by the "completed" events.
/// </summary>
internal static class CommandLogging
{
    /// <summary>SiteId used in the scope of commands that only touch local files (no site/API involved).</summary>
    public const string LocalSiteId = "(local)";

    private static readonly Func<ILogger, string, string, IDisposable?> CommandScope =
        LoggerMessage.DefineScope<string, string>("Command {Command} on site {SiteId}");

    private static readonly Func<ILogger, string, IDisposable?> TargetSiteScope =
        LoggerMessage.DefineScope<string>("Target site {SiteId}");

    /// <summary>Opens the <c>{Command, SiteId}</c> scope that wraps one command run.</summary>
    public static IDisposable? BeginCommandScope(this ILogger logger, string command, string siteId) =>
        CommandScope(logger, command, siteId);

    /// <summary>
    /// For commands with their own site picker: a nested scope whose <c>SiteId</c> overrides the outer
    /// command scope's once the user has picked the target site.
    /// </summary>
    public static IDisposable? BeginTargetSiteScope(this ILogger logger, string siteId) =>
        TargetSiteScope(logger, siteId);

    /// <summary>Records per second, rounded to one decimal; 0 when no time elapsed.</summary>
    public static double RecordsPerSecond(long count, double durationMs) =>
        durationMs > 0 ? Math.Round(count / (durationMs / 1000.0), 1) : 0;

    /// <summary>Path portion of a URL with no query string or fragment (safe at Debug and above).</summary>
    public static string UrlPath(Uri uri) => uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?', '#')[0];
}
