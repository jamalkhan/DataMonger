using System.Text.Json;
using DataMonger.Common;
using Microsoft.Extensions.Logging;

namespace DataMonger.Common.Tests;

[TestClass]
public class JsonLinesFileLoggerProviderTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dm-jsonl-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string LogPath() => Path.Combine(_dir, "nested", "run.jsonl");

    private static List<JsonElement> ReadLines(string path) =>
        File.ReadAllLines(path)
            .Where(l => l.Length > 0)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();

    [TestMethod]
    public void WritesOneValidJsonObjectPerLine_WithFixedFieldsPropertiesAndScopes()
    {
        var path = LogPath();
        using (var provider = new JsonLinesFileLoggerProvider(path))
        using (var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(provider)))
        {
            var logger = factory.CreateLogger("Test.Category");
            using (logger.BeginScope(new Dictionary<string, object?> { ["RunId"] = "abc123", ["SiteId"] = "outer" }))
            using (logger.BeginScope(new Dictionary<string, object?> { ["Command"] = "Demo", ["SiteId"] = "inner" }))
            {
                logger.Log(LogLevel.Information, new EventId(42, "DemoCompleted"),
                    "Done: {RecordCount} records in {DurationMs} ms ({FileName})", 1204, 18.5, "a.csv");
            }
            logger.LogWarning(new EventId(7), new InvalidOperationException("boom"), "Failed {Phase}", "load");
        }

        var lines = ReadLines(path);
        Assert.HasCount(2, lines);

        var info = lines[0];
        Assert.IsTrue(DateTime.TryParse(info.GetProperty("Timestamp").GetString(), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var ts));
        Assert.AreEqual(DateTimeKind.Utc, ts.Kind);
        Assert.AreEqual("Information", info.GetProperty("Level").GetString());
        Assert.AreEqual("Test.Category", info.GetProperty("Category").GetString());
        Assert.AreEqual(42, info.GetProperty("EventId").GetInt32());
        Assert.AreEqual("DemoCompleted", info.GetProperty("EventName").GetString());
        Assert.AreEqual("Done: 1204 records in 18.5 ms (a.csv)", info.GetProperty("Message").GetString());
        Assert.AreEqual(1204, info.GetProperty("RecordCount").GetInt32());
        Assert.AreEqual(18.5, info.GetProperty("DurationMs").GetDouble());
        Assert.AreEqual("a.csv", info.GetProperty("FileName").GetString());
        Assert.IsFalse(info.TryGetProperty("{OriginalFormat}", out _));
        Assert.AreEqual("abc123", info.GetProperty("RunId").GetString());
        Assert.AreEqual("Demo", info.GetProperty("Command").GetString());
        Assert.AreEqual("inner", info.GetProperty("SiteId").GetString(), "innermost scope wins");
        Assert.IsFalse(info.TryGetProperty("Exception", out _));

        var warn = lines[1];
        Assert.AreEqual("Warning", warn.GetProperty("Level").GetString());
        Assert.IsFalse(warn.TryGetProperty("EventName", out _));
        Assert.IsFalse(warn.TryGetProperty("RunId", out _), "scope ended");
        StringAssert.Contains(warn.GetProperty("Exception").GetString(), "InvalidOperationException: boom");
    }

    [TestMethod]
    public void ReservedNamesAndNonKeyValueScopes_AreKeptWithoutBreakingTheLine()
    {
        var path = LogPath();
        using (var provider = new JsonLinesFileLoggerProvider(path))
        using (var factory = LoggerFactory.Create(b => b.AddProvider(provider)))
        {
            var logger = factory.CreateLogger("C");
            using (logger.BeginScope("plain text scope"))
            {
                logger.LogInformation("Level is {Level}, values {Values}, nan {Nan}", "custom", new[] { 1, 2 }, double.NaN);
            }
        }

        var line = ReadLines(path).Single();
        Assert.AreEqual("Information", line.GetProperty("Level").GetString());
        Assert.AreEqual("custom", line.GetProperty("_Level").GetString());
        Assert.AreEqual(2, line.GetProperty("Values").GetArrayLength());
        Assert.AreEqual("NaN", line.GetProperty("Nan").GetString());
        Assert.AreEqual("plain text scope", line.GetProperty("Scope")[0].GetString());
    }

    [TestMethod]
    public void Dispose_DrainsEverythingQueued()
    {
        const int count = 20_000;
        var path = LogPath();
        var provider = new JsonLinesFileLoggerProvider(path);
        var logger = provider.CreateLogger("Drain");
        for (var i = 0; i < count; i++)
        {
            logger.Log(LogLevel.Information, default, i, null, (s, _) => "n=" + s);
        }
        provider.Dispose();

        var lines = File.ReadAllLines(path);
        Assert.HasCount(count, lines);
        Assert.AreEqual("n=0", JsonDocument.Parse(lines[0]).RootElement.GetProperty("Message").GetString());
        Assert.AreEqual($"n={count - 1}", JsonDocument.Parse(lines[^1]).RootElement.GetProperty("Message").GetString());
    }

    [TestMethod]
    public void ConcurrentWriters_ProduceOnlyCompleteLines_AndLoseNothing()
    {
        const int threads = 8, perThread = 5_000;
        var path = LogPath();
        using (var provider = new JsonLinesFileLoggerProvider(path))
        using (var factory = LoggerFactory.Create(b => b.AddProvider(provider)))
        {
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
            {
                var logger = factory.CreateLogger("Worker" + t);
                using (logger.BeginScope(new Dictionary<string, object?> { ["Worker"] = t }))
                {
                    for (var i = 0; i < perThread; i++)
                    {
                        logger.LogInformation("Item {Thread}:{Index}", t, i);
                    }
                }
            });
        }

        var lines = ReadLines(path);
        Assert.HasCount(threads * perThread, lines);
        var keys = lines.Select(l => (l.GetProperty("Thread").GetInt32(), l.GetProperty("Index").GetInt32())).ToHashSet();
        Assert.HasCount(threads * perThread, keys);
        Assert.IsTrue(lines.All(l => l.GetProperty("Worker").GetInt32() == l.GetProperty("Thread").GetInt32()),
            "scopes stay with the thread that logged");
    }

    [TestMethod]
    public void NeverThrows_WhenFileCannotBeOpened_OrAfterDispose_OrWhenAValueThrows()
    {
        // A path below an existing *file* cannot be created.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        using (var broken = new JsonLinesFileLoggerProvider(Path.Combine(blocker, "run.jsonl")))
        {
            Assert.IsFalse(broken.IsWriting);
            broken.CreateLogger("C").LogInformation("ignored {Value}", 1);
        }

        var path = LogPath();
        var provider = new JsonLinesFileLoggerProvider(path);
        var logger = provider.CreateLogger("C");
        logger.LogInformation("bad value {Value}", new ThrowingToString());
        logger.LogInformation("good");
        provider.Dispose();
        provider.Dispose();
        logger.LogInformation("after dispose");

        var line = ReadLines(path).Single();
        Assert.AreEqual("good", line.GetProperty("Message").GetString());
    }

    private sealed class ThrowingToString
    {
        public override string ToString() => throw new InvalidOperationException("nope");
    }
}
