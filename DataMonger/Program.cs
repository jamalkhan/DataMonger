using System.Reflection;
using bleak.Api.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DataMonger.Common;
using bleak.Data.InfiniteDataLoader;

// Use Cases
// Guest Checkout
// -- Just Email Address and a purchase
// Registered User
// -- Account already exists. Email Address, Name, and a purchase
// Shared Account
// -- Multiple people sharing an email address. Email Address, Name, and a purchase. (This is common for B2B use cases where multiple users share a work email address.)

class Program
{
    const int MaxParallelism = 50;

    static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var siteConfigs = config.GetSection("Sites").Get<SiteConfig[]>()
            ?? throw new InvalidOperationException("Sites must be a non-empty array in appsettings.json");
        if (siteConfigs.Length == 0)
        {
            throw new InvalidOperationException("Sites array is empty in appsettings.json");
        }

        var appLogPath = DataPaths.Combine("logs", "app.log");
        Directory.CreateDirectory(Path.GetDirectoryName(appLogPath)!);
        using var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new FileLoggerProvider(appLogPath)));

        var serializer = new JsonSerializer();
        using var httpClient = new HttpClient();
        var rest = new RestClient(httpClient, serializer, serializer, loggerFactory.CreateLogger<RestClient>());

        Console.WriteLine("=== DataMonger ===");
        new ConfigFolders().EnsureAll(); // look for config folders under ~/Documents/DataMonger; prompt if missing
        Console.WriteLine($">>> Configured sites ({siteConfigs.Length}):");
        foreach (var cfg in siteConfigs)
        {
            Console.WriteLine($"      - {cfg.SiteId} @ {cfg.BaseUrl}  (user: {cfg.Username})");
        }
        Console.WriteLine();

        // Build a discovery context against the first config just to read command Names for the menu.
        var menuContext = BuildContext(siteConfigs[0], rest, siteConfigs, loggerFactory);
        var commandTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(IConsoleCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();
        var menuCommands = commandTypes
            .Select(t => (IConsoleCommand)Activator.CreateInstance(t, menuContext)!)
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

        if (!selectedCommand.RunsPerSite)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {selectedName} ===");
            var localCtx = BuildContext(siteConfigs[0], rest, siteConfigs, loggerFactory);
            var localCommand = (IConsoleCommand)Activator.CreateInstance(selectedType, localCtx)!;
            await localCommand.Execute();
            return;
        }

        foreach (var cfg in siteConfigs)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {selectedName} -> site: {cfg.SiteId} ===");
            var siteCtx = BuildContext(cfg, rest, siteConfigs, loggerFactory);
            var siteCommand = (IConsoleCommand)Activator.CreateInstance(selectedType, siteCtx)!;
            await siteCommand.Execute();
        }
    }

    static CommandContext BuildContext(SiteConfig cfg, RestClient rest, IReadOnlyList<SiteConfig> allSites, ILoggerFactory loggerFactory)
    {
        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        return new CommandContext
        {
            Rest = rest,
            ActivityUri = new Uri($"{baseUrl}/ver2/{cfg.SiteId}/activities"),
            IdentifyUri = new Uri($"{baseUrl}/ver2/{cfg.SiteId}/subscribers/identify"),
            BaseUrl = baseUrl,
            SiteId = cfg.SiteId,
            Username = cfg.Username,
            Password = cfg.Password,
            MaxParallelism = MaxParallelism,
            AllSites = allSites,
            LoggerFactory = loggerFactory,
        };
    }
}
