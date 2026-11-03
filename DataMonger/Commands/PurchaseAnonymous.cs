using bleak.Api.Rest;
using System.Diagnostics;
using DataMonger.Common;
using DataMonger.Logging;
using Microsoft.Extensions.Logging;
using bleak.Data.InfiniteDataLoader;

namespace DataMonger.Commands;

partial class PurchaseAnonymous(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Anonymous purchases";

    private readonly ILogger _logger = ctx.LoggerFactory.CreateLogger<PurchaseAnonymous>();

    public async Task<(int success, int failure)> Execute()
    {
        using var scope = _logger.BeginCommandScope(Name, ctx.SiteId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var successCount = 0;
        var failureCount = 0;

        var configPath = new ConfigFolders(logger: ctx.LoggerFactory.CreateLogger<ConfigFolders>()).PromptForConfig(ConfigKind.PurchaseAnonymous);
        if (configPath is null)
        {
            Console.WriteLine("[FAIL] No config file provided or it does not exist. Aborting.");
            Log.NoConfig(_logger);
            return (0, 1);
        }
        PurchaseConfig config;
        List<Person> people;
        Product[] products;
        string customersFileName;
        string productsFileName;
        var loadStart = Stopwatch.GetTimestamp();
        try
        {
            config = CommandConfigFile.Load<PurchaseConfig>(configPath, ctx.LoggerFactory.CreateLogger(typeof(CommandConfigFile)));
            var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            var customersFile = CommandConfigFile.ResolvePath(config.CustomersFile, configDir, DataPaths.Combine("anonymous.csv"));
            var productsFile = CommandConfigFile.ResolvePath(config.ProductsFile, configDir, DataPaths.Combine("products.csv"));
            customersFileName = Path.GetFileName(customersFile);
            productsFileName = Path.GetFileName(productsFile);
            var csvLogger = ctx.LoggerFactory.CreateLogger(typeof(CsvLoader));
            people = CsvLoader.ReadPeople(customersFile, csvLogger).ToList();
            products = CsvLoader.ReadProducts(productsFile, csvLogger).ToArray();
            Console.WriteLine($"Config        : {configPath}");
            Console.WriteLine($"Customers     : {people.Count} (from {customersFile})");
            Console.WriteLine($"Products      : {products.Length} (from {productsFile})");
        }
        catch (Exception ex) when (ex is CommandConfigException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[FAIL] {ex.Message}");
            // The message can quote a CSV cell, so only the exception type goes to the log.
            Log.InputsInvalid(_logger, Path.GetFileName(configPath), ex.GetType().Name);
            return (0, 1);
        }
        Log.InputsLoaded(_logger, Path.GetFileName(configPath), people.Count, products.Length, Stopwatch.GetElapsedTime(loadStart).TotalMilliseconds);
        if (products.Length == 0)
        {
            Console.WriteLine("[FAIL] The products file has no rows: cannot pick items.");
            Log.NoProducts(_logger, productsFileName);
            return (0, 1);
        }
        var orderIdPrefix = config.OrderIdPrefix ?? "ORD-";
        Log.Started(_logger, Path.GetFileName(configPath), customersFileName, people.Count, productsFileName, products.Length,
            config.MinItems, config.MaxItems, ctx.MaxParallelism);
        var stats = new HttpFanOutStats(ctx.MaxParallelism);
        var fanOutStart = Stopwatch.GetTimestamp();

        await Parallel.ForEachAsync(
            people,
            new ParallelOptions { MaxDegreeOfParallelism = ctx.MaxParallelism },
            async (person, ct) =>
        {
            var itemCount = Random.Shared.Next(config.MinItems, config.MaxItems + 1);
            var picked = products.OrderBy(_ => Random.Shared.Next()).Take(itemCount).ToArray();

            var payload = new
            {
                activity = new
                {
                    subscriber = new
                    {
                        unique_client_id = new { name = "email_address", value = person.EmailAddress }
                    },
                    @event = "purchased",
                    properties = new
                    {
                        order_id = $"{orderIdPrefix}{Random.Shared.Next(100000000, 999999999)}",
                        items = picked.Select(p => new
                        {
                            id = p.Id,
                            resourceType = p.ResourceType,
                            price = p.Price,
                            quantity = p.Quantity,
                            productname = p.ProductName,
                            category = p.Category,
                            subcategory = p.Subcategory,
                            size = p.Size,
                            url = p.Url
                        }).ToArray()
                    }
                }
            };

            var requestStart = stats.Begin();
            var result = await ctx.Rest.ExecuteRestMethodAsync<string, string>(
                uri: ctx.ActivityUri,
                verb: HttpVerbs.POST,
                payload: payload,
                serializedPayload: null!,
                parameters: null!,
                headers: null!,
                username: ctx.Username,
                password: ctx.Password,
                accept: "application/json",
                contentType: "application/json",
                cancellationToken: ct);

            var statusCode = (int)result.Status;
            stats.End(requestStart, statusCode);
            var ok = statusCode is >= 200 and < 300;
            if (ok)
            {
                Interlocked.Increment(ref successCount);
                Console.WriteLine($"[OK]   {person.EmailAddress} ({itemCount} item{(itemCount == 1 ? "" : "s")}) -> {statusCode} {result.Status}");
            }
            else
            {
                Interlocked.Increment(ref failureCount);
                var detail = result.Error ?? result.UnhandledError ?? "(no response body)";
                if (detail.Length > 500)
                {
                    detail = detail[..500] + "...";
                }
                Console.WriteLine($"[FAIL] {person.EmailAddress} ({itemCount} item{(itemCount == 1 ? "" : "s")}) -> {statusCode} {result.Status}\n       {detail.Replace("\n", "\n       ")}");
            }
        });

        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"=== {Name} summary ===  Success: {successCount}   Failed: {failureCount}   Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
        stats.LogSummary(_logger, "purchase activities", Stopwatch.GetElapsedTime(fanOutStart).TotalMilliseconds);
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        Log.Completed(_logger, people.Count, successCount, failureCount, durationMs, CommandLogging.RecordsPerSecond(people.Count, durationMs));

        return (successCount, failureCount);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1500, EventName = "PurchaseAnonymousStarted", Level = LogLevel.Information,
            Message = "Anonymous purchases started: config {JobName}, customers {FileName} ({CustomerCount}), products {ProductsFileName} ({ProductCount}), min/max items {MinItems}/{MaxItems}, max parallelism {MaxParallelism}")]
        public static partial void Started(ILogger logger, string jobName, string fileName, int customerCount, string productsFileName, int productCount, int minItems, int maxItems, int maxParallelism);

        [LoggerMessage(EventId = 1501, EventName = "PurchaseAnonymousCompleted", Level = LogLevel.Information,
            Message = "Anonymous purchases completed: {RecordCount} customer(s), {SuccessCount} succeeded, {ErrorCount} failed in {DurationMs} ms ({RecordsPerSecond} customers/s)")]
        public static partial void Completed(ILogger logger, int recordCount, int successCount, int errorCount, double durationMs, double recordsPerSecond);

        [LoggerMessage(EventId = 1502, EventName = "PurchaseAnonymousInputsLoaded", Level = LogLevel.Debug,
            Message = "Load phase finished: config {JobName}, {CustomerCount} customer(s), {ProductCount} product(s) in {DurationMs} ms")]
        public static partial void InputsLoaded(ILogger logger, string jobName, int customerCount, int productCount, double durationMs);

        [LoggerMessage(EventId = 1503, EventName = "PurchaseAnonymousInputsInvalid", Level = LogLevel.Error,
            Message = "Config {JobName} or its input files could not be loaded ({ExceptionType}); aborting")]
        public static partial void InputsInvalid(ILogger logger, string jobName, string exceptionType);

        [LoggerMessage(EventId = 1504, EventName = "PurchaseAnonymousNoConfig", Level = LogLevel.Warning,
            Message = "No config selected; aborting")]
        public static partial void NoConfig(ILogger logger);

        [LoggerMessage(EventId = 1505, EventName = "PurchaseAnonymousNoProducts", Level = LogLevel.Warning,
            Message = "Products file {FileName} has no rows; aborting")]
        public static partial void NoProducts(ILogger logger, string fileName);
    }
}
