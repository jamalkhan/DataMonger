using System.Diagnostics;
using System.Globalization;
using bleak.Data.Anonymizer;
using bleak.Data.Anonymizer.Config;
using bleak.Data.Anonymizer.Engine;
using DataMonger.Common;

namespace DataMonger.Commands;

/// <summary>
/// Config-driven PII anonymization. Reads an <c>.anonconfig</c> describing, per input file, which columns
/// to replace with obviously-fake values (Simpsons-based names/e-mails, 555 phone numbers, etc.), and writes
/// the results to a separate output folder with each file renamed (e.g. <c>src_</c> → <c>anonymized_</c>).
///
/// Originals are never modified or deleted. Prompts for the config (from the saved anonymization config folder, asking for the folder on first run) and (when the config omits it) the
/// output folder; everything else — input files, columns, methods — comes from the config. Mirrors
/// <see cref="ExtractTransformLoad"/>.
/// </summary>
public class AnonymizePii(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Anonymize PII (config-driven)";
    public bool RunsPerSite => false;

    // Site-independent local transform; guard against the per-site run loop re-running it.
    private static bool _completedThisSession;

    public Task<(int success, int failure)> Execute()
    {
        _ = ctx; // local file transform: no site/API context needed.

        if (_completedThisSession)
        {
            Console.WriteLine("[skip] AnonymizePii already ran this session (it is site-independent).");
            return Task.FromResult((0, 0));
        }

        try
        {
            var configPath = new ConfigFolders().PromptForConfig(ConfigKind.Anon);
            if (configPath is null)
            {
                Console.WriteLine("[FAIL] No config file provided or it does not exist. Aborting.");
                return Task.FromResult((0, 1));
            }

            AnonConfig config;
            try
            {
                config = AnonConfigLoader.FromFile(configPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not parse config: {ex.Message}");
                return Task.FromResult((0, 1));
            }

            var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;

            // Only prompt for a fallback output folder when the config doesn't set one.
            var needsFolder = string.IsNullOrWhiteSpace(config.OutputFolder);
            var outputFolder = needsFolder ? PromptForOutputFolder() : null;

            Console.WriteLine();
            Console.WriteLine($"Config        : {configPath}");
            Console.WriteLine($"Files         : {config.Files.Count}");
            Console.WriteLine($"On error      : {config.Run.OnError}");
            Console.WriteLine($"Output folder : {(needsFolder ? outputFolder : "(from config)")}");
            Console.WriteLine($"Rename        : {config.Run.InputPrefix}* -> {config.Run.OutputPrefix}*");
            Console.WriteLine();

            var overall = Stopwatch.StartNew();
            var results = AnonymizeEngine.Run(config, configDir, outputFolder, log: Console.WriteLine);
            overall.Stop();

            Console.WriteLine();
            Console.WriteLine("=== Done ===");
            long totalRows = 0, totalCells = 0, failures = 0;
            foreach (var r in results)
            {
                if (r.Skipped is not null)
                {
                    failures++;
                    Console.WriteLine($"{Path.GetFileName(r.Input)}: SKIPPED ({r.Skipped})");
                    continue;
                }
                totalRows += r.Rows;
                totalCells += r.CellsAnonymized;
                var detail = r.Copied
                    ? "copied (no PII columns)"
                    : $"{r.Rows:N0} rows, {r.CellsAnonymized:N0} cells anonymized"
                      + (r.Errors > 0 ? $", {r.Errors:N0} error(s)" : "")
                      + (r.MissingColumns.Count > 0 ? $", missing columns: {string.Join(", ", r.MissingColumns)}" : "");
                Console.WriteLine($"{Path.GetFileName(r.Output)}: {detail}");
            }
            Console.WriteLine();
            Console.WriteLine($"Files: {results.Count}, rows anonymized: {totalRows:N0}, cells: {totalCells:N0}");
            Console.WriteLine($"Total elapsed: {overall.Elapsed:hh\\:mm\\:ss\\.fff}");

            _completedThisSession = true;
            return Task.FromResult(((int)Math.Min(int.MaxValue, totalRows), (int)failures));
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
        Console.Write($"Output folder for the anonymized files [{def}]: ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(input) ? def : input;
    }
}
