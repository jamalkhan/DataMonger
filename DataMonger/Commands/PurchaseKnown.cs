using bleak.Api.Rest;
using DataMonger.Common;
using bleak.Data.InfiniteDataLoader;

namespace DataMonger.Commands;

public class PurchaseKnown(CommandContext ctx) : IConsoleCommand
{
    public string Name => "Known (identified) purchases";

    public async Task<(int success, int failure)> Execute()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var successCount = 0;
        var failureCount = 0;
        var people = CsvLoader.ReadIdentified(DataPaths.Combine("identified.csv")).ToList();
        Console.WriteLine($"Customers loaded: {people.Count} (from identified.csv)");

        await Parallel.ForEachAsync(
            people,
            new ParallelOptions { MaxDegreeOfParallelism = ctx.MaxParallelism },
            async (person, ct) =>
        {
            // 1) Identify the subscriber
            var identifyPayload = new
            {
                subscriber = new
                {
                    uid = person.UserId,
                    unique_client_ids = new[]
                    {
                        new { name = "email_address", value = person.EmailAddress }
                    },
                    properties = new
                    {
                        first_name = person.FirstName,
                        last_name = person.LastName
                    },
                    contacts = new object[]
                    {
                        new
                        {
                            contact_type = "email",
                            contact_value = person.EmailAddress,
                            subscription_status = "active"
                        }
                    }
                }
            };

            var identifyResult = await ctx.Rest.ExecuteRestMethodAsync<string, string>(
                uri: ctx.IdentifyUri,
                verb: HttpVerbs.POST,
                payload: identifyPayload,
                serializedPayload: null!,
                parameters: null!,
                headers: null!,
                username: ctx.Username,
                password: ctx.Password,
                accept: "application/json",
                contentType: "application/json",
                cancellationToken: ct);

            var identifyStatus = (int)identifyResult.Status;
            if (identifyStatus is < 200 or >= 300)
            {
                Interlocked.Increment(ref failureCount);
                var detail = identifyResult.Error ?? identifyResult.UnhandledError ?? "(no response body)";
                if (detail.Length > 500)
                {
                    detail = detail[..500] + "...";
                }
                Console.WriteLine($"[FAIL] {person.EmailAddress} identify -> {identifyStatus} {identifyResult.Status}\n       {detail.Replace("\n", "\n       ")}");
                return;
            }

            // 2) Send the purchase activity
            var itemCount = Random.Shared.Next(1, 4);
            var picked = ctx.Products.OrderBy(_ => Random.Shared.Next()).Take(itemCount).ToArray();

            var activityPayload = new
            {
                activity = new
                {
                    identify_person = true,
                    subscriber = new
                    {
                        uid = person.UserId,
                    },
                    @event = "purchased",
                    properties = new
                    {
                        order_id = $"TST-ORD-{Random.Shared.Next(100000000, 999999999)}",
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

            var activityResult = await ctx.Rest.ExecuteRestMethodAsync<string, string>(
                uri: ctx.ActivityUri,
                verb: HttpVerbs.POST,
                payload: activityPayload,
                serializedPayload: null!,
                parameters: null!,
                headers: null!,
                username: ctx.Username,
                password: ctx.Password,
                accept: "application/json",
                contentType: "application/json",
                cancellationToken: ct);

            var activityStatus = (int)activityResult.Status;
            if (activityStatus is >= 200 and < 300)
            {
                Interlocked.Increment(ref successCount);
                Console.WriteLine($"[OK]   {person.EmailAddress} identify+activity ({itemCount} item{(itemCount == 1 ? "" : "s")}) -> {identifyStatus}/{activityStatus}");
            }
            else
            {
                Interlocked.Increment(ref failureCount);
                var detail = activityResult.Error ?? activityResult.UnhandledError ?? "(no response body)";
                if (detail.Length > 500)
                {
                    detail = detail[..500] + "...";
                }
                Console.WriteLine($"[FAIL] {person.EmailAddress} activity -> {activityStatus} {activityResult.Status}\n       {detail.Replace("\n", "\n       ")}");
            }
        });

        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"=== {Name} summary ===  Success: {successCount}   Failed: {failureCount}   Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");

        return (successCount, failureCount);
    }
}
