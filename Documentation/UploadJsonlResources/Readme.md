# UploadJsonlResources

Menu entry: **`API => Upload JSONL Resources (create/update)`**
Source: `DataMonger/Commands/UploadJsonlResources.cs`

Reads one `.jsonl` file of resource (product) records and sends each line as the body of **`PUT {BaseUrl}/ver2/{SiteId}/resources/{resource_id}`**, where `resource_id` is taken from the record itself. Up to 50 requests run concurrently. HTTP 2xx counts as success.

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | Prompts, request, concurrency, output |
| [InputFormat.md](InputFormat.md) | Record shape, `resource_id` rules |
| [Troubleshooting.md](Troubleshooting.md) | Messages and reproductions |
| [samples/README.md](samples/README.md) | Inputs and recorded output |

## Syntax / how to run

```text
dotnet DataMonger.dll      # from a folder with (or under) LocalData/
```

Choose `API => Upload JSONL Resources (create/update)` (number `7` in the sample build). Prompts:

| # | Prompt | Accepts | Default |
|---|---|---|---|
| 1 | `Choice:` | command number or `q` | none, re-prompts |
| 2 | `Choice [1-N]:` under `Available JSONL files in <dir>:` | number of a listed file | none |
| 3 | `Choice [1-M]:` under `Available sites:` | number of a `Sites` entry | none |

There are **no** preview/confirmation prompts: after prompt 3 the upload starts immediately, so this command works with piped stdin (`printf '7\n2\n1\n' | dotnet DataMonger.dll`).

File discovery: `*.jsonl` files directly inside `LocalData/jsonl/products` (not recursive), sorted by full path case-insensitively, where `LocalData` is the folder four levels above the executable's folder if present, else `./LocalData`. Only a number is accepted at prompt 2; typed paths and folders are **not** supported here (unlike the Events/Identifies commands). If the folder does not exist, the prompt becomes `JSONL directory not found (...). Enter a full path to a .jsonl file:` (single file; quotes stripped). Folder empty: `[FAIL] No .jsonl files found in ...`. Invalid input: `[FAIL] Invalid choice.`, then `[FAIL] No file selected. Aborting.` / `[FAIL] No site selected. Aborting.`; no re-prompt.

## Inputs

* One JSONL file: [InputFormat.md](InputFormat.md).
* `appsettings.json` `Sites`; the command lists them (`  1) demo  (http://localhost:5281)`) and uses the picked entry's `BaseUrl`, `SiteId`, `Username`, `Password`.

## Outputs

* Console: a header (`File:`, `Site ID:`, `Target:  <BaseUrl>/ver2/<SiteId>/resources/{resource_id}`, `Parallelism: 50`), warnings/failures, then `=== API => Upload JSONL Resources (create/update) summary ===  Site: <id>   Success: N   Failed: N   Elapsed: hh:mm:ss.fff`.
* No failure log or processed-files log. Only the HTTP client's per-request lines go to `LocalData/logs/app.log`.

## Remarks

**Request.**

| Part | Value |
|---|---|
| Method | `PUT` |
| URL | `{BaseUrl without trailing /}/ver2/{SiteId}/resources/{Uri.EscapeDataString(resource_id)}`; e.g. `LAMP 1/2` becomes `LAMP%201%2F2` |
| Auth | HTTP Basic with the picked site's `Username` / `Password` |
| Headers | `Accept: application/json`, `Content-Type: application/json` |
| Body | the whole line unchanged, including `resource_id` and `resource_type` |

The mock answers `200` for PUT (`{"status":"accepted","requestId":N,"route":"resources"}`), `400` for a non-JSON body, and `500`/`429` when injected with `MockApi__FailEveryNth` / `MockApi__ThrottleEveryNth`. (The mock's `PUT` route accepts any `resource_id`.)

**Concurrency.** Every line, including the first, goes through one `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 50` (the `MaxParallelism` constant in `Program.cs`). Order of requests, console output and failure messages is not file order. There is no first-request preview.

**Rate limiting, retries, batching.** None: no `System.Threading.RateLimiting`, no delay, no retry, one request per line. A `429` is just a failure (`Retry-After` ignored). Verified: 10 lines produced exactly 10 mock requests with 429s injected.

**Response handling.** 200-299 success; otherwise `[FAIL] L<line> resource_id=<id> -> <code> <StatusName>` and the body or transport error text (cut at 500 chars). Status `0` means no response was received.

**Local validation.**
* Blank/whitespace line: skipped silently, not counted.
* Not a JSON object: `[WARN] Skipping malformed line N: <message>`, counted failed, not sent.
* No `resource_id`, or blank: `[WARN] Skipping line N: missing 'resource_id'.`, counted failed, not sent.
* A numeric `resource_id` is accepted and converted to text (`77` becomes `/resources/77`).

**Progress.** Every 1,000 completed lines: `  ... N/TOTAL lines processed (ok X, fail Y)`.

**Resume.** None. The command keeps no record of what was sent. Because the API verb is `PUT` on a per-resource URL, re-sending a file is a create-or-update of the same resources, which is why re-runs are normally safe on a real service; the tool itself does not guard against it.

**Run once.** `RunsPerSite` is not overridden, so with several `Sites` the outer loop invokes it once per site, but a static flag makes later calls print `[skip] UploadJsonlResources already ran this session (it picks its own site).`

**Exit code.** Always 0 unless an unhandled exception occurs (e.g. an unreadable file); the success/failure tuple is ignored by `Main`.

## Examples

[samples/README.md](samples/README.md): 10 products, injected failures/throttling, and a file with bad lines.

## Related

[UploadJsonlEvents](../UploadJsonlEvents/Readme.md), [UploadJsonlIdentifies](../UploadJsonlIdentifies/Readme.md), [ApiBulkRunner](../ApiBulkRunner/Readme.md)
