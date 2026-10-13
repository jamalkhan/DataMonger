using DataMonger.Commands;
using DataMonger.Common;
using Newtonsoft.Json.Linq;

namespace DataMonger.Tests;

[TestClass]
public class EmailConversionGeneratorTests
{
    private static readonly IdentifiedPerson[] People = Enumerable.Range(1, 300)
        .Select(i => new IdentifiedPerson("First" + i, "Last" + i, $"user{i}@example.com", (1000 + i).ToString()))
        .ToArray();

    private static readonly Product[] Products =
    {
        new("P-1", "product", 59.95m, 1, "Acme Trail Jacket", "Outerwear", "Mens", "M", "https://shop.example.com/p/1"),
        new("P-2", "product", 24.00m, 2, "Acme Kids Rain Boots", "Footwear", "Kids", "12", "https://shop.example.com/p/2?color=red"),
        new("P-3", "product", 9.99m, 1, "Acme Canvas Tote", "Accessories", "Unisex", "OS", "https://shop.example.com/p/3"),
    };

    private static List<JObject> Gen(int seed) =>
        GenerateEmailConversions.Generate(People, Products, new Random(seed), GenerateEmailConversions.DefaultSeededAnchor);

    [TestMethod]
    public void SameSeed_ProducesIdenticalOutput_DifferentSeed_Differs()
    {
        var a = string.Join("\n", Gen(42).Select(r => r.ToString()));
        var b = string.Join("\n", Gen(42).Select(r => r.ToString()));
        var c = string.Join("\n", Gen(43).Select(r => r.ToString()));

        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
    }

    [TestMethod]
    public void EveryRow_StartsWithSent_HasMonotonicTimes_AndAllowedEventTypes()
    {
        var allowed = new HashSet<string> { "email_sent", "email_delivered", "email_opened", "email_clicked", "email_bounced", "purchase" };
        foreach (var row in Gen(7))
        {
            var events = ((JArray)row["events"]!).Cast<JObject>().ToList();
            Assert.AreEqual("email_sent", (string?)events[0]["event_type"]);
            var times = events.Select(e => DateTimeOffset.Parse((string)e["event_time"]!, null, System.Globalization.DateTimeStyles.AssumeUniversal)).ToList();
            for (var i = 1; i < times.Count; i++)
            {
                Assert.IsTrue(times[i] >= times[i - 1], "events must be time-ordered");
            }
            foreach (var e in events)
            {
                Assert.IsTrue(allowed.Contains((string)e["event_type"]!));
                Assert.AreEqual((string?)row["recipient"]!["email"], (string?)e["recipient_email"]);
                Assert.AreEqual((string?)row["recipient"]!["user_id"], (string?)e["user_id"]);
            }
            Assert.AreEqual(1, events.Select(e => (string?)e["message_id"]).Distinct().Count());
        }
    }

    [TestMethod]
    public void ClickedEvents_UseClickedName_AndCarryLinkUrl_BouncesCarryReason_PurchasesCarryOrder()
    {
        var all = Enumerable.Range(1, 5).SelectMany(s => Gen(s)).SelectMany(r => ((JArray)r["events"]!).Cast<JObject>()).ToList();

        var clicks = all.Where(e => (string?)e["event_type"] == "email_clicked").ToList();
        var bounces = all.Where(e => (string?)e["event_type"] == "email_bounced").ToList();
        var purchases = all.Where(e => (string?)e["event_type"] == "purchase").ToList();
        Assert.IsTrue(clicks.Count > 0 && bounces.Count > 0 && purchases.Count > 0, "seeds chosen to exercise every branch");

        Assert.IsTrue(clicks.All(e => ((string?)e["link_url"])!.StartsWith("https://shop.example.com/p/") && ((string)e["link_url"]!).Contains("utm_campaign=")));
        Assert.IsTrue(bounces.All(e => !string.IsNullOrEmpty((string?)e["bounce_reason"])));
        Assert.IsTrue(purchases.All(e => ((JArray)e["order"]!["items"]!).Count >= 1 && e["order"]!["order_id"] != null));
        Assert.IsTrue(all.All(e => e["event_time"] != null), "every event, purchases included, carries a timestamp");
    }

    [TestMethod]
    public void BouncedRows_AreTerminal_NoDeliveredOrOpenAfterBounce()
    {
        var bouncedRows = Enumerable.Range(1, 5).SelectMany(s => Gen(s))
            .Select(r => ((JArray)r["events"]!).Select(e => (string)e["event_type"]!).ToList())
            .Where(t => t.Contains("email_bounced")).ToList();

        Assert.IsTrue(bouncedRows.Count > 0);
        Assert.IsTrue(bouncedRows.All(t => t.SequenceEqual(new[] { "email_sent", "email_bounced" })));
    }

    [TestMethod]
    public void Output_ContainsNoRemovedOrVendorStyleFields()
    {
        var json = string.Join("\n", Gen(3).Select(r => r.ToString()));
        foreach (var banned in new[] { "loyalty", "test_ext_id", "subscriber" })
        {
            Assert.IsFalse(json.Contains(banned, StringComparison.OrdinalIgnoreCase), banned);
        }
    }

    [TestMethod]
    public void RunsPerSite_IsFalse()
    {
        var cmd = new GenerateEmailConversions(null!);
        Assert.IsFalse(((IConsoleCommand)cmd).RunsPerSite);
    }

    [TestMethod]
    public void Generate_NoProducts_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            GenerateEmailConversions.Generate(People, Array.Empty<Product>(), new Random(1), GenerateEmailConversions.DefaultSeededAnchor));
    }
}
