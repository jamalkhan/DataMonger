using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using bleak.Data.Transformer.Output;
using DataMonger.Common;
using DataMonger.Jsonl;
using DataMonger.Synth;

namespace DataMonger.Commands;

/// <summary>
/// Generates fake customer-profile JSONL for the invented retailer "Acme Outfitters" (see Documentation/SynthesizeUserProps/Schema.md), without
/// reading any real CSVs. Output is split via <see cref="JsonlSplitWriter"/>; the source of truth is a config file
/// (*.synthconfig) rather than CSV exports: record count and every picklist value pool live there, so running this
/// repeatedly for new test scenarios means editing the config, not this command.
/// </summary>
public class SynthesizeUserProps(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Synthesize User Props (fake data)";
    public bool RunsPerSite => false;

    // Site-independent local generator; guard against the per-site run loop re-running it.
    private static bool _completedThisSession;

    public Task<(int success, int failure)> Execute()
    {
        _ = ctx; // local file generator: no site/API context needed.

        if (_completedThisSession)
        {
            Console.WriteLine("[skip] SynthesizeUserProps already ran this session (it is site-independent).");
            return Task.FromResult((0, 0));
        }

        try
        {
            var configPath = new ConfigFolders().PromptForConfig(ConfigKind.Synth);
            if (configPath is null)
            {
                Console.WriteLine("[FAIL] No config file provided or it does not exist. Aborting.");
                return Task.FromResult((0, 1));
            }

            SynthConfig config;
            try
            {
                config = SynthConfigLoader.FromFile(configPath);
            }
            catch (SynthConfigException ex)
            {
                Console.WriteLine($"[FAIL] Invalid config {configPath}:");
                foreach (var error in ex.Errors)
                {
                    Console.WriteLine($"  - {error}");
                }
                return Task.FromResult((0, 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[FAIL] Could not read config: {ex.Message}");
                return Task.FromResult((0, 1));
            }

            var recordCount = PromptForRecordCount(config.RecordCount);
            var outputFolder = PromptForOutputFolder();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var rng = SynthPrompts.CreateRandom(config.Seed);
            var now = config.ResolveAnchor(DateTimeOffset.UtcNow);

            Console.WriteLine();
            Console.WriteLine($"Config          : {configPath}");
            Console.WriteLine($"Records         : {recordCount:N0}");
            Console.WriteLine($"Brands          : {string.Join(", ", config.Brands.Select(b => b.Code))}");
            Console.WriteLine($"Seed            : {(config.Seed is { } s ? s.ToString(CultureInfo.InvariantCulture) : "(random)")}");
            Console.WriteLine($"Anchor (now)    : {now:yyyy-MM-dd HH:mm:ss}Z{(config.AnchorDate is null ? " (real clock)" : "")}");
            Console.WriteLine($"Output folder   : {outputFolder}");
            Console.WriteLine($"Split every     : {config.SplitEvery:N0} records");
            Console.WriteLine($"Run timestamp   : {timestamp}");
            if (config.Seed is not null && config.AnchorDate is null)
            {
                Console.WriteLine("[note] A seed without an anchorDate is only reproducible within the same day: dates are relative to the current time.");
            }
            Console.WriteLine();

            var sw = Stopwatch.StartNew();
            var synthesizer = new UserPropsSynthesizer(config, rng, now);

            // Step 1: generate exactly one record, rendered compact (for the eventual .jsonl line)
            // and prettified (for the preview file), and let the user sanity-check it before paying
            // for the full run.
            var buffer = new ArrayBufferWriter<byte>();
            using (var compact = new Utf8JsonWriter(buffer, JsonlRender.Options))
            {
                synthesizer.Write(compact, config.StartUserSeq);
                compact.Flush();
            }
            var firstRecordBytes = buffer.WrittenSpan.ToArray();

            var previewPath = Path.Combine(outputFolder, $"{config.FileBaseName}_{timestamp}.json");
            Directory.CreateDirectory(outputFolder);
            using (var doc = JsonDocument.Parse(firstRecordBytes))
            using (var previewStream = new FileStream(previewPath, FileMode.Create, FileAccess.Write))
            using (var pretty = new Utf8JsonWriter(previewStream, new JsonWriterOptions
            {
                Indented = true,
                Encoder = JsonlRender.Options.Encoder,
            }))
            {
                doc.RootElement.WriteTo(pretty);
            }

            Console.WriteLine($"Preview record written: {previewPath}");
            Console.WriteLine();

            if (!PromptContinueWithFullSet(recordCount))
            {
                Console.WriteLine("Stopped after the preview record. No .jsonl file was written.");
                _completedThisSession = true;
                return Task.FromResult((1, 0));
            }

            // Step 2: the full run. Record #1 is replayed verbatim from the preview so the .jsonl's
            // first line matches what the user just reviewed; records 2..N continue from the same rng.
            using var split = new JsonlSplitWriter(outputFolder, config.FileBaseName, timestamp, config.SplitEvery);
            split.WriteLine(firstRecordBytes);
            using (var json = new Utf8JsonWriter(buffer, JsonlRender.Options))
            {
                for (var i = 1; i < recordCount; i++)
                {
                    buffer.ResetWrittenCount();
                    json.Reset();
                    synthesizer.Write(json, config.StartUserSeq + i);
                    json.Flush();
                    split.WriteLine(buffer.WrittenSpan);
                }
            }
            split.Dispose();
            sw.Stop();

            Console.WriteLine("=== Done ===");
            Console.WriteLine($"Synthesized: {split.RecordsWritten:N0} records across {split.FinalPaths.Count} file(s):");
            foreach (var f in split.FinalPaths)
            {
                Console.WriteLine($"    {f}");
            }
            Console.WriteLine($"Elapsed: {sw.Elapsed:hh\\:mm\\:ss\\.fff}");

            _completedThisSession = true;
            return Task.FromResult(((int)Math.Min(int.MaxValue, split.RecordsWritten), 0));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return Task.FromResult((0, 1));
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Prompts
    // ----------------------------------------------------------------------------------------------
    private static int PromptForRecordCount(int def)
    {
        Console.Write($"Number of customers to synthesize [{def:N0}]: ");
        var input = Console.ReadLine()?.Trim().Replace(",", "");
        if (string.IsNullOrEmpty(input))
        {
            return def;
        }
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
        {
            return n;
        }
        Console.WriteLine($"  [warn] invalid number; using default {def:N0}.");
        return def;
    }

    private static bool PromptContinueWithFullSet(int recordCount)
    {
        while (true)
        {
            Console.Write($"Continue and generate the full set of {recordCount:N0} record(s) as .jsonl? [y/N]: ");
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

    private static string PromptForOutputFolder()
    {
        var def = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(def))
        {
            def = Directory.GetCurrentDirectory();
        }
        Console.Write($"Output folder for the .jsonl file(s) [{def}]: ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(input) ? def : input;
    }
}
