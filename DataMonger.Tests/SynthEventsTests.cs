using System.Globalization;
using System.Text;
using System.Text.Json;
using DataMonger.Synth;
using static DataMonger.Tests.SynthTestSupport;

namespace DataMonger.Tests;

[TestClass]
public class SynthEventsTests
{
    private static List<JsonDocument> Parse(IEnumerable<SessionEvent> events) =>
        events.Select(e => JsonDocument.Parse(e.Json)).ToList();

    private static List<SessionEvent> Session(string profileJson, int seed, bool full = true)
    {
        using var doc = Doc(profileJson);
        return SessionEventsBuilder.BuildForProfile(doc.RootElement, SmallCatalog(), new Random(seed), Anchor, full);
    }

    // ---- reproducibility ------------------------------------------------------------------------------

    [TestMethod]
    public void SessionEvents_SameSeedAndAnchor_AreByteIdentical()
    {
        var a = Session(Profile(), 9).Select(e => Encoding.UTF8.GetString(e.Json)).ToList();
        var b = Session(Profile(), 9).Select(e => Encoding.UTF8.GetString(e.Json)).ToList();
        CollectionAssert.AreEqual(a, b);
        CollectionAssert.AreNotEqual(a, Session(Profile(), 10).Select(e => Encoding.UTF8.GetString(e.Json)).ToList());
    }

    [TestMethod]
    public void OptinEvents_SameSeedAndAnchor_AreByteIdentical()
    {
        byte[]? Build(int seed)
        {
            using var doc = Doc(Profile());
            return OptinEventBuilder.BuildForProfile(doc.RootElement, new Random(seed), Anchor);
        }
        var seed = Enumerable.Range(0, 50).First(s => Build(s) is not null);
        CollectionAssert.AreEqual(Build(seed), Build(seed));
    }

    [TestMethod]
    public void SessionEvents_AreNotAffectedByRandomShared()
    {
        // Two runs interleaved with unrelated Random.Shared draws must still match.
        var a = Session(Profile(), 4).Select(e => Encoding.UTF8.GetString(e.Json)).ToList();
        _ = Random.Shared.Next();
        var b = Session(Profile(), 4).Select(e => Encoding.UTF8.GetString(e.Json)).ToList();
        CollectionAssert.AreEqual(a, b);
    }

    // ---- session invariants ---------------------------------------------------------------------------

    [TestMethod]
    public void FullFunnelSession_StartsWithPageView_PurchaseIsLast_AndSharesSession()
    {
        var purchases = 0;
        for (var seed = 0; seed < 40; seed++)
        {
            var events = Session(Profile(), seed);
            var docs = Parse(events);
            Assert.AreEqual("page_view", docs[0].RootElement.GetProperty("event_type").GetString());
            // A purchase is only emitted when the cart is non-empty at the end (removes can empty it).
            var purchaseCount = docs.Count(d => d.RootElement.GetProperty("event_type").GetString() == "purchase");
            Assert.IsTrue(purchaseCount <= 1);
            if (purchaseCount == 1)
            {
                purchases++;
                Assert.AreEqual("purchase", docs[^1].RootElement.GetProperty("event_type").GetString());
            }
            Assert.AreEqual(1, docs.Select(d => d.RootElement.GetProperty("session_id").GetString()).Distinct().Count());
            Assert.AreEqual(1, docs.Select(d => d.RootElement.GetProperty("channel").GetString()).Distinct().Count());
            CollectionAssert.IsSubsetOf(new[] { docs[0].RootElement.GetProperty("channel").GetString() }, EventSession.Channels.ToList());

            var times = events.Select(e => e.Timestamp).ToList();
            CollectionAssert.AreEqual(times.OrderBy(t => t).ToList(), times);
            Assert.IsTrue((times[^1] - times[0]).TotalMinutes <= EventSession.WindowMinutes);
            Assert.IsTrue(times[^1] <= Anchor, "events must not be in the future of the anchor");
            Assert.IsTrue(times[0] > Anchor.AddDays(-16));
            foreach (var d in docs) d.Dispose();
        }
        Assert.IsTrue(purchases > 25, $"forced funnels should nearly always purchase; got {purchases}/40");
    }

