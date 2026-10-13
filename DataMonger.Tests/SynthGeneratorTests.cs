using System.Globalization;
using System.Text.Json;
using DataMonger.Synth;
using static DataMonger.Tests.SynthTestSupport;

namespace DataMonger.Tests;

[TestClass]
public class SynthGeneratorTests
{
    [TestMethod]
    public void SameSeedAndAnchor_ProduceIdenticalProfiles()
    {
        var a = GenerateProfiles(LoadExample(42), 15);
        var b = GenerateProfiles(LoadExample(42), 15);
        CollectionAssert.AreEqual(a, b);
    }

    [TestMethod]
    public void DifferentSeed_ProducesDifferentProfiles()
    {
        CollectionAssert.AreNotEqual(GenerateProfiles(LoadExample(1), 5), GenerateProfiles(LoadExample(2), 5));
    }

    [TestMethod]
    public void DifferentAnchor_ShiftsDatesButNotThePicks()
    {
        var c = LoadExample(42);
        var a = GenerateProfiles(c, 3, Anchor);
        var b = GenerateProfiles(c, 3, Anchor.AddDays(100));
        CollectionAssert.AreNotEqual(a, b);
        using var da = Doc(a[0]);
        using var db = Doc(b[0]);
        Assert.AreEqual(da.RootElement.GetProperty("first_name").GetString(), db.RootElement.GetProperty("first_name").GetString());
        Assert.AreEqual(da.RootElement.GetProperty("gender").GetString(), db.RootElement.GetProperty("gender").GetString());
    }

    [TestMethod]
    public void Output_IsIndependentOfCurrentCulture()
    {
        var invariant = GenerateProfiles(LoadExample(7), 8);
        foreach (var culture in new[] { "fr-FR", "tr-TR", "ar-SA", "th-TH" })
        {
            using var _ = Culture(culture);
            CollectionAssert.AreEqual(invariant, GenerateProfiles(LoadExample(7), 8), culture);
        }
    }

    [TestMethod]
    public void Profile_HasDocumentedShapeAndConsistentValues()
    {
        var config = LoadExample(11);
        foreach (var json in GenerateProfiles(config, 40))
        {
            using var doc = Doc(json);
            var p = doc.RootElement;
            var first = p.GetProperty("first_name").GetString()!;
            var last = p.GetProperty("last_name").GetString()!;
            Assert.AreEqual($"{first} {last}", p.GetProperty("full_name").GetString());

            var email = ProfileFields.ResolveEmail(p)!;
            StringAssert.Contains(email, first.ToLowerInvariant() + "." + last.ToLowerInvariant());
            StringAssert.EndsWith(email, "@" + last.ToLowerInvariant() + ".example.com");
            Assert.AreEqual(email, p.GetProperty("contact").GetProperty("email").GetString());
            StringAssert.StartsWith(ProfileFields.ResolvePhone(p)!, "+1212555");

            var created = DateTimeOffset.Parse(p.GetProperty("created_at").GetString()!, CultureInfo.InvariantCulture);
            Assert.IsTrue(created <= Anchor);
            Assert.AreEqual(UserPropsSynthesizer.CountryOf(p.GetProperty("locale").GetString()!), p.GetProperty("country").GetString());

            // order summary counters are consistent
            var os = p.GetProperty("order_summary");
            var count = os.GetProperty("order_count").GetInt32();
            Assert.IsTrue(os.GetProperty("returns_count").GetInt32() <= count);
            if (count == 0)
            {
                Assert.AreEqual(JsonValueKind.Null, os.GetProperty("first_order_at").ValueKind);
                Assert.AreEqual(0m, os.GetProperty("lifetime_spend").GetDecimal());
            }
            else
            {
                var firstOrder = DateTimeOffset.Parse(os.GetProperty("first_order_at").GetString()!, CultureInfo.InvariantCulture);
                var lastOrder = DateTimeOffset.Parse(os.GetProperty("last_order_at").GetString()!, CultureInfo.InvariantCulture);
                Assert.IsTrue(created <= firstOrder && firstOrder <= lastOrder && lastOrder <= Anchor);
                var spend = os.GetProperty("lifetime_spend").GetDecimal();
                var aov = os.GetProperty("average_order_value").GetDecimal();
                Assert.AreEqual(Math.Round(aov * count, 2), spend);
            }

            // one membership per brand; tier/points agree; flat contact mirrors them
            var memberships = p.GetProperty("memberships").EnumerateArray().ToList();
            Assert.AreEqual(config.Brands.Count, memberships.Count);
            for (var i = 0; i < memberships.Count; i++)
            {
                var m = memberships[i];
                var brand = config.Brands[i];
                Assert.AreEqual(brand.Code, m.GetProperty("brand_code").GetString());
                Assert.AreEqual(brand.StorefrontId, m.GetProperty("storefront_id").GetString());
                var tier = m.GetProperty("tier").GetString()!;
                var (min, max) = MembershipTiers.PointsRange(tier);
                var points = m.GetProperty("points").GetInt32();
                Assert.IsTrue(points >= min && points < max, $"{tier} {points}");
                var prev = m.GetProperty("previous_tier");
                if (prev.ValueKind == JsonValueKind.String)
                {
                    Assert.AreNotEqual(tier, prev.GetString());
                }
                var joined = DateTime.ParseExact(m.GetProperty("joined_at").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var since = DateTime.ParseExact(m.GetProperty("tier_since").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                Assert.IsTrue(joined <= since && since <= Anchor.UtcDateTime);

                var contact = p.GetProperty("contact");
                Assert.AreEqual(tier, contact.GetProperty(brand.ContactPrefix + "_tier").GetString());
                Assert.AreEqual(points, contact.GetProperty(brand.ContactPrefix + "_points").GetInt32());
            }

            // postal codes stay paired with their city
            foreach (var a in p.GetProperty("addresses").EnumerateArray())
            {
                var triple = $"{a.GetProperty("city").GetString()}|{a.GetProperty("region").GetString()}|{a.GetProperty("postal_code").GetString()}";
                CollectionAssert.Contains(config.Picklists["cityRegionPostal"], triple);
            }
        }
    }

    [TestMethod]
    public void ProbabilityZeroAndOne_AreHonoured()
    {
        var c = LoadExample(3);
        c.Probabilities.EmailConsent = 1;
        c.Probabilities.SmsConsent = 0;
        c.Probabilities.HasOrders = 0;
        c.Probabilities.SecondAddress = 1;
        foreach (var json in GenerateProfiles(c, 20))
        {
            using var doc = Doc(json);
            Assert.IsTrue(doc.RootElement.GetProperty("consent").GetProperty("email").GetBoolean());
            Assert.IsFalse(doc.RootElement.GetProperty("consent").GetProperty("sms").GetBoolean());
            Assert.AreEqual(0, doc.RootElement.GetProperty("order_summary").GetProperty("order_count").GetInt32());
            Assert.AreEqual(2, doc.RootElement.GetProperty("addresses").GetArrayLength());
        }
    }

    [TestMethod]
    public void EmailsAreUniqueAcrossRecords()
    {
        var emails = GenerateProfiles(LoadExample(5), 200).Select(j =>
        {
            using var d = Doc(j);
            return ProfileFields.ResolveEmail(d.RootElement)!;
        }).ToList();
        Assert.AreEqual(emails.Count, emails.Distinct().Count());
    }
}
