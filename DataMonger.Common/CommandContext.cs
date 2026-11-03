namespace DataMonger.Common;

/// <summary>
/// Per-site, per-run data handed to a command as a runtime constructor argument (Program builds one per configured
/// site and creates the command with <c>ActivatorUtilities.CreateInstance</c>). Process-wide services (loggers, the
/// REST client, config helpers) are not here: commands take those from the DI container through their constructor.
/// </summary>
public class CommandContext
{
    public required Uri ActivityUri { get; init; }
    public required Uri IdentifyUri { get; init; }

    // Raw site coordinates, used by commands (e.g. ApiBulkRunner) that resolve their own relative
    // URLs against the site base rather than the prebuilt ActivityUri/IdentifyUri. Non-required so
    // existing construction sites remain valid; BuildContext populates them.
    public string BaseUrl { get; init; } = "";
    public string SiteId { get; init; } = "";
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required int MaxParallelism { get; init; }

    // Every configured Sites site, for commands (e.g. UploadJsonlResources) that let the user pick
    // a target site of their own rather than running against whichever single site the outer
    // Program.cs per-site loop built this context for.
    public IReadOnlyList<SiteConfig> AllSites { get; init; } = Array.Empty<SiteConfig>();
}

public class SiteConfig
{
    public required string BaseUrl { get; init; }
    public required string SiteId { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
}
