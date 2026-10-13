namespace DataMonger.Common;

/// <summary>
/// Resolves paths into the repo-level local data folder (sibling of DataMonger, gitignored), which
/// holds input/output data kept out of DataMonger/data so it's never bundled into the build output or
/// accidentally committed. Tries the build-output-relative path first (four levels up from
/// bin/Debug/net10.0 reaches the repo root), then falls back to resolving from the current working
/// directory (e.g. `dotnet run` from the repo root).
/// </summary>
public static class DataPaths
{
    public const string RootFolderName = "LocalData";

    public static string Root()
    {
        var fromBuildOutput = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", RootFolderName));
        return Directory.Exists(fromBuildOutput) ? fromBuildOutput : Path.Combine(Directory.GetCurrentDirectory(), RootFolderName);
    }

    public static string Combine(params string[] segments) => Path.Combine([Root(), .. segments]);
}
