namespace DataMonger.Common;

public interface IConsoleCommand
{
    string Name { get; }

    /// <summary>
    /// Whether this command should be re-run once per configured site. Local file-only commands
    /// that ignore <see cref="CommandContext"/> (no API/site-specific behavior) should override
    /// this to false so they run exactly once regardless of how many sites are configured.
    /// </summary>
    bool RunsPerSite => true;

    Task<(int success, int failure)> Execute();
}
