using DataMonger.Commands;

namespace DataMonger.Tests;

[TestClass]
public class UploadJsonlEventsTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "upload_events_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [TestMethod]
    public void ResolvePathInput_SingleFile_ReturnsOnePath_NotFromFolder()
    {
        var file = Path.Combine(_dir, "a.jsonl");
        File.WriteAllText(file, "{}");

        var resolved = UploadJsonlEvents.ResolvePathInput(file);

        Assert.IsNotNull(resolved);
        CollectionAssert.AreEqual(new[] { file }, resolved!.Paths.ToArray());
        Assert.IsFalse(resolved.FromFolder);
    }

    [TestMethod]
    public void ResolvePathInput_Folder_ReturnsEveryJsonlRecursively_IgnoringOtherFiles_MarkedFromFolder()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        var top = Path.Combine(_dir, "top.jsonl");
        var nested = Path.Combine(sub, "nested.jsonl");
        var notJsonl = Path.Combine(_dir, "readme.txt");
        File.WriteAllText(top, "{}");
        File.WriteAllText(nested, "{}");
        File.WriteAllText(notJsonl, "not jsonl");

        var resolved = UploadJsonlEvents.ResolvePathInput(_dir);

        Assert.IsNotNull(resolved);
        CollectionAssert.AreEquivalent(new[] { top, nested }, resolved!.Paths.ToArray());
        Assert.IsTrue(resolved.FromFolder, "a folder input must be flagged so the caller skips preview prompts");
    }

    [TestMethod]
    public void ResolvePathInput_EmptyFolder_ReturnsNull()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);

        Assert.IsNull(UploadJsonlEvents.ResolvePathInput(empty));
    }

    [TestMethod]
    public void ResolvePathInput_NonexistentPath_ReturnsNull()
    {
        Assert.IsNull(UploadJsonlEvents.ResolvePathInput(Path.Combine(_dir, "does_not_exist")));
    }

    [TestMethod]
    public void ResolvePathInput_BlankInput_ReturnsNull()
    {
        Assert.IsNull(UploadJsonlEvents.ResolvePathInput(""));
        Assert.IsNull(UploadJsonlEvents.ResolvePathInput(null));
        Assert.IsNull(UploadJsonlEvents.ResolvePathInput("   "));
    }

    [TestMethod]
    public void BuildProcessedFileLine_ContainsFilePathAndSummary_TabSeparated()
    {
        var line = UploadJsonlEvents.BuildProcessedFileLine(
            "/some/file.jsonl", success: 120, failure: 3, elapsed: TimeSpan.FromSeconds(12.345), reason: null);

        var parts = line.Split('\t');
        Assert.AreEqual(3, parts.Length, "expected timestamp, file path, and summary, tab-separated");
        Assert.AreEqual("/some/file.jsonl", parts[1]);
        StringAssert.Contains(parts[2], "success=120");
        StringAssert.Contains(parts[2], "failed=3");
        StringAssert.Contains(parts[2], "elapsed=00:00:12.345");
        Assert.IsFalse(parts[2].Contains('('), "no reason parenthetical when reason is null");
    }

    [TestMethod]
    public void BuildProcessedFileLine_WithReason_AppendsItInParentheses()
    {
        var line = UploadJsonlEvents.BuildProcessedFileLine(
            "/some/file.jsonl", success: 0, failure: 1, elapsed: TimeSpan.Zero, reason: "could not be read");

        StringAssert.Contains(line, "(could not be read)");
    }

    [TestMethod]
    public void FailureLogWriter_CreatesNothingUntilFirstWrite_ThenWritesExpectedFormat()
    {
        var path = Path.Combine(_dir, "failures.log");
        using (var writer = new UploadJsonlEvents.FailureLogWriter(path))
        {
            Assert.IsFalse(File.Exists(path), "a clean run must not leave an empty failure log behind");
            Assert.IsFalse(writer.WroteAny);

            writer.Write("/some/file.jsonl", lineNum: 42, status: 400, reason: "Bad Request: invalid email");

            Assert.IsTrue(writer.WroteAny);
        }

        Assert.IsTrue(File.Exists(path));
        var line = File.ReadAllLines(path).Single();
        StringAssert.Contains(line, "file=/some/file.jsonl");
        StringAssert.Contains(line, "line=42");
        StringAssert.Contains(line, "status=400");
        StringAssert.Contains(line, "reason=Bad Request: invalid email");
    }

    [TestMethod]
    public void FailureLogWriter_NoStatus_WritesNA()
    {
        var path = Path.Combine(_dir, "failures2.log");
        using var writer = new UploadJsonlEvents.FailureLogWriter(path);

        writer.Write("/some/file.jsonl", lineNum: 7, status: null, reason: "malformed JSON: unexpected token");

        var line = File.ReadAllLines(path).Single();
        StringAssert.Contains(line, "status=n/a");
    }
}
