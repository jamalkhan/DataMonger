# Known (identified) purchases

Sends, for every customer in `identified.csv`, an **identify** call (creating or updating the subscriber) followed by a
**purchase** activity containing 1 to 3 randomly chosen products from `products.csv`. "Known" means the purchase is
attached to a subscriber by `uid` (the `user_id` column), as opposed to [Anonymous purchases](../PurchaseAnonymous/Readme.md), which identifies
the shopper only by email address.

## Contents

* [InputFormat.md](InputFormat.md) - column layout of `products.csv`, `identified.csv`, `anonymous.csv` (canonical)
* [samples/](samples/README.md) - runnable inputs, config and real output

## Syntax / how to run

Menu entry text: `Known (identified) purchases` (command class `PurchaseKnown`). The app lists commands alphabetically by
name, so the number depends on the build; in the reference build it is `10`.

| Prompt | Default | Accepted |
|---|---|---|
| `Choice:` | none (re-asked on invalid input) | menu number, or `q` to quit |

The command asks nothing else. Everything is driven by `appsettings.json` and the CSV files. The command runs **once per
configured site** (`RunsPerSite` defaults to true), sequentially, under the header `=== Known (identified) purchases -> site: <SiteId> ===`.

```
printf '10\n' | dotnet DataMonger.dll
```

## Inputs

* `LocalData/identified.csv` (read when the command starts) - see [InputFormat.md](InputFormat.md#identifiedcsv).
* `LocalData/products.csv` (loaded at app startup) - see [InputFormat.md](InputFormat.md#productscsv).
* `appsettings.json` next to the DLL: `Sites[]` of `BaseUrl`, `SiteId`, `Username`, `Password`.

Console line printed first: `Customers loaded: N (from identified.csv)`.

## What is sent

Two HTTP requests per customer, strictly in sequence (the purchase is sent only if the identify succeeded). Both are
`POST` with `Accept` and `Content-Type: application/json` and credentials from the site config (the mock confirms an
`Authorization` header is present).

### 1. Identify

`POST {BaseUrl}/ver2/{SiteId}/subscribers/identify` (a trailing `/` on `BaseUrl` is removed). Body shape:

```jsonc
{ "subscriber": {
    "uid": "<col 4 user_id>",
    "unique_client_ids": [ {"name": "email_address", "value": "<col 3>"} ],
    "properties": { "first_name": "<col 1>", "last_name": "<col 2>" },
    "contacts": [ { "contact_type": "email", "contact_value": "<col 3>", "subscription_status": "active" } ] } }
```

Only name, email and user id are sent; there are no additional identifiers and no per-customer attribute records. A
recorded example is in [samples/expected-output/mock-requests-trimmed.json](samples/expected-output/mock-requests-trimmed.json).

### 2. Purchase activity

`POST {BaseUrl}/ver2/{SiteId}/activities`

```jsonc
{ "activity": {
    "identify_person": true,
    "subscriber": { "uid": "<col 4 user_id>" },
    "event": "purchased",
    "properties": {
      "order_id": "TST-ORD-<random 9-digit number 100000000..999999998>",
      "items": [ { "id", "resourceType", "price", "quantity", "productname", "category", "subcategory", "size", "url" } ] } } }
```

`items` has 1, 2 or 3 entries (uniform), each a **distinct** product (products are shuffled with the unseeded `Random.Shared` and the
first N taken), copied field-for-field from `products.csv`. `quantity` is the CSV value, not randomized. Random selection
is unseeded, so runs differ.

## Parallelism, rate limits, retries

* Customers are processed with `Parallel.ForEachAsync`, `MaxDegreeOfParallelism = 50` (`const MaxParallelism` in
  `Program.cs`; not configurable). Console output order is therefore non-deterministic.
* No client-side rate limiter, no retries, no backoff: a `429` or `5xx` is counted as a failure and the customer is
  skipped (verified against the mock with `FailEveryNth`/`ThrottleEveryNth`; the mock saw exactly one request per attempt).
* The command code sets no timeout (the shared `HttpClient` default applies, unless the REST client library sets its own).

## Success / failure counting

* HTTP status 200-299 is success. Anything else, and any transport error (reported through the response's
  `UnhandledError`), is a failure.
* A customer is a **success only if both calls succeed**; it adds 1 to either Success or Failed, never both.
  Identify failure: `[FAIL] <email> identify -> <code> <StatusName>` followed by the response body (first 500 characters,
  indented), and the purchase is skipped. Purchase failure: `[FAIL] <email> activity -> ...` (the identify already went through).
* Success line: `[OK]   <email> identify+activity (N items) -> 202/202`.
* Final line: `=== Known (identified) purchases summary ===  Success: S   Failed: F   Elapsed: hh:mm:ss.fff`. The returned
  tuple is not used by `Program.cs` (no process exit code).

## Outputs

No files other than the log. Diagnostics from the REST client (one `Executing REST method: POST <url>` line per request)
go to `LocalData/logs/app.log`, appended. The mock API's `GET /mock/requests` shows the bodies received.

## Remarks

* Not idempotent: each run creates new `order_id` values and a new purchase per customer; identify is an upsert on the
  server side.
* Blank lines in the CSV are skipped. A row with fewer than four columns throws an unhandled `InvalidDataException`
  (`<file>, line <n>: expected at least 4 comma-separated columns but found <m>.`) before any request is sent, because the
  whole file is parsed into a list first.
* A failure message and its response body are written in a single console call, so output of parallel customers no longer interleaves.
* The same `uid` can legitimately appear in several CSV rows; each row produces its own pair of requests.

## Related

[Anonymous purchases](../PurchaseAnonymous/Readme.md) | [Generate Email Conversion data](../GenerateEmailConversions/Readme.md)
(uses the same identified.csv)
