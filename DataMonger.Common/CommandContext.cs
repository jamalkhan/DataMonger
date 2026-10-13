using bleak.Api.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataMonger.Common;

public class CommandContext
{
    public required IReadOnlyList<Product> Products { get; init; }
    public required RestClient Rest { get; init; }
    public required Uri ActivityUri { get; init; }
    public required Uri IdentifyUri { get; init; }

    /// <summary>Creates category-scoped loggers for commands to use. Defaults to a no-op factory so
    /// existing construction sites (tests, etc.) remain valid without wiring real logging.</summary>
    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

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
