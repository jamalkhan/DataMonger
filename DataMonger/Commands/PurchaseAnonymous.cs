using bleak.Api.Rest;
using DataMonger.Common;
using bleak.Data.InfiniteDataLoader;

namespace DataMonger.Commands;

class PurchaseAnonymous(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Anonymous purchases";

    public async Task<(int success, int failure)> Execute()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var successCount = 0;
        var failureCount = 0;
        var people = CsvLoader.ReadPeople(DataPaths.Combine("anonymous.csv")).ToList();
        Console.WriteLine($"Customers loaded: {people.Count} (from anonymous.csv)");

        await Parallel.ForEachAsync(
            people,
            new ParallelOptions { MaxDegreeOfParallelism = ctx.MaxParallelism },
            async (person, ct) =>
        {
            var itemCount = Random.Shared.Next(1, 4);
            var picked = ctx.Products.OrderBy(_ => Random.Shared.Next()).Take(itemCount).ToArray();

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
                        order_id = $"ORD-{Random.Shared.Next(100000000, 999999999)}",
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

        return (successCount, failureCount);
    }
}
