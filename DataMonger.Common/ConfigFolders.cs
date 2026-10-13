using System.Globalization;
using System.Text.Json;

namespace DataMonger.Common;

/// <summary>The kinds of user-editable config file DataMonger commands look for.</summary>
public enum ConfigKind
{
    /// <summary><c>.etlconfig</c> files used by Extract / Transform / Load.</summary>
    Etl,

    /// <summary><c>.anonconfig</c> files used by Anonymize PII.</summary>
    Anon,

    /// <summary><c>.synthconfig</c> files used by Synthesize User Props.</summary>
    Synth,
}

/// <summary>
/// Locates the folder each kind of config lives in. The first time a command needs a kind of config it asks
/// where that folder is (default <c>~/Documents/DataMonger/Config/&lt;Kind&gt;Config</c>), creates it, copies in the
/// bundled example configs if it has none, and remembers the answer in <c>settings.json</c> under the DataMonger
/// home folder. Set the <c>DATAMONGER_HOME</c> environment variable to relocate the home folder
/// (default <c>~/Documents/DataMonger</c>).
/// </summary>
public sealed class ConfigFolders
{
    private const string HomeEnvVar = "DATAMONGER_HOME";
    private const string SettingsFileName = "settings.json";

    private readonly TextReader _in;
    private readonly TextWriter _out;

    public ConfigFolders(TextReader? input = null, TextWriter? output = null)
    {
        _in = input ?? Console.In;
        _out = output ?? Console.Out;
    }

