using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DataMonger.Common;
using DataMonger.Hosting;
using DataMonger.Logging;

// Use Cases
// Guest Checkout
// -- Just Email Address and a purchase
// Registered User
// -- Account already exists. Email Address, Name, and a purchase
// Shared Account
// -- Multiple people sharing an email address. Email Address, Name, and a purchase. (This is common for B2B use cases where multiple users share a work email address.)

partial class Program
{
    const int MaxParallelism = 50;

    static async Task Main(string[] args)
    {
        // Generic Host for configuration, logging and DI only: the host is built but never started (no Run/RunAsync),
        // so no hosted-service lifetime messages appear and Ctrl+C keeps its default behavior. Default sources apply:
        // appsettings.json and appsettings.{Environment}.json from the output folder, environment variables, command line.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // The host loads appsettings.json as optional; DataMonger requires it.
        var appSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            throw new FileNotFoundException(
                $"The configuration file 'appsettings.json' was not found and is not optional. The expected physical path was '{appSettingsPath}'.",
                appSettingsPath);
        }

        var siteConfigs = builder.Configuration.GetSection("Sites").Get<SiteConfig[]>()
            ?? throw new InvalidOperationException("Sites must be a non-empty array in appsettings.json");
        if (siteConfigs.Length == 0)
        {
            throw new InvalidOperationException("Sites array is empty in appsettings.json");
        }

        // One JSON-lines log file per run (LocalData/logs/datamonger_<local yyyyMMdd_HHmmss>.jsonl). Levels come
        // from the "Logging" section of the configuration (bound by the host); Information when the section is missing.
        var runId = Guid.NewGuid().ToString("N")[..8];
        var appLogPath = BuildRunLogPath(runId);
        builder.Services.AddDataMonger(appLogPath);

        // Disposing the host disposes the container, which drains and flushes the file sink it owns.
        using var host = builder.Build();
        var services = host.Services;

        // Program lives in the global namespace; name the category explicitly so the "DataMonger" level key covers it.
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DataMonger.Program");
        using var runScope = RunScope(logger, runId);
        var runStart = Stopwatch.GetTimestamp();
        var outcome = "completed";
        Log.RunStarted(logger, AppVersion(), RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture,
            Environment.ProcessorCount, RuntimeInformation.FrameworkDescription, siteConfigs.Length, MaxParallelism, appLogPath);

