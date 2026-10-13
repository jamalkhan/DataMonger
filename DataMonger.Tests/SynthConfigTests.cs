using DataMonger.Synth;

namespace DataMonger.Tests;

[TestClass]
public class SynthConfigTests
{
    [TestMethod]
    public void ExampleConfig_LoadsAndValidates()
    {
        var c = SynthTestSupport.LoadExample();
        Assert.AreEqual(2, c.Brands.Count);
        Assert.AreEqual("acme-us", c.Brands[0].StorefrontId);
        Assert.AreEqual(42, c.Seed);
        Assert.AreEqual("2026-10-01", c.AnchorDate);
        Assert.AreEqual(0, c.Validate().Count);
    }

    [TestMethod]
    public void Picklists_AreCaseInsensitive_AfterJsonDeserialization()
    {
        var c = SynthTestSupport.LoadExample();
        Assert.IsTrue(c.Picklists.TryGetValue("GENDER", out var upper));
        Assert.IsTrue(c.Picklists.TryGetValue("Gender", out var mixed));
        CollectionAssert.AreEqual(upper, mixed);
        Assert.IsTrue(c.Picklists.ContainsKey("membershiptier"));
    }

    [TestMethod]
    public void Picklists_UppercaseKeysInFile_StillSatisfyTheGenerator()
    {
        var json = File.ReadAllText(SynthTestSupport.FindExampleConfig())
            .Replace("\"gender\":", "\"GENDER\":")
            .Replace("\"membershipTier\":", "\"MEMBERSHIPTIER\":");
        var c = SynthConfigLoader.FromJson(json);
        var profiles = SynthTestSupport.GenerateProfiles(c, 3);
        Assert.AreEqual(3, profiles.Count);
    }

    [TestMethod]
    public void Picklists_SetterReplacesComparerEvenWhenAssignedACaseSensitiveDictionary()
    {
        var c = new SynthConfig
        {
            Picklists = new Dictionary<string, List<string>> { ["Tier"] = new() { "Gold" } },
        };
        Assert.IsTrue(c.Picklists.ContainsKey("tier"));
    }

    [TestMethod]
    public void FromJson_AllowsCommentsAndTrailingCommas_AndCaseInsensitiveKeys()
    {
        var json = File.ReadAllText(SynthTestSupport.FindExampleConfig()).Replace("\"recordCount\"", "\"RECORDCOUNT\"");
        Assert.AreEqual(12, SynthConfigLoader.FromJson(json).RecordCount);
    }

    private static SynthConfigException Invalid(Action<SynthConfig> mutate)
    {
        var c = SynthTestSupport.LoadExample();
        mutate(c);
        var errors = c.Validate();
        Assert.IsTrue(errors.Count > 0, "expected validation errors");
        return new SynthConfigException(errors);
    }

    [TestMethod]
    public void Validate_NoBrands() =>
        StringAssert.Contains(string.Join("|", Invalid(c => c.Brands.Clear()).Errors), "brands must contain");

    [TestMethod]
    public void Validate_SplitEveryZero() =>
        StringAssert.Contains(string.Join("|", Invalid(c => c.SplitEvery = 0).Errors), "splitEvery");

    [TestMethod]
    public void Validate_RecordCountZero() =>
        StringAssert.Contains(string.Join("|", Invalid(c => c.RecordCount = 0).Errors), "recordCount");

    [TestMethod]
    public void Validate_DuplicateBrandCodeAndPrefix()
    {
        var text = string.Join("|", Invalid(c =>
        {
            c.Brands[1].Code = "ac";
            c.Brands[1].ContactPrefix = "AC";
        }).Errors);
        StringAssert.Contains(text, "brand code '");
        StringAssert.Contains(text, "contactPrefix");
    }

    [TestMethod]
    public void Validate_BlankBrandFields() =>
        StringAssert.Contains(string.Join("|", Invalid(c => c.Brands[0].StorefrontId = " ").Errors), "brands[0].storefrontId");

    [TestMethod]
    public void Validate_ProbabilityOutOfRange() =>
        StringAssert.Contains(string.Join("|", Invalid(c => c.Probabilities.EmailConsent = 1.5).Errors), "probabilities.EmailConsent");

    [TestMethod]
    public void Validate_MissingAndEmptyPicklists()
    {
        var text = string.Join("|", Invalid(c =>
        {
            c.Picklists.Remove("gender");
            c.Picklists["timezone"] = new List<string>();
        }).Errors);
        StringAssert.Contains(text, "picklist 'gender' is missing or empty");
        StringAssert.Contains(text, "picklist 'timezone' is missing or empty");
    }

    [TestMethod]
    public void Validate_UnknownTier_And_BadCityRegionPostal()
    {
        var text = string.Join("|", Invalid(c =>
        {
            c.Picklists["membershipTier"] = new List<string> { "Platinum" };
            c.Picklists["cityRegionPostal"] = new List<string> { "Nowhere|ZZ" };
        }).Errors);
        StringAssert.Contains(text, "'Platinum'");
        StringAssert.Contains(text, "City|Region|PostalCode");
    }

    [TestMethod]
    public void Validate_BadAnchorDate() =>
        StringAssert.Contains(string.Join("|", Invalid(c => c.AnchorDate = "10/01/2026").Errors), "anchorDate");

    [TestMethod]
    public void Validate_EmptyNamePools() =>
        StringAssert.Contains(string.Join("|", Invalid(c => c.FirstNames.Clear()).Errors), "firstNames");

    [TestMethod]
    public void FromJson_MalformedJson_ThrowsSynthConfigException()
    {
        var ex = Assert.ThrowsExactly<SynthConfigException>(() => SynthConfigLoader.FromJson("{ not json"));
        StringAssert.Contains(ex.Errors[0], "Could not parse JSON");
    }

    [TestMethod]
    public void FromJson_InvalidConfig_ListsEveryProblem()
    {
        var ex = Assert.ThrowsExactly<SynthConfigException>(() => SynthConfigLoader.FromJson("{}"));
        Assert.IsTrue(ex.Errors.Count >= 5, "an empty config should report brands, names and every picklist");
    }

    [TestMethod]
    public void ResolveAnchor_UsesConfiguredDate_OrFallback()
    {
        var c = SynthTestSupport.LoadExample();
        Assert.AreEqual(SynthTestSupport.Anchor, c.ResolveAnchor(DateTimeOffset.UtcNow));
        c.AnchorDate = null;
        var fallback = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Assert.AreEqual(fallback, c.ResolveAnchor(fallback));
    }

    [TestMethod]
    public void ParseAnchor_IsCultureIndependent()
    {
        using var _ = SynthTestSupport.Culture("fr-FR");
        Assert.AreEqual(SynthTestSupport.Anchor, SynthConfig.ParseAnchor("2026-10-01"));
        Assert.IsNull(SynthConfig.ParseAnchor("01/10/2026"));
        Assert.IsNull(SynthConfig.ParseAnchor(null));
    }
}
