using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using bleak.Data.Transformer.Output;
using DataMonger.Common;
using DataMonger.Synth;

namespace DataMonger.Commands;

/// <summary>
/// Generates fake marketing_opt_in events for each profile in a chosen profile .jsonl file (profiles as written by
/// Synthesize User Props): each profile has a 70% chance of a single opt-in event on a channel it consents to,
/// dated anywhere in the last 4 years. Unlike Synthesize Session Events, this is NOT tied to a shopping session -
/// see <see cref="OptinEventBuilder"/>. An optional seed and anchor date make the run reproducible.
/// </summary>
public class SynthesizeMyAccountOptins(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Synthesize MyAccount Optin Events (fake data)";
    public bool RunsPerSite => false;

    public Task<(int success, int failure)> Execute()
    {
        _ = ctx; // local file generator: no site/API context needed.

        try
        {
            var profilePath = SynthPrompts.PromptForJsonlFile("profile", "profile");
            if (profilePath is null)
            {
                Console.WriteLine("[FAIL] No profile file selected. Aborting.");
                return Task.FromResult((0, 1));
            }

            var outputFolder = SynthPrompts.PromptForOutputFolder(SynthPrompts.DesktopOrCwd());
            var seed = SynthPrompts.PromptForSeed();
            var anchor = SynthPrompts.PromptForAnchor();
            var now = anchor ?? DateTimeOffset.UtcNow;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            const string baseName = "marketing_opt_in_events";
            var rng = SynthPrompts.CreateRandom(seed);

            Console.WriteLine();
            Console.WriteLine($"Profile file    : {profilePath}");
            Console.WriteLine($"Output folder   : {outputFolder}");
            Console.WriteLine($"Seed            : {(seed is { } s ? s.ToString(CultureInfo.InvariantCulture) : "(random)")}");
            Console.WriteLine($"Anchor (now)    : {now:yyyy-MM-dd HH:mm:ss}Z{(anchor is null ? " (real clock)" : "")}");
            Console.WriteLine($"Run timestamp   : {timestamp}");
            if (seed is not null && anchor is null)
            {
                Console.WriteLine("[note] A seed without an anchor date is only reproducible within the same day: event times are relative to the current time.");
            }
            Console.WriteLine();

            using var profileLines = SynthPrompts.ReadProfileLines(profilePath).GetEnumerator();
            var skipped = 0;
            var profileCount = 0;

            // Step 1: roll the first usable profile and preview it (or note that it rolled no opt-in)
            // before paying for the full run across every profile in the file.
            JsonDocument? firstDoc = null;
            while (profileLines.MoveNext())
            {
                firstDoc = SynthPrompts.TryParseProfile(profileLines.Current.Text);
                if (firstDoc is not null)
                {
                    break;
                }
                skipped++;
                Console.WriteLine($"[WARN] Skipping line {profileLines.Current.LineNumber}: not a JSON object.");
            }
            if (firstDoc is null)
            {
                Console.WriteLine("[FAIL] Profile file has no usable profiles.");
                return Task.FromResult((0, 1));
            }

            byte[]? firstEvent;
            using (firstDoc)
            {
                profileCount = 1;
                firstEvent = OptinEventBuilder.BuildForProfile(firstDoc.RootElement, rng, now);
            }

            if (firstEvent is null)
            {
                Console.WriteLine("First profile rolled no opt-in event; no preview to show, but the full run may still produce some.");
            }
            else
            {
                var previewPath = Path.Combine(outputFolder, $"{baseName}_{timestamp}.json");
                Directory.CreateDirectory(outputFolder);
                using var doc = JsonDocument.Parse(firstEvent);
                using var fileStream = new FileStream(previewPath, FileMode.Create, FileAccess.Write);
                using var pretty = new Utf8JsonWriter(fileStream, new JsonWriterOptions
                {
                    Indented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
                doc.RootElement.WriteTo(pretty);
                Console.WriteLine($"Preview event written: {previewPath}");
            }
            Console.WriteLine();

            if (!SynthPrompts.PromptYesNo("Continue and generate opt-in events for every profile in the file? [y/N]: "))
            {
                Console.WriteLine("Stopped after the preview event. No .jsonl file was written.");
                return Task.FromResult((1, 0));
            }

            // Step 2: the full run. The first profile's roll is replayed verbatim (same rng draw as
            // the preview); every subsequent profile continues from the same rng.
            using var split = new JsonlSplitWriter(outputFolder, baseName, timestamp, recordsPerFile: 50_000);
            if (firstEvent is not null)
            {
                split.WriteLine(firstEvent);
            }

            while (profileLines.MoveNext())
            {
                using var doc = SynthPrompts.TryParseProfile(profileLines.Current.Text);
                if (doc is null)
                {
                    skipped++;
                    Console.WriteLine($"[WARN] Skipping line {profileLines.Current.LineNumber}: not a JSON object.");
                    continue;
                }
                profileCount++;
                var evt = OptinEventBuilder.BuildForProfile(doc.RootElement, rng, now);
                if (evt is not null)
                {
                    split.WriteLine(evt);
                }
            }
            split.Dispose();

            Console.WriteLine("=== Done ===");
            Console.WriteLine($"Profiles processed: {profileCount:N0}" + (skipped > 0 ? $" ({skipped:N0} line(s) skipped)" : ""));
            Console.WriteLine($"Opt-in events     : {split.RecordsWritten:N0} across {split.FinalPaths.Count} file(s):");
            foreach (var f in split.FinalPaths)
            {
                Console.WriteLine($"    {f}");
            }

            return Task.FromResult(((int)Math.Min(int.MaxValue, split.RecordsWritten), 0));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return Task.FromResult((0, 1));
        }
    }
}
