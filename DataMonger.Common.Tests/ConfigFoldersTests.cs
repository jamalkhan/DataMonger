using DataMonger.Common;

namespace DataMonger.Common.Tests;

[TestClass]
public class ConfigFoldersTests
{
    private string _home = "";
    private string? _oldHome;

    [TestInitialize]
    public void Setup()
    {
        _home = Path.Combine(Path.GetTempPath(), "dm_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        _oldHome = Environment.GetEnvironmentVariable("DATAMONGER_HOME");
        Environment.SetEnvironmentVariable("DATAMONGER_HOME", _home);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("DATAMONGER_HOME", _oldHome);
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private static ConfigFolders With(string answers, out StringWriter output)
    {
        output = new StringWriter();
        return new ConfigFolders(new StringReader(answers), output);
    }

    [TestMethod]
    public void DefaultFolder_IsUnderHomeConfigKindFolder()
    {
        Assert.AreEqual(Path.Combine(_home, "Config", "SynthConfig"), ConfigFolders.DefaultFolder(ConfigKind.Synth));
        Assert.AreEqual(Path.Combine(_home, "Config", "EtlConfig"), ConfigFolders.DefaultFolder(ConfigKind.Etl));
        Assert.AreEqual(Path.Combine(_home, "Config", "AnonConfig"), ConfigFolders.DefaultFolder(ConfigKind.Anon));
    }

    [TestMethod]
    public void FirstRun_AcceptingTheDefault_CreatesAndSavesTheFolder()
    {
        var folders = With("\n", out var output);

        var dir = folders.Resolve(ConfigKind.Etl);

        Assert.AreEqual(ConfigFolders.DefaultFolder(ConfigKind.Etl), dir);
        Assert.IsTrue(Directory.Exists(dir));
        Assert.AreEqual(dir, ConfigFolders.SavedFolder(ConfigKind.Etl));
        StringAssert.Contains(output.ToString(), "First run");
    }

    [TestMethod]
    public void FirstRun_CustomPath_IsUsedAndSaved()
    {
        var custom = Path.Combine(_home, "mine");

        var dir = With(custom + "\n", out _).Resolve(ConfigKind.Synth);

        Assert.AreEqual(custom, dir);
        Assert.IsTrue(Directory.Exists(custom));
    }

    [TestMethod]
    public void SecondCall_UsesTheSavedFolder_WithoutPrompting()
    {
        var custom = Path.Combine(_home, "mine");
        With(custom + "\n", out _).Resolve(ConfigKind.Anon);

        var second = With("", out var output);
        Assert.AreEqual(custom, second.Resolve(ConfigKind.Anon));
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void KindsAreSavedIndependently()
    {
        var a = Path.Combine(_home, "a");
        var b = Path.Combine(_home, "b");
        With(a + "\n", out _).Resolve(ConfigKind.Etl);
        With(b + "\n", out _).Resolve(ConfigKind.Synth);

        Assert.AreEqual(a, ConfigFolders.SavedFolder(ConfigKind.Etl));
        Assert.AreEqual(b, ConfigFolders.SavedFolder(ConfigKind.Synth));
        Assert.IsNull(ConfigFolders.SavedFolder(ConfigKind.Anon));
    }

    [TestMethod]
    public void SavedFolderDeleted_PromptsAgain()
    {
        var custom = Path.Combine(_home, "gone");
        With(custom + "\n", out _).Resolve(ConfigKind.Etl);
        Directory.Delete(custom);

        var dir = With("\n", out var output).Resolve(ConfigKind.Etl);

        Assert.AreEqual(custom, dir); // default offered is the previously saved folder
        Assert.IsTrue(Directory.Exists(custom));
        StringAssert.Contains(output.ToString(), "Choose the folder");
    }

    [TestMethod]
    public void PromptForConfig_PicksByNumber_AndPathAndCancel()
    {
        var dir = Path.Combine(_home, "etl");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "b.etlconfig"), "{}");
        File.WriteAllText(Path.Combine(dir, "a.etlconfig"), "{}");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
        With(dir + "\n", out _).Resolve(ConfigKind.Etl);

        Assert.AreEqual(Path.Combine(dir, "a.etlconfig"), With("1\n", out _).PromptForConfig(ConfigKind.Etl));
        Assert.AreEqual(Path.Combine(dir, "b.etlconfig"), With("2\n", out _).PromptForConfig(ConfigKind.Etl));
        Assert.IsNull(With("q\n", out _).PromptForConfig(ConfigKind.Etl));
        Assert.IsNull(With("", out _).PromptForConfig(ConfigKind.Etl)); // EOF cancels
        Assert.AreEqual(Path.Combine(dir, "a.etlconfig"),
            With("9\n" + Path.Combine(dir, "a.etlconfig") + "\n", out var output).PromptForConfig(ConfigKind.Etl));
        StringAssert.Contains(output.ToString(), "[warn]");
    }

    [TestMethod]
    public void PromptForConfig_C_ChangesTheFolder()
    {
        var first = Path.Combine(_home, "one");
        var second = Path.Combine(_home, "two");
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "x.anonconfig"), "{}");
        With(first + "\n", out _).Resolve(ConfigKind.Anon);

        var picked = With("c\n" + second + "\n1\n", out _).PromptForConfig(ConfigKind.Anon);

        Assert.AreEqual(Path.Combine(second, "x.anonconfig"), picked);
        Assert.AreEqual(second, ConfigFolders.SavedFolder(ConfigKind.Anon));
    }

    [TestMethod]
    public void Extension_PerKind()
    {
        Assert.AreEqual(".etlconfig", ConfigFolders.Extension(ConfigKind.Etl));
        Assert.AreEqual(".anonconfig", ConfigFolders.Extension(ConfigKind.Anon));
        Assert.AreEqual(".synthconfig", ConfigFolders.Extension(ConfigKind.Synth));
    }
}
