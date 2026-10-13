using DataMonger.Synth;

namespace DataMonger.Tests;

[TestClass]
public class SynthProductCatalogTests
{
    [TestMethod]
    public void Parse_ReadsRowsAndAppliesDefaults()
    {
        var c = ProductCatalog.Parse(new[]
        {
            """{"sku":"S1","name":"Thing","price":12.5}""",
        });
        Assert.AreEqual(1, c.Count);
        var v = c.RandomVariant(new Random(1));
        Assert.AreEqual("S1", v.Sku);
        Assert.AreEqual("S1", v.ProductId, "product_id defaults to sku");
        Assert.AreEqual(12.5m, v.ListPrice, "list_price defaults to price");
        Assert.AreEqual("Acme", v.Brand);
        Assert.AreEqual("", v.Color);
    }

    [TestMethod]
    public void Parse_SkipsInvalidDuplicateAndOutOfStockRows_AndCountsThem()
    {
        var c = ProductCatalog.Parse(new[]
        {
            """{"sku":"S1","name":"Ok","price":1}""",
            """{"sku":"S1","name":"Duplicate","price":2}""",
            """{"sku":"S2","name":"NoPrice"}""",
            """{"sku":"S3","price":3}""",
            """{"sku":"S4","name":"StringPrice","price":"3"}""",
            """not json""",
            """[1,2]""",
            "",
            """{"sku":"S5","name":"Gone","price":5,"in_stock":false}""",
            """{"sku":"S6","name":"Here","price":6,"in_stock":true}""",
        });
        Assert.AreEqual(2, c.Count);
        Assert.AreEqual(5, c.SkippedInvalid);
        Assert.AreEqual(1, c.SkippedDuplicate);
        Assert.AreEqual(1, c.SkippedOutOfStock);
    }

    [TestMethod]
    public void Parse_NoUsableRows_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => ProductCatalog.Parse(new[] { """{"name":"x"}""" }, "my.jsonl"));
        StringAssert.Contains(ex.Message, "my.jsonl");
    }

    [TestMethod]
    public void BrandFromUrl_HostOrFirstSegmentKids_IsAcmeKids()
    {
        Assert.AreEqual("Acme Kids", ProductCatalog.BrandFromUrl("https://kids.acme.example.com/p/1"));
        Assert.AreEqual("Acme Kids", ProductCatalog.BrandFromUrl("https://www.acme.example.com/kids/boots"));
        Assert.AreEqual("Acme", ProductCatalog.BrandFromUrl("https://www.acme.example.com/p/kids"));
        Assert.AreEqual("Acme", ProductCatalog.BrandFromUrl("https://www.acme.example.com/p/1"));
        Assert.AreEqual("Acme", ProductCatalog.BrandFromUrl(""));
        Assert.AreEqual("Acme", ProductCatalog.BrandFromUrl(null));
        Assert.AreEqual("Acme", ProductCatalog.BrandFromUrl("not a url"));
    }

    [TestMethod]
    public void Parse_ExplicitBrandWinsOverUrl()
    {
        var c = ProductCatalog.Parse(new[] { """{"sku":"S","name":"n","price":1,"brand":"Acme Kids","url":"https://www.acme.example.com/p/1"}""" });
        Assert.AreEqual("Acme Kids", c.RandomVariant(new Random(1)).Brand);
    }

    [TestMethod]
    public void RandomVariant_PicksAmongPipeSeparatedCategories_UsingTheSuppliedRandom()
    {
        var c = ProductCatalog.Parse(new[] { """{"sku":"S","name":"n","price":1,"category":"A|B|C","subcategory":"x|y"}""" });
        var seen = Enumerable.Range(0, 60).Select(i => c.RandomVariant(new Random(i)).Category).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "A", "B", "C" }, seen.ToList());

        var a = c.RandomVariant(new Random(5));
        var b = c.RandomVariant(new Random(5));
        Assert.AreEqual(a, b, "same seed must pick the same category/subcategory (no Random.Shared)");
    }

    [TestMethod]
    public void Load_ReadsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "synth_products_" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            File.WriteAllText(path, "{\"sku\":\"S1\",\"name\":\"n\",\"price\":2}\n\n{\"sku\":\"S2\",\"name\":\"m\",\"price\":3}\n");
            Assert.AreEqual(2, ProductCatalog.Load(path).Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
