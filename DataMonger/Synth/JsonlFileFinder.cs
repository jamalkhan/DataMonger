namespace DataMonger.Synth;

/// <summary>Locates the repo's LocalData/jsonl folder regardless of the console's working directory.</summary>
public static class JsonlFileFinder
{
    /// <summary>
    /// Walks upward from both the current directory and the executable's directory looking for a
    /// "LocalData/jsonl" folder, then returns the given subfolder under it (e.g. "profile", "products").
    /// </summary>
    public static string? FindJsonlSubfolder(string subfolder)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; dir is not null && i < 8; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "LocalData", "jsonl", subfolder);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Same walk-upward search as <see cref="FindJsonlSubfolder"/>, but returns the target subfolder
    /// path even if it doesn't exist yet (e.g. an output folder that hasn't been written to before),
    /// as long as the containing "LocalData/jsonl" root can be located.
    /// </summary>
    public static string ResolveJsonlSubfolder(string subfolder)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; dir is not null && i < 8; i++, dir = dir.Parent)
            {
                var jsonlRoot = Path.Combine(dir.FullName, "LocalData", "jsonl");
                if (Directory.Exists(jsonlRoot))
                {
                    return Path.Combine(jsonlRoot, subfolder);
                }
            }
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "LocalData", "jsonl", subfolder);
    }

    /// <summary>Lists *.jsonl files in a folder, most-recently-modified first.</summary>
    public static IReadOnlyList<string> ListJsonlFiles(string folder) =>
        Directory.GetFiles(folder, "*.jsonl")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
}
