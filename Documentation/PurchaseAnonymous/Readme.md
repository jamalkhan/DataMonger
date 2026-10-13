# Anonymous purchases

Sends one **purchase activity** per row of `anonymous.csv`, with 1 to 3 randomly chosen products from `products.csv`. The
shopper is identified only by email address (a guest checkout); no identify call is made and no name is sent.

## Contents

* [Input CSV formats](../PurchaseKnown/InputFormat.md) (canonical copy; see [anonymous.csv](../PurchaseKnown/InputFormat.md#anonymouscsv) and [products.csv](../PurchaseKnown/InputFormat.md#productscsv))
* [samples/](samples/README.md)

## Syntax / how to run

Menu entry text: `Anonymous purchases` (class `PurchaseAnonymous`; number `2` in the reference build, menu is alphabetical).

| Prompt | Default | Accepted |
|---|---|---|
| `Choice:` | none (re-asked if invalid) | menu number or `q` |

No other prompts. The command runs once per configured site (`=== Anonymous purchases -> site: <SiteId> ===`).

```
printf '2\n' | dotnet DataMonger.dll
```

## Inputs

`LocalData/anonymous.csv` (3 columns: first name, last name, email; header skipped; only email used),
`LocalData/products.csv` (loaded at startup), `appsettings.json` (`Sites[]`). First console line:
`Customers loaded: N (from anonymous.csv)`.

## What is sent

One request per row: `POST {BaseUrl}/ver2/{SiteId}/activities`, `Content-Type: application/json`, credentials from the
site config.

```jsonc
{ "activity": {
    "subscriber": { "unique_client_id": { "name": "email_address", "value": "<col 3>" } },
    "event": "purchased",
    "properties": {
      "order_id": "ORD-<random 9-digit number 100000000..999999998>",
      "items": [ { "id", "resourceType", "price", "quantity", "productname", "category", "subcategory", "size", "url" } ] } } }
```

Compare to [Known purchases](../PurchaseKnown/Readme.md): here there is no `identify_person`, no `uid`, and the order id
prefix is `ORD-` rather than `TST-ORD-`. Items: 1-3 distinct products (uniform count, shuffled with unseeded
`Random.Shared`), `quantity` taken from the CSV. See
[samples/expected-output/mock-requests-trimmed.json](samples/expected-output/mock-requests-trimmed.json).

## Parallelism, rate limits, retries

`Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 50` (`Program.cs`, not configurable). No rate limiter, no retries
(the mock saw exactly 10 requests for 10 rows even with injected 429/500), no timeout set by the command code.

## Success / failure counting

2xx = success: `[OK]   <email> (N items) -> 202 Accepted`. Anything else: `[FAIL] <email> (N items) -> 429 TooManyRequests`
plus the response body (up to 500 characters, indented). Summary:
`=== Anonymous purchases summary ===  Success: S   Failed: F   Elapsed: hh:mm:ss.fff`. Output order is non-deterministic.
See [console-with-injected-failures.txt](samples/expected-output/console-with-injected-failures.txt).

## Outputs

No data files. REST-client diagnostics are appended to `LocalData/logs/app.log`.

## Remarks

* Not idempotent: new `order_id` each run.
* Blank CSV lines are skipped. A row with fewer than 3 columns throws an unhandled `InvalidDataException`
  (`<file>, line <n>: expected at least 3 comma-separated columns but found <m>.`) before any request is sent.
* A failure message and its response body are written in a single console call, so output of parallel customers does not interleave.
* Names in the CSV are parsed but never sent.

## Related

[Known (identified) purchases](../PurchaseKnown/Readme.md)