    [TestMethod]
    public void CartItems_WereViewedEarlier_AndRemovesOnlyHappenWithItems()
    {
        for (var seed = 0; seed < 40; seed++)
        {
            var viewed = new HashSet<string>();
            foreach (var e in Session(Profile(), seed))
            {
                using var d = JsonDocument.Parse(e.Json);
                var r = d.RootElement;
                switch (r.GetProperty("event_type").GetString())
                {
                    case "page_view":
                        viewed.Add(r.GetProperty("product").GetProperty("sku").GetString()!);
                        break;
                    case "add_to_cart":
                        Assert.IsTrue(r.GetProperty("items").GetArrayLength() > 0);
                        foreach (var item in r.GetProperty("items").EnumerateArray())
                        {
                            Assert.IsTrue(viewed.Contains(item.GetProperty("sku").GetString()!));
                        }
                        break;
                    case "purchase":
                        var sum = 0m;
                        foreach (var item in r.GetProperty("items").EnumerateArray())
                        {
                            Assert.IsTrue(viewed.Contains(item.GetProperty("sku").GetString()!));
                            Assert.AreEqual(item.GetProperty("unit_price").GetDecimal() * item.GetProperty("quantity").GetInt32(),
                                item.GetProperty("line_total").GetDecimal());
                            sum += item.GetProperty("line_total").GetDecimal();
                        }
                        var o = r.GetProperty("order");
                        Assert.AreEqual(sum, o.GetProperty("subtotal").GetDecimal());
                        Assert.AreEqual(
                            Math.Round(sum - o.GetProperty("discount").GetDecimal() + o.GetProperty("shipping").GetDecimal() + o.GetProperty("tax").GetDecimal(), 2),
                            o.GetProperty("total").GetDecimal());
                        StringAssert.StartsWith(o.GetProperty("order_id").GetString(), "ORD-");
                        CollectionAssert.Contains(SessionEventsBuilder.PaymentMethods.ToList(), o.GetProperty("payment_method").GetString());
                        break;
                }
            }
        }
    }