    /// <summary>The DataMonger home folder: <c>$DATAMONGER_HOME</c>, else <c>~/Documents/DataMonger</c>.</summary>
    public static string HomeFolder()
    {
        var overridden = Environment.GetEnvironmentVariable(HomeEnvVar);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(ExpandHome(overridden));
        }
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
        {
            documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        }
        return Path.Combine(documents, "DataMonger");
    }

    /// <summary>Default config folder for a kind, e.g. <c>~/Documents/DataMonger/Config/SynthConfig</c>.</summary>
    public static string DefaultFolder(ConfigKind kind) => Path.Combine(HomeFolder(), "Config", FolderName(kind));

    public static string SettingsPath() => Path.Combine(HomeFolder(), SettingsFileName);

    public static string Extension(ConfigKind kind) => kind switch
    {
        ConfigKind.Etl => ".etlconfig",
        ConfigKind.Anon => ".anonconfig",
        ConfigKind.Synth => ".synthconfig",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string FolderName(ConfigKind kind) => kind switch
    {
        ConfigKind.Etl => "EtlConfig",
        ConfigKind.Anon => "AnonConfig",
        ConfigKind.Synth => "SynthConfig",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string Label(ConfigKind kind) => kind switch
    {
        ConfigKind.Etl => "ETL",
        ConfigKind.Anon => "anonymization",
        ConfigKind.Synth => "synthetic-data",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The folder saved for a kind in settings.json, or null if none was saved yet.</summary>
    public static string? SavedFolder(ConfigKind kind)
    {
        var settings = LoadSettings();
        return settings.TryGetValue(kind.ToString(), out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    }

    /// <summary>
    /// Returns the config folder for <paramref name="kind"/>. Uses the saved folder when it still exists;
    /// otherwise prompts (first run, or the saved folder was deleted) and saves the answer.
    /// </summary>
    public string Resolve(ConfigKind kind)
    {
        var saved = SavedFolder(kind);
        if (saved is not null && Directory.Exists(saved))
        {
            return saved;
        }
        return Choose(kind, firstRun: saved is null);
    }

    /// <summary>Asks for a new config folder for <paramref name="kind"/> and saves it.</summary>
    public string Change(ConfigKind kind) => Choose(kind, firstRun: false);

    private string Choose(ConfigKind kind, bool firstRun)
    {
        var def = SavedFolder(kind) ?? DefaultFolder(kind);
        _out.WriteLine();
        _out.WriteLine(firstRun
            ? $"First run: choose the folder where your {Label(kind)} config files ({Extension(kind)}) live."
            : $"Choose the folder for your {Label(kind)} config files ({Extension(kind)}).");
        _out.Write($"Config folder [{def}]: ");
        var line = _in.ReadLine()?.Trim().Trim('"');
        var folder = Path.GetFullPath(ExpandHome(string.IsNullOrEmpty(line) ? def : line));

        Directory.CreateDirectory(folder);
        var copied = SeedExamples(kind, folder);
        if (copied > 0)
        {
            _out.WriteLine($"  Copied {copied} example {Extension(kind)} file(s) into {folder}");
        }

        var settings = LoadSettings();
        settings[kind.ToString()] = folder;
        SaveSettings(settings);
        _out.WriteLine($"  Saved. Using {folder} (settings: {SettingsPath()})");
        return folder;
    }

    /// <summary>Lists the config files in the folder, ordered by name.</summary>
    public static IReadOnlyList<string> List(ConfigKind kind, string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*" + Extension(kind)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();

    /// <summary>
    /// Shows the configs in the configured folder and lets the user pick one by number, type a path to their
    /// own config, type <c>c</c> to change the config folder, or <c>q</c> to cancel. Returns null if cancelled.
    /// </summary>
    public string? PromptForConfig(ConfigKind kind)
    {
        var ext = Extension(kind);
        var folder = Resolve(kind);
        while (true)
        {
            var configs = List(kind, folder);
            _out.WriteLine();
            if (configs.Count > 0)
            {
                _out.WriteLine($"Available {Label(kind)} configs ({folder}):");
                for (var i = 0; i < configs.Count; i++)
                {
                    _out.WriteLine($"  {i + 1}) {Path.GetFileName(configs[i])}");
                }
            }
            else
            {
                _out.WriteLine($"No {ext} files found in {folder}.");
            }

            while (true)
            {
                _out.Write($"Select a number, enter a path to your own {ext}, 'c' to change the config folder, or 'q' to cancel: ");
                var line = _in.ReadLine();
                if (line is null)
                {
                    return null; // EOF / no console input -> cancel (don't spin)
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
                if (string.Equals(input, "c", StringComparison.OrdinalIgnoreCase))
                {
                    folder = Change(kind);
                    break; // re-list the new folder
                }
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    && n >= 1 && n <= configs.Count)
                {
                    return configs[n - 1];
                }

                var path = ExpandHome(input); // allow ~/... custom paths
                if (File.Exists(path))
                {
                    return path;
                }

                _out.WriteLine(configs.Count > 0
                    ? $"  [warn] enter 1-{configs.Count}, a valid {ext} path, 'c', or 'q'."
                    : $"  [warn] file not found: {input}");
            }
        }
    }

    /// <summary>Copies the bundled example configs into an empty config folder. Returns how many were copied.</summary>
    private static int SeedExamples(ConfigKind kind, string folder)
    {
        if (List(kind, folder).Count > 0)
        {
            return 0;
        }
        string[] sources =
        {
            Path.Combine(Directory.GetCurrentDirectory(), "data", FolderName(kind).ToLowerInvariant()),
            Path.Combine(AppContext.BaseDirectory, "data", FolderName(kind).ToLowerInvariant()),
        };
        var dir = sources.FirstOrDefault(Directory.Exists);
        if (dir is null)
        {
            return 0;
        }
        var copied = 0;
        foreach (var file in Directory.GetFiles(dir, "*" + Extension(kind)))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: false);
            copied++;
        }
        return copied;
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }
        return path;
    }

    private static Dictionary<string, string> LoadSettings()
    {
        try
        {
            var path = SettingsPath();
            if (File.Exists(path))
            {
                var root = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path));
                if (root is not null && root.TryGetValue("ConfigFolders", out var folders))
                {
                    return new Dictionary<string, string>(folders, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable or corrupt settings: treat as first run.
        }
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void SaveSettings(Dictionary<string, string> folders)
    {
        Directory.CreateDirectory(HomeFolder());
        var json = JsonSerializer.Serialize(
            new Dictionary<string, Dictionary<string, string>> { ["ConfigFolders"] = folders },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath(), json);
    }
}
