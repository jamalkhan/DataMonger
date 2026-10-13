using System.Globalization;
using System.Text;
using DataMonger.Common;
using bleak.Data.InfiniteDataLoader;
using Newtonsoft.Json.Linq;

namespace DataMonger.Commands;

/// <summary>
/// Generates a synthetic email-marketing event stream (one JSONL row per identified customer, each row
/// holding a short, time-ordered sequence of email_sent / email_delivered / email_opened / email_clicked /
/// email_bounced / purchase events). Output is consumed by <see cref="ApiEmailConversionEvents"/>.
/// </summary>
public class GenerateEmailConversions(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Generate Email Conversion data";

    // The command only reads local files and writes one fixed output file; it never touches the site
    // API, so running it once per configured site would just overwrite the same file repeatedly.
    public bool RunsPerSite => false;

    /// <summary>Anchor ("now") used when a seed is supplied and no explicit anchor is given, so that the
    /// same seed always yields byte-identical output.</summary>
    public static readonly DateTimeOffset DefaultSeededAnchor = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    // --- Neutral campaign pools -----------------------------------------------------------------

    static readonly (string Slug, string Subject, string Preheader)[] EmailCopy =
    {
        ("spring-arrivals",   "New Spring Arrivals: 30% Off",          "Refresh your wardrobe with this season's must-haves"),
        ("denim-days",        "Denim Days: Up to 50% Off",             "Find your perfect fit"),
        ("cozy-layers",       "Cozy Layers for Cooler Days",           "Hoodies, sweaters, and more"),
        ("final-sale",        "Last Chance: Final Sale",               "Limited stock, shop before it's gone"),
        ("wardrobe-refresh",  "Your Wardrobe Refresh Awaits",          "Easy looks for every occasion"),
        ("kids-collection",   "New Collection for Kids",               "Playful styles for the whole family"),
        ("essentials-sale",   "Acme Essentials, On Sale",              "Soft layers you'll live in"),
        ("workwear",          "Workwear Reimagined",                   "Polished pieces for desk to dinner"),
    };

    static readonly (string Type, string Reason)[] BounceReasons =
    {
        ("hard", "mailbox does not exist"),
        ("soft", "mailbox full"),
        ("soft", "temporary server error"),
        ("hard", "domain not found"),
    };

    static readonly string[] Devices = { "desktop", "mobile", "tablet" };

    const string BrandMain = "Acme";
    const string BrandKids = "Acme Kids";

    // --- Entry point ----------------------------------------------------------------------------

    public async Task<(int success, int failure)> Execute()
    {
        var people = CsvLoader.ReadIdentified(DataPaths.Combine("identified.csv")).ToList();
        var products = ctx.Products.ToArray();
        if (products.Length == 0)
        {
            Console.WriteLine("[FAIL] products.csv is empty: cannot pick featured products.");
            return (0, 1);
        }

        var seed = PromptForSeed();
        var rnd = seed.HasValue ? new Random(seed.Value) : new Random();
        var anchor = seed.HasValue ? DefaultSeededAnchor : DateTimeOffset.UtcNow;
        Console.WriteLine(seed.HasValue
            ? $"Seed {seed.Value}: output is reproducible (events are anchored at {FormatIsoMs(anchor)})."
            : "No seed: output differs on every run (events are anchored at the current time).");

        var outputPath = DataPaths.Combine("EmailConversion.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var rows = Generate(people, products, rnd, anchor);

        await using (var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            foreach (var row in rows)
            {
                await writer.WriteLineAsync(row.ToString(Newtonsoft.Json.Formatting.None));
            }
        }

        var totalEvents = rows.Sum(r => ((JArray)r["events"]!).Count);
        Console.WriteLine($"Wrote {rows.Count} rows ({totalEvents} events total) to {outputPath}");
        return (rows.Count, 0);
    }

    static int? PromptForSeed()
    {
        while (true)
        {
            Console.Write("Random seed (integer; blank = unseeded, different every run): ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }
            if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            {
                return seed;
            }
            Console.WriteLine($"'{line.Trim()}' is not a 32-bit integer. Try again, or press Enter for no seed.");
        }
    }

    // --- Generation -----------------------------------------------------------------------------

    /// <summary>
    /// Builds one row per person. Row shape:
    /// <c>{"recipient":{"user_id","first_name","last_name","email"},"events":[ {event}, ... ]}</c>.
    /// All randomness comes from <paramref name="rnd"/> and all timestamps are relative to
    /// <paramref name="anchor"/>, so a seeded <see cref="Random"/> and a fixed anchor give identical output.
    /// </summary>
    public static List<JObject> Generate(IReadOnlyList<IdentifiedPerson> people, IReadOnlyList<Product> products, Random rnd, DateTimeOffset anchor)
    {
        if (products.Count == 0)
        {
            throw new ArgumentException("At least one product is required.", nameof(products));
        }

        var campaigns = BuildCampaigns(products, rnd, anchor);
        var rows = new List<JObject>(people.Count);
        foreach (var person in people)
        {
            rows.Add(BuildRow(person, campaigns, products, rnd, anchor));
        }
        return rows;
    }

    sealed record Campaign(int Id, string Name, string Brand, string Category, string Subject, string Preheader, Product Featured);

    static Campaign[] BuildCampaigns(IReadOnlyList<Product> products, Random rnd, DateTimeOffset anchor)
    {
        var dateTag = anchor.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return EmailCopy.Select((copy, i) =>
        {
            var featured = products[rnd.Next(products.Count)];
            var kids = featured.Subcategory.Equals("Kids", StringComparison.OrdinalIgnoreCase)
                    || featured.Subcategory.Equals("Baby", StringComparison.OrdinalIgnoreCase);
            return new Campaign(
                Id: 5001 + i,
                Name: $"{copy.Slug}-{dateTag}",
                Brand: kids ? BrandKids : BrandMain,
                Category: featured.Category,
                Subject: copy.Subject,
                Preheader: copy.Preheader,
                Featured: featured);
        }).ToArray();
    }

    static JObject BuildRow(IdentifiedPerson person, Campaign[] campaigns, IReadOnlyList<Product> products, Random rnd, DateTimeOffset anchor)
    {
        var campaign = campaigns[rnd.Next(campaigns.Length)];
        var messageId = $"msg-{RandomHex(rnd, 16)}";
        var device = Devices[rnd.Next(Devices.Length)];

        // email_sent: uniform in the 7 days before the anchor
        var sentTime = anchor.AddSeconds(-rnd.NextDouble() * 7 * 24 * 3600);
        var events = new JArray { BuildEvent("email_sent", sentTime, person, campaign, messageId, rnd) };

        // 5% bounce (terminal), otherwise delivered
        if (rnd.NextDouble() < 0.05)
        {
            var bounce = BounceReasons[rnd.Next(BounceReasons.Length)];
            var bounced = BuildEvent("email_bounced", sentTime.AddSeconds(rnd.NextDouble() * 30 * 60), person, campaign, messageId, rnd);
            bounced["bounce_type"] = bounce.Type;
            bounced["bounce_reason"] = bounce.Reason;
            events.Add(bounced);
            return BuildRow(person, events);
        }

        var lastTime = sentTime.AddSeconds(rnd.NextDouble() * 30 * 60);
        events.Add(BuildEvent("email_delivered", lastTime, person, campaign, messageId, rnd));

        // 20% never open
        if (rnd.NextDouble() < 0.20)
        {
            return BuildRow(person, events);
        }

        lastTime = lastTime.AddSeconds(rnd.NextDouble() * 48 * 3600);
        events.Add(BuildOpen(lastTime));

        // After an open: 60% stop, 10% open again, 30% click. After a click: 40% purchase. Every step moves time forward.
        var state = ChainState.JustOpened;
        const int maxChainSteps = 20; // defensive cap
        for (var step = 0; step < maxChainSteps && state != ChainState.Closed; step++)
        {
            if (state == ChainState.JustOpened)
            {
                var r = rnd.NextDouble();
                if (r < 0.60)
                {
                    state = ChainState.Closed;
                }
                else if (r < 0.70)
                {
                    lastTime = lastTime.AddSeconds(rnd.NextDouble() * 48 * 3600);
                    events.Add(BuildOpen(lastTime));
                }
                else
                {
                    lastTime = lastTime.AddSeconds(rnd.NextDouble() * 120);
                    var click = BuildEvent("email_clicked", lastTime, person, campaign, messageId, rnd);
                    click["link_url"] = AddTrackingParams(campaign.Featured.Url, campaign);
                    click["device_type"] = device;
                    events.Add(click);
                    state = ChainState.JustClicked;
                }
            }
            else
            {
                if (rnd.NextDouble() < 0.40)
                {
                    lastTime = lastTime.AddSeconds(rnd.NextDouble() * 600);
                    events.Add(BuildPurchaseEvent(person, campaign, messageId, products, rnd, lastTime));
                }
                state = ChainState.Closed;
            }
        }

        return BuildRow(person, events);

        JObject BuildOpen(DateTimeOffset t)
        {
            var e = BuildEvent("email_opened", t, person, campaign, messageId, rnd);
            e["device_type"] = device;
            return e;
        }
    }

    enum ChainState { JustOpened, JustClicked, Closed }

    static JObject BuildRow(IdentifiedPerson person, JArray events) => new()
    {
        ["recipient"] = new JObject
        {
            ["user_id"] = person.UserId,
            ["first_name"] = person.FirstName,
            ["last_name"] = person.LastName,
            ["email"] = person.EmailAddress,
        },
        ["events"] = events,
    };

    static JObject BuildEvent(string eventType, DateTimeOffset time, IdentifiedPerson p, Campaign c, string messageId, Random rnd) => new()
    {
        ["event_id"] = RandomGuid(rnd),
        ["event_type"] = eventType,
        ["event_time"] = FormatIsoMs(time),
        ["recipient_email"] = p.EmailAddress,
        ["user_id"] = p.UserId,
        ["message_id"] = messageId,
        ["campaign"] = new JObject
        {
            ["id"] = c.Id,
            ["name"] = c.Name,
            ["brand"] = c.Brand,
            ["category"] = c.Category,
            ["subject"] = c.Subject,
            ["preheader"] = c.Preheader,
        },
    };

    static JObject BuildPurchaseEvent(IdentifiedPerson p, Campaign c, string messageId, IReadOnlyList<Product> products, Random rnd, DateTimeOffset purchaseTime)
    {
        // The clicked (featured) product plus 0-2 other distinct products.
        var extra = rnd.Next(0, 3);
        var items = new List<Product> { c.Featured };
        items.AddRange(products.Where(pr => pr.Id != c.Featured.Id).OrderBy(_ => rnd.Next()).Take(extra));

        var e = BuildEvent("purchase", purchaseTime, p, c, messageId, rnd);
        e["order"] = new JObject
        {
            ["order_id"] = $"ORD-{rnd.Next(100_000_000, 999_999_999)}",
            ["currency"] = "USD",
            ["total"] = items.Sum(i => i.Price * i.Quantity),
            ["items"] = new JArray(items.Select(i => new JObject
            {
                ["sku"] = i.Id,
                ["product_name"] = i.ProductName,
                ["category"] = i.Category,
                ["subcategory"] = i.Subcategory,
                ["unit_price"] = i.Price,
                ["quantity"] = i.Quantity,
                ["size"] = i.Size,
                ["url"] = i.Url,
            })),
        };
        return e;
    }

    // --- Helpers --------------------------------------------------------------------------------

    static string AddTrackingParams(string baseUrl, Campaign c)
    {
        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}utm_source=email&utm_medium=email&utm_campaign={c.Name}";
    }

    static string FormatIsoMs(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    static string RandomGuid(Random rnd)
    {
        var bytes = new byte[16];
        rnd.NextBytes(bytes);
        return new Guid(bytes).ToString();
    }

    static string RandomHex(Random rnd, int length)
    {
        const string chars = "0123456789abcdef";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(chars[rnd.Next(16)]);
        }
        return sb.ToString();
    }
}