    [TestMethod]
    public void NonForcedSession_MayBeEmpty_AndNeverHasPurchaseBeforeLastEvent()
    {
        var empties = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var events = Session(Profile(), seed, full: false);
            if (events.Count == 0) { empties++; continue; }
            Assert.AreEqual("page_view", events[0].EventType);
            Assert.IsFalse(events.Take(events.Count - 1).Any(e => e.EventType == "purchase"));
        }
        Assert.IsTrue(empties > 0);
    }

    [TestMethod]
    public void ProfileWithoutEmail_ProducesNoSession()
    {
        var phoneOnly = """{"user_id":"CUST_9","identifiers":[{"type":"phone","value":"+12125550101"}]}""";
        Assert.AreEqual(0, Session(phoneOnly, 1).Count);
    }

    [TestMethod]
    public void ProfileWithoutUserId_StillWorks_WithNullUserId()
    {
        var events = Session(Profile(userId: null), 1);
        Assert.IsTrue(events.Count > 0);
        using var d = JsonDocument.Parse(events[0].Json);
        Assert.AreEqual(JsonValueKind.Null, d.RootElement.GetProperty("user_id").ValueKind);
    }

    // ---- timezone handling ----------------------------------------------------------------------------

    [TestMethod]
    [DataRow("Asia/Tokyo", "+09:00")]
    [DataRow("Asia/Kolkata", "+05:30")]
    [DataRow("UTC", "+00:00")]
    public void SessionEvents_UseTheProfilesTimeZoneOffset(string tz, string offset)
    {
        foreach (var e in Session(Profile(timezone: tz), 3))
        {
            using var d = JsonDocument.Parse(e.Json);
            StringAssert.EndsWith(d.RootElement.GetProperty("event_time").GetString(), offset);
        }
    }

    [TestMethod]
    public void SessionEvents_MissingOrUnknownTimeZone_FallBackToEastern()
    {
        foreach (var tz in new string?[] { null, "Not/AZone", "  " })
        {
            var e = Session(Profile(timezone: tz), 3)[0];
            using var d = JsonDocument.Parse(e.Json);
            var t = d.RootElement.GetProperty("event_time").GetString()!;
            Assert.IsTrue(t.EndsWith("-04:00") || t.EndsWith("-05:00"), t); // EDT in Sept/Oct, EST otherwise
        }
    }

    [TestMethod]
    [DataRow("Asia/Tokyo", "+09:00")]
    [DataRow("Asia/Kolkata", "+05:30")]
    public void OptinEvents_UseTheProfilesTimeZoneOffset(string tz, string offset)
    {
        var rolled = 0;
        for (var seed = 0; seed < 30; seed++)
        {
            using var doc = Doc(Profile(timezone: tz));
            var bytes = OptinEventBuilder.BuildForProfile(doc.RootElement, new Random(seed), Anchor);
            if (bytes is null) continue;
            rolled++;
            using var d = JsonDocument.Parse(bytes);
            StringAssert.EndsWith(d.RootElement.GetProperty("event_time").GetString(), offset);
            StringAssert.EndsWith(d.RootElement.GetProperty("consent").GetProperty("subscribed_at").GetString(), offset);
        }
        Assert.IsTrue(rolled > 5);
    }

    [TestMethod]
    public void TimezoneIsReadFromTheTopLevel_AndIgnoresALegacyWrapper()
    {
        using var flat = Doc("""{"timezone":"Asia/Tokyo"}""");
        Assert.AreEqual("Asia/Tokyo", ProfileFields.ResolveTimezone(flat.RootElement));
        using var none = Doc("""{"properties":{"timezone":"Asia/Tokyo"}}""");
        Assert.IsNull(ProfileFields.ResolveTimezone(none.RootElement));
    }

    // ---- opt-in specifics -----------------------------------------------------------------------------

    [TestMethod]
    public void Optin_RespectsConsentChannels_AndNeverFiresWithoutConsent()
    {
        var consentOnlySms = Profile(consent: """{"email":false,"sms":true,"push":false,"postal":false}""");
        var noConsent = Profile(consent: """{"email":false,"sms":false,"push":false,"postal":false}""");
        var phoneProfile = """{"user_id":"U","timezone":"UTC","identifiers":[{"type":"email","value":"a@example.com"},{"type":"phone","value":"+12125550123"}],"consent":{"sms":true}}""";
        for (var seed = 0; seed < 60; seed++)
        {
            using var a = Doc(consentOnlySms);
            if (OptinEventBuilder.BuildForProfile(a.RootElement, new Random(seed), Anchor) is { } bytes)
            {
                using var d = JsonDocument.Parse(bytes);
                Assert.AreEqual("sms", d.RootElement.GetProperty("consent").GetProperty("channel").GetString());
            }
            using var b = Doc(noConsent);
            Assert.IsNull(OptinEventBuilder.BuildForProfile(b.RootElement, new Random(seed), Anchor));
            using var c = Doc(phoneProfile);
            if (OptinEventBuilder.BuildForProfile(c.RootElement, new Random(seed), Anchor) is { } sms)
            {
                using var d = JsonDocument.Parse(sms);
                Assert.AreEqual("+12125550123", d.RootElement.GetProperty("consent").GetProperty("contact_value").GetString());
            }
        }
    }

    [TestMethod]
    public void Optin_ContactValue_FollowsTheChannel()
    {
        using var p = Doc("""{"user_id":"U1","identifiers":[{"type":"email","value":"a@example.com"},{"type":"phone","value":"+12125550123"}],"addresses":[{"type":"shipping","line1":"1 Elm St","city":"X","region":"OR","postal_code":"1"},{"type":"billing","line1":"2 Oak Ave","city":"Portland","region":"OR","postal_code":"97201"}]}""");
        Assert.AreEqual("a@example.com", OptinEventBuilder.ContactValueFor("email", p.RootElement, "U1", "a@example.com"));
        Assert.AreEqual("+12125550123", OptinEventBuilder.ContactValueFor("sms", p.RootElement, "U1", "a@example.com"));
        Assert.AreEqual("2 Oak Ave, Portland, OR 97201", OptinEventBuilder.ContactValueFor("postal", p.RootElement, "U1", "a@example.com"));
        Assert.AreEqual("U1", OptinEventBuilder.ContactValueFor("push", p.RootElement, "U1", "a@example.com"));
        using var bare = Doc("""{"user_id":"U1"}""");
        Assert.AreEqual("a@example.com", OptinEventBuilder.ContactValueFor("sms", bare.RootElement, "U1", "a@example.com"));
        Assert.AreEqual("a@example.com", OptinEventBuilder.ContactValueFor("postal", bare.RootElement, "U1", "a@example.com"));
    }

    [TestMethod]
    public void Optin_IsNeverBeforeCreatedAt_NeverInTheFuture_AndRollsAboutSeventyPercent()
    {
        var created = Anchor.AddDays(-30);
        var profile = Profile().Replace("\"identifiers\"", $"\"created_at\":\"{created:yyyy-MM-ddTHH:mm:ssZ}\",\"identifiers\"");
        var rolled = 0;
        const int n = 400;
        for (var seed = 0; seed < n; seed++)
        {
            using var doc = Doc(profile);
            var bytes = OptinEventBuilder.BuildForProfile(doc.RootElement, new Random(seed), Anchor);
            if (bytes is null) continue;
            rolled++;
            using var d = JsonDocument.Parse(bytes);
            var t = DateTimeOffset.Parse(d.RootElement.GetProperty("event_time").GetString()!, CultureInfo.InvariantCulture);
            Assert.IsTrue(t <= Anchor && t >= created.AddDays(-1), t.ToString("o"));
        }
        Assert.IsTrue(rolled > n * 0.6 && rolled < n * 0.8, $"rolled {rolled}/{n}");
    }

    [TestMethod]
    public void Optin_ProfileWithoutUserIdOrEmail_Skips()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            using var noId = Doc(Profile(userId: null));
            Assert.IsNull(OptinEventBuilder.BuildForProfile(noId.RootElement, new Random(seed), Anchor));
            using var noEmail = Doc("""{"user_id":"U"}""");
            Assert.IsNull(OptinEventBuilder.BuildForProfile(noEmail.RootElement, new Random(seed), Anchor));
        }
    }

    // ---- cross-command compatibility ------------------------------------------------------------------

    [TestMethod]
    public void GeneratedProfiles_FeedBothEventCommands()
    {
        var config = LoadExample(21);
        var catalog = SmallCatalog();
        var rng = new Random(21);
        var sessionEvents = 0;
        var optins = 0;
        foreach (var json in GenerateProfiles(config, 30))
        {
            using var doc = Doc(json);
            var p = doc.RootElement;
            var email = ProfileFields.ResolveEmail(p);
            Assert.IsNotNull(email);

            foreach (var e in SessionEventsBuilder.BuildForProfile(p, catalog, rng, Anchor))
            {
                using var d = JsonDocument.Parse(e.Json);
                Assert.AreEqual(email, d.RootElement.GetProperty("email").GetString());
                Assert.AreEqual(p.GetProperty("user_id").GetString(), d.RootElement.GetProperty("user_id").GetString());
                Assert.AreEqual(p.GetProperty("memberships")[0].GetProperty("storefront_id").GetString(), d.RootElement.GetProperty("storefront_id").GetString());
                Assert.AreEqual(p.GetProperty("locale").GetString(), d.RootElement.GetProperty("locale").GetString());
                Assert.AreEqual(p.GetProperty("country").GetString() == "CA" ? "CAD" : "USD", d.RootElement.GetProperty("currency").GetString());
                sessionEvents++;
            }

            if (OptinEventBuilder.BuildForProfile(p, rng, Anchor) is { } bytes)
            {
                using var d = JsonDocument.Parse(bytes);
                Assert.AreEqual("marketing_opt_in", d.RootElement.GetProperty("event_type").GetString());
                var channel = d.RootElement.GetProperty("consent").GetProperty("channel").GetString()!;
                Assert.IsTrue(p.GetProperty("consent").GetProperty(channel).GetBoolean(), "opt-in channel must be one the profile consents to");
                optins++;
            }
        }
        Assert.IsTrue(sessionEvents > 30 && optins > 5);
    }

    [TestMethod]
    public void EveryEmittedLine_IsSingleLineJsonObject()
    {
        foreach (var json in GenerateProfiles(LoadExample(2), 5))
        {
            Assert.IsFalse(json.Contains('\n'));
            using var d = Doc(json);
            Assert.AreEqual(JsonValueKind.Object, d.RootElement.ValueKind);
        }
        foreach (var e in Session(Profile(), 2))
        {
            Assert.IsFalse(Encoding.UTF8.GetString(e.Json).Contains('\n'));
        }
    }

    [TestMethod]
    public void NonObjectProfile_IsIgnored()
    {
        using var doc = Doc("[1,2,3]");
        Assert.AreEqual(0, SessionEventsBuilder.BuildForProfile(doc.RootElement, SmallCatalog(), new Random(1), Anchor).Count);
        Assert.IsNull(OptinEventBuilder.BuildForProfile(doc.RootElement, new Random(1), Anchor));
    }

    [TestMethod]
    public void NamesUsedInOutputAreNeutral()
    {
        // guard against client/vendor-derived names creeping back into emitted JSON keys or constants
        var banned = new[] { "loyalty", "scoped", "sub_site", "rf_tier", "unified", "omnifeed", "cc_id", "nudge", "plcc", "cbcc" };
        var text = string.Join("\n", GenerateProfiles(LoadExample(1), 5))
            + string.Join("\n", Session(Profile(), 1).Select(e => Encoding.UTF8.GetString(e.Json)));
        foreach (var b in banned)
        {
            Assert.IsFalse(text.Contains(b, StringComparison.OrdinalIgnoreCase), b);
        }
    }
}
