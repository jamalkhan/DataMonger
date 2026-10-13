namespace DataMonger.Common.Tests;

[TestClass]
public class CsvLoaderTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "csvloader_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [TestMethod]
    public void ReadIdentified_ParsesFourColumns_AndDiscardsHeader()
    {
        var path = Write("identified.csv", "first_name,last_name,email,user_id\nAda,Lovelace,ada@example.com,1001\nAlan,Turing,alan@example.com,1002\n");

        var people = CsvLoader.ReadIdentified(path).ToList();

        Assert.AreEqual(2, people.Count);
        Assert.AreEqual("Ada", people[0].FirstName);
        Assert.AreEqual("Lovelace", people[0].LastName);
        Assert.AreEqual("ada@example.com", people[0].EmailAddress);
        Assert.AreEqual("1001", people[0].UserId);
        Assert.AreEqual("1002", people[1].UserId);
    }

    [TestMethod]
    public void ReadIdentified_IgnoresExtraColumns()
    {
        var path = Write("identified.csv", "h\nAda,Lovelace,ada@example.com,1001,extra,more\n");

        Assert.AreEqual("1001", CsvLoader.ReadIdentified(path).Single().UserId);
    }

    [TestMethod]
    public void Readers_SkipBlankAndWhitespaceOnlyLines()
    {
        var path = Write("identified.csv", "h\n\nAda,Lovelace,ada@example.com,1001\n   \n\nAlan,Turing,alan@example.com,1002\n\n\n");

        Assert.AreEqual(2, CsvLoader.ReadIdentified(path).Count());
    }

    [TestMethod]
    public void ReadIdentified_ShortRow_ThrowsWithFileAndPhysicalLineNumber()
    {
        var path = Write("identified.csv", "h\nAda,Lovelace,ada@example.com,1001\n\nAlan,Turing,alan@example.com\n");

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => CsvLoader.ReadIdentified(path).ToList());

        StringAssert.Contains(ex.Message, path);
        StringAssert.Contains(ex.Message, "line 4");
        StringAssert.Contains(ex.Message, "at least 4");
        StringAssert.Contains(ex.Message, "found 3");
    }

    [TestMethod]
    public void ReadPeople_ParsesThreeColumns_AndShortRowThrows()
    {
        var ok = Write("anonymous.csv", "h\nCharles,Babbage,charles@example.com\n");
        Assert.AreEqual("charles@example.com", CsvLoader.ReadPeople(ok).Single().EmailAddress);

        var bad = Write("bad.csv", "h\nCharles,Babbage\n");
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => CsvLoader.ReadPeople(bad).ToList());
        StringAssert.Contains(ex.Message, "line 2");
    }

    [TestMethod]
    public void ReadProducts_ParsesNineColumns_LastColumnMayContainCommas()
    {
        var path = Write("products.csv", "h\nP-1,product,59.95,2,Trail Jacket,Outerwear,Mens,M,https://shop.example.com/p/1?a=1,2\n");

        var p = CsvLoader.ReadProducts(path).Single();

        Assert.AreEqual("P-1", p.Id);
        Assert.AreEqual(59.95m, p.Price);
        Assert.AreEqual(2, p.Quantity);
        Assert.AreEqual("https://shop.example.com/p/1?a=1,2", p.Url);
    }

    [TestMethod]
    public void ReadProducts_SkipsBlankLines_ShortRowAndBadNumberReportFileAndLine()
    {
        var blank = Write("blank.csv", "h\n\nP-1,product,1.50,1,Tee,Tops,Mens,M,https://x\n\n");
        Assert.AreEqual(1, CsvLoader.ReadProducts(blank).Count());

        var shortRow = Write("short.csv", "h\nP-1,product,1.50\n");
        var ex1 = Assert.ThrowsExactly<InvalidDataException>(() => CsvLoader.ReadProducts(shortRow).ToList());
        StringAssert.Contains(ex1.Message, shortRow);
        StringAssert.Contains(ex1.Message, "line 2");

        var badNumber = Write("badnum.csv", "h\nP-1,product,1.50,1,Tee,Tops,Mens,M,https://x\nP-2,product,abc,1,Tee,Tops,Mens,M,https://x\n");
        var ex2 = Assert.ThrowsExactly<InvalidDataException>(() => CsvLoader.ReadProducts(badNumber).ToList());
        StringAssert.Contains(ex2.Message, "line 3");
        StringAssert.Contains(ex2.Message, "price");
    }
}
