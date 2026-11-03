using DataMonger.Common;

namespace DataMonger.Tests;

/// <summary>Placeholder per-site contexts for tests that construct commands (no real site or credentials).</summary>
internal static class TestContexts
{
    public static CommandContext Site()
    {
        var site = new SiteConfig { BaseUrl = "https://api.example.com", SiteId = "demo", Username = "demo", Password = "placeholder" };
        return new CommandContext
        {
            ActivityUri = new Uri($"{site.BaseUrl}/ver2/{site.SiteId}/activities"),
            IdentifyUri = new Uri($"{site.BaseUrl}/ver2/{site.SiteId}/subscribers/identify"),
            BaseUrl = site.BaseUrl,
            SiteId = site.SiteId,
            Username = site.Username,
            Password = site.Password,
            MaxParallelism = 1,
            AllSites = [site],
        };
    }
}