        try
        {
            await RunMenu(siteConfigs, services, logger);
        }
        catch (Exception ex)
        {
            outcome = "failed";
            Log.RunFailed(logger, ex);
            throw;
        }
        finally
        {
            Log.RunFinished(logger, outcome, Stopwatch.GetElapsedTime(runStart).TotalMilliseconds);
        }
    }

    static async Task RunMenu(SiteConfig[] siteConfigs, IServiceProvider services, ILogger logger)
    {
        var commandFactory = services.GetRequiredService<CommandFactory>();

        Console.WriteLine("=== DataMonger ===");
        services.GetRequiredService<ConfigFolders>().EnsureAll(); // look for config folders under ~/Documents/DataMonger; prompt if missing
        Console.WriteLine($">>> Configured sites ({siteConfigs.Length}):");
        foreach (var cfg in siteConfigs)
        {
            Console.WriteLine($"      - {cfg.SiteId} @ {cfg.BaseUrl}  (user: {cfg.Username})");
        }
        Console.WriteLine();

        // Build a discovery context against the first config just to read command Names for the menu.
        var menuContext = BuildContext(siteConfigs[0], siteConfigs);
        var menuCommands = CommandFactory.DiscoverCommandTypes()
            .Select(t => commandFactory.Create(t, menuContext))
            .OrderBy(c => c.Name)
            .ToList();
        var orderedTypes = menuCommands.Select(c => c.GetType()).ToList();

        if (menuCommands.Count == 0)
        {
            Console.WriteLine("No commands available.");
            return;
        }

        int selectedIndex = -1;
        while (selectedIndex < 0)
        {
            Console.WriteLine($"Available commands (will run against all {siteConfigs.Length} site(s)):");
            for (var i = 0; i < menuCommands.Count; i++)
            {
                Console.WriteLine($"  {i + 1}) {menuCommands[i].Name}");
            }
            Console.WriteLine("  q) Quit");
            Console.Write("Choice: ");

            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (input == "q")
            {
                Console.WriteLine("Quitting.");
                Log.QuitFromMenu(logger);
                return;
            }
            if (int.TryParse(input, out var n) && n >= 1 && n <= menuCommands.Count)
            {
                selectedIndex = n - 1;
                break;
            }

            Console.WriteLine($"Invalid choice. Please enter 1-{menuCommands.Count} or q.");
            Console.WriteLine();
        }

        var selectedType = orderedTypes[selectedIndex];
        var selectedName = menuCommands[selectedIndex].Name;
        var selectedCommand = menuCommands[selectedIndex];
        Log.CommandSelected(logger, selectedName, selectedType.Name, selectedCommand.RunsPerSite, selectedCommand.RunsPerSite ? siteConfigs.Length : 1);

        if (!selectedCommand.RunsPerSite)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {selectedName} ===");
            var localCtx = BuildContext(siteConfigs[0], siteConfigs);
            var localCommand = commandFactory.Create(selectedType, localCtx);
            await ExecuteLogged(localCommand, selectedName, CommandLogging.LocalSiteId, logger);
            return;
        }

        foreach (var cfg in siteConfigs)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {selectedName} -> site: {cfg.SiteId} ===");
            var siteCtx = BuildContext(cfg, siteConfigs);
            var siteCommand = commandFactory.Create(selectedType, siteCtx);
            await ExecuteLogged(siteCommand, selectedName, cfg.SiteId, logger);
        }
    }

    static async Task ExecuteLogged(IConsoleCommand command, string name, string siteId, ILogger logger)
    {
        var start = Stopwatch.GetTimestamp();
        var (success, failure) = await command.Execute();
        Log.CommandReturned(logger, name, siteId, success, failure, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>LocalData/logs/datamonger_&lt;local yyyyMMdd_HHmmss&gt;.jsonl, with the run id appended if that name is taken.</summary>
    static string BuildRunLogPath(string runId)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var path = DataPaths.Combine("logs", $"datamonger_{stamp}.jsonl");
        return File.Exists(path) ? DataPaths.Combine("logs", $"datamonger_{stamp}_{runId}.jsonl") : path;
    }

    static string AppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    static readonly Func<ILogger, string, IDisposable?> RunScope = LoggerMessage.DefineScope<string>("Run {RunId}");

    static partial class Log
    {
        [LoggerMessage(EventId = 1, EventName = "RunStarted", Level = LogLevel.Information,
            Message = "DataMonger run started: version {AppVersion}, {OsDescription} ({OsArchitecture}), {ProcessorCount} processors, {DotNetVersion}; {SiteCount} site(s) configured, max parallelism {MaxParallelism}; log file {FilePath}")]
        public static partial void RunStarted(ILogger logger, string appVersion, string osDescription, Architecture osArchitecture,
            int processorCount, string dotNetVersion, int siteCount, int maxParallelism, string filePath);

        [LoggerMessage(EventId = 2, EventName = "RunFinished", Level = LogLevel.Information,
            Message = "DataMonger run finished ({Outcome}) in {DurationMs} ms")]
        public static partial void RunFinished(ILogger logger, string outcome, double durationMs);

        [LoggerMessage(EventId = 3, EventName = "RunFailed", Level = LogLevel.Error,
            Message = "DataMonger run failed with an unhandled exception")]
        public static partial void RunFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 4, EventName = "CommandSelected", Level = LogLevel.Information,
            Message = "Command selected: {Command} ({CommandType}); runs per site: {RunsPerSite}; invocations: {InvocationCount}")]
        public static partial void CommandSelected(ILogger logger, string command, string commandType, bool runsPerSite, int invocationCount);

        [LoggerMessage(EventId = 5, EventName = "CommandReturned", Level = LogLevel.Debug,
            Message = "Command {Command} returned for site {SiteId}: success={SuccessCount}, failure={ErrorCount} in {DurationMs} ms")]
        public static partial void CommandReturned(ILogger logger, string command, string siteId, int successCount, int errorCount, double durationMs);

        [LoggerMessage(EventId = 6, EventName = "QuitFromMenu", Level = LogLevel.Information,
            Message = "Quit from the command menu")]
        public static partial void QuitFromMenu(ILogger logger);
    }

    static CommandContext BuildContext(SiteConfig cfg, IReadOnlyList<SiteConfig> allSites)
    {
        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        return new CommandContext
        {
            ActivityUri = new Uri($"{baseUrl}/ver2/{cfg.SiteId}/activities"),
            IdentifyUri = new Uri($"{baseUrl}/ver2/{cfg.SiteId}/subscribers/identify"),
            BaseUrl = baseUrl,
            SiteId = cfg.SiteId,
            Username = cfg.Username,
            Password = cfg.Password,
            MaxParallelism = MaxParallelism,
            AllSites = allSites,
        };
    }
}
