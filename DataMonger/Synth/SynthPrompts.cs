using DataMonger.Synth;

namespace DataMonger.Synth;

/// <summary>Shared console prompts and input helpers for the two event commands.</summary>
public static class SynthPrompts
{
    /// <summary>
    /// Lists *.jsonl files under LocalData/jsonl/&lt;subfolder&gt; and asks for a number, a path, or 'q'.
    /// Returns null when cancelled or at end of input.
    /// </summary>
    public static string? PromptForJsonlFile(string subfolder, string label)
    {
        var folder = JsonlFileFinder.FindJsonlSubfolder(subfolder);
        var files = folder is null
            ? Array.Empty<string>()
            : JsonlFileFinder.ListJsonlFiles(folder).ToArray();

        Console.WriteLine();
        if (files.Length > 0)
        {
            Console.WriteLine($"Available {label} jsonl files ({folder}):");
            for (var i = 0; i < files.Length; i++)
            {
                Console.WriteLine($"  {i + 1}) {Path.GetFileName(files[i])}");
            }
        }
        else
        {
            Console.WriteLine($"No *.jsonl files found under LocalData/jsonl/{subfolder}.");
        }

        while (true)
        {
            Console.Write($"Select a number, enter a path to your own {label} .jsonl, or 'q' to cancel: ");
            var line = Console.ReadLine();
            if (line is null)
            {
                return null;
            }
            var input = line.Trim().Trim('"');
            if (input.Length == 0)
            {
                continue;
            }
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (int.TryParse(input, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
                n >= 1 && n <= files.Length)
            {
                return files[n - 1];
            }
            if (File.Exists(input))
            {
                return input;
            }
            Console.WriteLine(files.Length > 0
                ? $"  [warn] enter 1-{files.Length}, a valid .jsonl path, or 'q'."
                : $"  [warn] file not found: {input}");
        }
    }

    /// <summary>Asks for an optional integer seed. Blank (or end of input) = null (random). Re-prompts on junk.</summary>
    public static int? PromptForSeed()
    {
        while (true)
        {
            Console.Write("Random seed (integer; blank = random, unrepeatable): ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }
            if (int.TryParse(input, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seed))
            {
                return seed;
            }
            Console.WriteLine("  [warn] enter a whole number or leave blank.");
        }
    }

    /// <summary>Asks for an optional anchor date (UTC, yyyy-MM-dd) used as "today". Blank = null (real clock). Re-prompts on junk.</summary>
    public static DateTimeOffset? PromptForAnchor()
    {
        while (true)
        {
            Console.Write("Anchor date used as \"today\", UTC yyyy-MM-dd (blank = the real current time): ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }
            if (SynthConfig.ParseAnchor(input) is { } anchor)
            {
                return anchor;
            }
            Console.WriteLine("  [warn] enter a date like 2026-10-01 or leave blank.");
        }
    }

    public static bool PromptYesNo(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input) || string.Equals(input, "n", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (string.Equals(input, "y", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            Console.WriteLine("  [warn] enter 'y' or 'n'.");
        }
    }

    public static string PromptForOutputFolder(string defaultFolder)
    {
        Console.Write($"Output folder for the .jsonl file(s) [{defaultFolder}]: ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(input) ? defaultFolder : input;
    }

    public static string DesktopOrCwd()
    {
        var def = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.IsNullOrEmpty(def) ? Directory.GetCurrentDirectory() : def;
    }

    /// <summary>Non-blank lines of a profile file, each paired with its 1-based line number.</summary>
    public static IEnumerable<(int LineNumber, string Text)> ReadProfileLines(string path)
    {
        var n = 0;
        foreach (var line in File.ReadLines(path))
        {
            n++;
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return (n, line);
            }
        }
    }

    /// <summary>Parses one profile line to a JSON object document; null when it is not a JSON object.</summary>
    public static System.Text.Json.JsonDocument? TryParseProfile(string line)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                return doc;
            }
            doc.Dispose();
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Builds the run's Random: seeded when <paramref name="seed"/> is set, otherwise unseeded.</summary>
    public static Random CreateRandom(int? seed) => seed is { } s ? new Random(s) : new Random();
}
