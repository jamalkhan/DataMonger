using DataMonger.Common;

namespace DataMonger.Common.Tests;

[TestClass]
public class CommandConfigTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dm_cc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "c.cfg");
        File.WriteAllText(path, json);
        return path;
    }

    [TestMethod]
    public void Load_AllowsCommentsTrailingCommasAndAnyCase()
    {
        var c = CommandConfigFile.Load<PurchaseConfig>(Write("// hi\n{ \"MINITEMS\": 2, \"maxItems\": 4, /* x */ \"orderIdPrefix\": \"X-\", }"));
        Assert.AreEqual(2, c.MinItems);
        Assert.AreEqual(4, c.MaxItems);
        Assert.AreEqual("X-", c.OrderIdPrefix);
    }

    [TestMethod]
    public void Load_EmptyObject_UsesDefaults()
    {
        var c = CommandConfigFile.Load<PurchaseConfig>(Write("{}"));
        Assert.AreEqual(1, c.MinItems);
        Assert.AreEqual(3, c.MaxItems);
        Assert.IsNull(c.ProductsFile);
    }

    [TestMethod]
    public void Load_InvalidRange_Throws()
    {
        var ex = Assert.ThrowsExactly<CommandConfigException>(() =>
            CommandConfigFile.Load<PurchaseConfig>(Write("{ \"minItems\": 3, \"maxItems\": 1 }")));
        StringAssert.Contains(ex.Message, "maxItems");
    }

    [TestMethod]
    public void Load_BadJson_Throws() =>
        Assert.ThrowsExactly<CommandConfigException>(() => CommandConfigFile.Load<EmailConversionConfig>(Write("{ nope")));

    [TestMethod]
    public void Load_EmailConfig_ReadsSeed() =>
        Assert.AreEqual(7, CommandConfigFile.Load<EmailConversionConfig>(Write("{ \"seed\": 7 }")).Seed);

    [TestMethod]
    public void ResolvePath_BlankRelativeAndAbsolute()
    {
        Assert.AreEqual("fb", CommandConfigFile.ResolvePath(" ", _dir, "fb"));
        Assert.AreEqual(Path.Combine(_dir, "p.csv"), CommandConfigFile.ResolvePath("p.csv", _dir, "fb"));
        var abs = Path.Combine(Path.GetTempPath(), "x.csv");
        Assert.AreEqual(abs, CommandConfigFile.ResolvePath(abs, _dir, "fb"));
    }
}
