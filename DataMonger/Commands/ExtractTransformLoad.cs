using System.Diagnostics;
using System.Globalization;
using bleak.Data.Transformer;
using bleak.Data.Transformer.Config;
using bleak.Data.Transformer.Engine;
using DataMonger.Common;

namespace DataMonger.Commands;

/// <summary>
/// Config-driven ETL. Instead of hardcoded sources,
/// joins, field maps, and output shapes, this command reads an ETL config (JSON) describing the sources,
/// relationships, typed fields, transform pipelines, and load settings, and runs the
/// <see cref="EtlEngine"/>. Prompts for the config (from the saved ETL config folder, asking for the folder on first run) and the output folder; everything else
/// (input files, split size, join strategy, error policy) comes from the config.
/// </summary>
public class ExtractTransformLoad(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Extract / Transform / Load (config-driven)";
    public bool RunsPerSite => false;

    // Site-independent local transform; guard against the per-site run loop re-running it.
    private static bool _completedThisSession;

    public Task<(int success, int failure)> Execute()
    {
        _ = ctx; // local file transform: no site/API context needed.

        if (_completedThisSession)
        {
            Console.WriteLine("[skip] ExtractTransformLoad already ran this session (it is site-independent).");
            return Task.FromResult((0, 0));
        }

        try
        {
            var configPath = new ConfigFolders().PromptForConfig(ConfigKind.Etl);
            if (configPath is null)
            {
                Console.WriteLine("[FAIL] No config file provided or it does not exist. Aborting.");
                return Task.FromResult((0, 1));
            }

            EtlConfig config;
            try
            {
                config = EtlConfigLoader.FromFile(configPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not parse config: {ex.Message}");
                return Task.FromResult((0, 1));
            }

            var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;

            // Outputs may carry their own load.folder; only prompt for a fallback if some output lacks one.
            var needsFolder = config.Outputs.Any(o => string.IsNullOrWhiteSpace(o.Load.Folder));
            var outputFolder = needsFolder ? PromptForOutputFolder() : "";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            Console.WriteLine();
            Console.WriteLine($"Config        : {configPath}");
            Console.WriteLine($"Sources       : {config.Sources.Count}");
            Console.WriteLine($"Outputs       : {config.Outputs.Count}");
            Console.WriteLine($"Join strategy : {config.Run.JoinStrategy}");
            Console.WriteLine($"On error      : {config.Run.OnError}");
            Console.WriteLine($"Sample        : {(config.Run.Sample is { PrimaryRows: > 0 } s
                ? $"first {s.PrimaryRows:N0} primary row(s), scoped by {s.KeyColumn} (SAMPLE RUN)"
                : "(full run)")}");
            Console.WriteLine($"Max records   : {(config.Run.MaxRecords is > 0 ? $"{config.Run.MaxRecords:N0}" : "(no cap)")}");
            Console.WriteLine($"Output folder : {(needsFolder ? outputFolder : "(from config)")}");
            Console.WriteLine($"Run timestamp : {timestamp}");
            Console.WriteLine();

            var overall = Stopwatch.StartNew();
            var results = EtlEngine.Run(config, configDir, outputFolder, timestamp, Console.WriteLine);
            overall.Stop();

            Console.WriteLine();
            Console.WriteLine("=== Done ===");
            long total = 0;
            foreach (var r in results)
            {
                total += r.Records;
                Console.WriteLine($"{r.Name}: {r.Records:N0} records across {r.Files.Count} file(s)"
                    + (r.Rejected > 0 ? $", {r.Rejected:N0} skipped" : "")
                    + (r.Filtered > 0 ? $", {r.Filtered:N0} filtered out" : "")
                    + (r.Warnings > 0 ? $", {r.Warnings:N0} warnings" : "") + ":");
                foreach (var f in r.Files)
                {
                    Console.WriteLine($"    {f}");
                }
            }
            Console.WriteLine($"Total elapsed: {overall.Elapsed:hh\\:mm\\:ss\\.fff}");

            _completedThisSession = true;
            return Task.FromResult(((int)Math.Min(int.MaxValue, total), 0));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return Task.FromResult((0, 1));
        }
    }

    private static string PromptForOutputFolder()
    {
        var def = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(def))
        {
            def = Directory.GetCurrentDirectory();
        }
        Console.Write($"Output folder for the .jsonl files [{def}]: ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(input) ? def : input;
    }
}
