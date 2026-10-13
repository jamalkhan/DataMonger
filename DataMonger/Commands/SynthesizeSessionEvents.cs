using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using bleak.Data.Transformer.Output;
using DataMonger.Common;
using DataMonger.Synth;

namespace DataMonger.Commands;

/// <summary>
/// Generates fake browsing-session activity for each profile in a chosen profile .jsonl file (profiles as written by
/// Synthesize User Props): page_view, add_to_cart, remove_from_cart and (30% of the time, when eligible) a single
/// purchase event, all interleaved within one shared 45-minute session per profile. See
/// <see cref="SessionEventsBuilder"/> for the interleaving/eligibility rules and event shapes. An optional seed and
/// anchor date make the run reproducible.
/// </summary>
public class SynthesizeSessionEvents(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Synthesize Session Events (page views/cart/purchase, fake data)";
    public bool RunsPerSite => false;

    public async Task<(int success, int failure)> Execute()
    {
        try
        {
            var profilePath = SynthPrompts.PromptForJsonlFile("profile", "profile");
            if (profilePath is null)
            {
                Console.WriteLine("[FAIL] No profile file selected. Aborting.");
                return (0, 1);
            }

            var productPath = SynthPrompts.PromptForJsonlFile("products", "product");
            if (productPath is null)
            {
                Console.WriteLine("[FAIL] No product file selected. Aborting.");
                return (0, 1);
            }

            Console.WriteLine($"Loading product catalog: {productPath}");
            var catalog = ProductCatalog.Load(productPath);
            Console.WriteLine($"  {catalog.Count:N0} purchasable products loaded " +
                $"({catalog.SkippedInvalid:N0} invalid, {catalog.SkippedDuplicate:N0} duplicate, {catalog.SkippedOutOfStock:N0} out-of-stock rows skipped).");

            var defaultOutput = JsonlFileFinder.ResolveJsonlSubfolder(Path.Combine("events", "Loads"));
            var outputFolder = SynthPrompts.PromptForOutputFolder(defaultOutput);
            var seed = SynthPrompts.PromptForSeed();
            var anchor = SynthPrompts.PromptForAnchor();
            var now = anchor ?? DateTimeOffset.UtcNow;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            const string baseName = "session_events";
            var rng = SynthPrompts.CreateRandom(seed);

            Console.WriteLine();
            Console.WriteLine($"Profile file    : {profilePath}");
            Console.WriteLine($"Product file    : {productPath}");
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

            // Step 1: build every event for the FIRST usable profile only, and write each one out as its
            // own file under a preview folder so the user can sanity-check the whole session before
            // paying for the full run across every profile in the file.
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
                return (0, 1);
            }

            List<SessionEvent> firstEvents;
            string? firstUserId;
            using (firstDoc)
            {
                profileCount = 1;
                firstEvents = SessionEventsBuilder.BuildForProfile(firstDoc.RootElement, catalog, rng, now, forceFullFunnel: true);
                firstUserId = ProfileFields.ResolveUserId(firstDoc.RootElement)
                    ?? SanitizeForFileName(ProfileFields.ResolveEmail(firstDoc.RootElement) ?? "user");
            }

            var previewFolder = Path.Combine(outputFolder, $"{baseName}_{timestamp}_user_events");
            if (firstEvents.Count == 0)
            {
                Console.WriteLine("First profile has no resolvable email (no session); no preview to show, but the full run may still produce sessions for other profiles.");
            }
            else
            {
                Directory.CreateDirectory(previewFolder);
                Console.WriteLine($"Preview session written to: {previewFolder}");
                foreach (var evt in firstEvents)
                {
                    var fileName = BuildEventFileName(previewFolder, firstUserId!, evt);
                    var fullPath = Path.Combine(previewFolder, fileName);
                    using var doc = JsonDocument.Parse(evt.Json);
                    using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                    using var pretty = new Utf8JsonWriter(fileStream, new JsonWriterOptions
                    {
                        Indented = true,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    });
                    doc.RootElement.WriteTo(pretty);
                    Console.WriteLine($"    {fileName}  ({evt.EventType})");
                }
            }
            Console.WriteLine();

            if (!SynthPrompts.PromptYesNo("Continue and generate session events for every profile in the file? [y/N]: "))
            {
                Console.WriteLine("Stopped after the preview session. No .jsonl file was written.");
                return (1, 0);
            }

            // Step 2: the full run. The first profile's events are replayed verbatim (same rng draws
            // as the preview); every subsequent profile continues from the same rng/catalog. All
            // event types land in one combined, chronologically-ordered .jsonl per profile.
            using var split = new JsonlSplitWriter(outputFolder, baseName, timestamp, recordsPerFile: 50_000);
            var eventTypeTotals = new Dictionary<string, int>();
            foreach (var evt in firstEvents)
            {
                split.WriteLine(evt.Json);
                eventTypeTotals[evt.EventType] = eventTypeTotals.GetValueOrDefault(evt.EventType) + 1;
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
                foreach (var evt in SessionEventsBuilder.BuildForProfile(doc.RootElement, catalog, rng, now))
                {
                    split.WriteLine(evt.Json);
                    eventTypeTotals[evt.EventType] = eventTypeTotals.GetValueOrDefault(evt.EventType) + 1;
                }
            }
            split.Dispose();

            Console.WriteLine("=== Done ===");
            Console.WriteLine($"Profiles processed: {profileCount:N0}" + (skipped > 0 ? $" ({skipped:N0} line(s) skipped)" : ""));
            foreach (var (eventType, count) in eventTypeTotals.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {eventType,-18}: {count:N0}");
            }
            Console.WriteLine($"Total events      : {split.RecordsWritten:N0} across {split.FinalPaths.Count} file(s):");
            foreach (var f in split.FinalPaths)
            {
                Console.WriteLine($"    {f}");
            }

            if (split.FinalPaths.Count > 0 && PromptUploadNow(split.FinalPaths.Count))
            {
                Console.WriteLine();
                Console.WriteLine("=== API => Upload JSONL Events (track event) ===");
                await new UploadJsonlEvents(ctx).Execute(split.FinalPaths);
            }

            return ((int)Math.Min(int.MaxValue, split.RecordsWritten), 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return (0, 1);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Preview file naming
    // ----------------------------------------------------------------------------------------------
    private static string BuildEventFileName(string folder, string userId, SessionEvent evt)
    {
        var eventTimestamp = evt.Timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var baseName = $"{SanitizeForFileName(userId)}_{eventTimestamp}_{evt.EventType}";
        var fileName = $"{baseName}.json";
        var suffix = 2;
        while (File.Exists(Path.Combine(folder, fileName)))
        {
            fileName = $"{baseName}_{suffix}.json";
            suffix++;
        }
        return fileName;
    }

    private static string SanitizeForFileName(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }
        var result = sb.ToString().Trim('_');
        return result.Length == 0 ? "user" : result;
    }

    private static bool PromptUploadNow(int fileCount)
    {
        var plural = fileCount == 1 ? "file" : "files";
        return SynthPrompts.PromptYesNo($"Upload the {fileCount} generated .jsonl {plural} now via API => Upload JSONL Events (track event)? [y/N]: ");
    }
}
