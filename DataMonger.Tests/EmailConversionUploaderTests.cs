using DataMonger.Commands;
using Newtonsoft.Json.Linq;

namespace DataMonger.Tests;

[TestClass]
public class EmailConversionUploaderTests
{
    [TestMethod]
    public void ExtractEventName_ReadsEventType_FallsBackToUnknown()
    {
        Assert.AreEqual("email_clicked", ApiEmailConversionEvents.ExtractEventName(JObject.Parse("{\"event_type\":\"email_clicked\"}")));
        Assert.AreEqual("(unknown event)", ApiEmailConversionEvents.ExtractEventName(JObject.Parse("{\"x\":1}")));
        Assert.AreEqual("(unknown event)", ApiEmailConversionEvents.ExtractEventName(JObject.Parse("{\"event_type\":5}")));
    }

    [TestMethod]
    [DataRow(null, 120.0)]
    [DataRow("", 120.0)]
    [DataRow("  ", 120.0)]
    [DataRow("0", 0.0)]
    [DataRow("2.5", 2.5)]
    [DataRow(" 30 ", 30.0)]
    public void TryParseMaxDelay_AcceptsBlankDefaultAndNonNegativeNumbers(string? input, double expectedSeconds)
    {
        Assert.IsTrue(ApiEmailConversionEvents.TryParseMaxDelay(input, out var delay));
        Assert.AreEqual(expectedSeconds, delay.TotalSeconds, 1e-9);
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("abc")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    public void TryParseMaxDelay_RejectsInvalid(string input)
    {
        Assert.IsFalse(ApiEmailConversionEvents.TryParseMaxDelay(input, out _));
    }

    [TestMethod]
    public void NextDelay_ZeroMax_IsZero_OtherwiseWithinWindow()
    {
        var rnd = new Random(1);
        Assert.AreEqual(TimeSpan.Zero, ApiEmailConversionEvents.NextDelay(rnd, TimeSpan.Zero));
        for (var i = 0; i < 100; i++)
        {
            var d = ApiEmailConversionEvents.NextDelay(rnd, TimeSpan.FromSeconds(5));
            Assert.IsTrue(d >= TimeSpan.Zero && d < TimeSpan.FromSeconds(5));
        }
    }
}
