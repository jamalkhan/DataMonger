using bleak.Api.Rest;
using DataMonger.Commands;
using DataMonger.Common;
using DataMonger.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataMonger.Tests;

/// <summary>
/// Builds the real container the way <c>Program</c> does (Generic Host + <see cref="DataMongerServiceCollectionExtensions.AddDataMonger"/>)
/// so that a command whose constructor asks for an unregistered service fails here rather than at run time.
/// </summary>
[TestClass]
public class ServiceRegistrationTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dm_di_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private IHost BuildHost(IDictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = _dir,
            // Development turns on ValidateOnBuild and ValidateScopes for the container.
            EnvironmentName = Environments.Development,
        });
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }
        builder.Services.AddDataMonger(Path.Combine(_dir, "run.jsonl"));
        return builder.Build();
    }

    [TestMethod]
    public void EveryCommand_CanBeCreatedFromTheContainer()
    {
        using var host = BuildHost();
        var types = CommandFactory.DiscoverCommandTypes();
        Assert.IsNotEmpty(types);

        foreach (var type in types)
        {
            var command = (IConsoleCommand)ActivatorUtilities.CreateInstance(host.Services, type, TestContexts.Site());
            Assert.IsFalse(string.IsNullOrWhiteSpace(command.Name), type.Name);
        }
    }

    [TestMethod]
    public void CommandFactory_CreatesNestedCommands()
    {
        using var host = BuildHost();
        var factory = host.Services.GetRequiredService<CommandFactory>();
        Assert.IsInstanceOfType<UploadJsonlEvents>(factory.Create<UploadJsonlEvents>(TestContexts.Site()));
    }

    [TestMethod]
    public void RestClient_IsASingleton()
    {
        using var host = BuildHost();
        Assert.AreSame(host.Services.GetRequiredService<RestClient>(), host.Services.GetRequiredService<RestClient>());
    }

    [TestMethod]
    public void Logging_FileSinkIsTheOnlyProvider()
    {
        using var host = BuildHost();
        var providers = host.Services.GetServices<ILoggerProvider>().ToList();
        Assert.HasCount(1, providers);
        Assert.IsInstanceOfType<JsonLinesFileLoggerProvider>(providers[0]);
    }

    [TestMethod]
    public void Logging_DefaultsToInformation_WhenNoLoggingSection()
    {
        using var host = BuildHost();
        var factory = host.Services.GetRequiredService<ILoggerFactory>();
        foreach (var category in new[] { "DataMonger.Program", "bleak.Api.Rest.RestClient", "Other" })
        {
            var logger = factory.CreateLogger(category);
            Assert.IsTrue(logger.IsEnabled(LogLevel.Information), category);
            Assert.IsFalse(logger.IsEnabled(LogLevel.Debug), category);
        }
    }

    [TestMethod]
    public void Logging_HonorsCategoryKeysFromConfiguration()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:bleak"] = "Debug",
            ["Logging:LogLevel:DataMonger"] = "Information",
        });
        var factory = host.Services.GetRequiredService<ILoggerFactory>();
        Assert.IsTrue(factory.CreateLogger("bleak.Data.Transformer.Engine.EtlEngine").IsEnabled(LogLevel.Debug));
        Assert.IsFalse(factory.CreateLogger("DataMonger.Commands.SplitCsv").IsEnabled(LogLevel.Debug));
        Assert.IsTrue(factory.CreateLogger("DataMonger.Commands.SplitCsv").IsEnabled(LogLevel.Information));
        Assert.IsFalse(factory.CreateLogger("Other").IsEnabled(LogLevel.Information));
    }
}
