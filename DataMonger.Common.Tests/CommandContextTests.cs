using bleak.Api.Rest;
using DataMonger.Common;

namespace DataMonger.Common.Tests;

[TestClass]
public class CommandContextTests
{
    // Smoke test: CommandContext is a required-init contract shared across commands. This verifies it
    // constructs and round-trips its values. Expand with real DataMonger.Common coverage as it grows.
    [TestMethod]
    public void CommandContext_RoundTripsInitializedValues()
    {
        var products = new[]
        {
            new Product("1", "product", 9.99m, 3, "Tee", "Tops", "Shirts", "M", "https://example.com/tee"),
        };
        var activity = new Uri("https://example.com/activities");
        var identify = new Uri("https://example.com/identify");

        var ctx = new CommandContext
        {
            Products = products,
            Rest = new RestClient(),
            ActivityUri = activity,
            IdentifyUri = identify,
            Username = "user",
            Password = "pass",
            MaxParallelism = 50,
        };

        Assert.AreEqual(1, ctx.Products.Count);
        Assert.AreEqual("Tee", ctx.Products[0].ProductName);
        Assert.AreEqual(activity, ctx.ActivityUri);
        Assert.AreEqual(identify, ctx.IdentifyUri);
        Assert.AreEqual("user", ctx.Username);
        Assert.AreEqual("pass", ctx.Password);
        Assert.AreEqual(50, ctx.MaxParallelism);
    }
}
