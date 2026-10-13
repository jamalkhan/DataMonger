# UploadJsonlIdentifies

Menu entry: **`API => Upload JSONL Identifies (identify user)`**
Source: `DataMonger/Commands/UploadJsonlIdentifies.cs`

Reads one or more `.jsonl` files of user-profile records and sends **each line, unchanged, as the body of one `POST {BaseUrl}/ver2/{SiteId}/subscribers/identify` request** (the "identify a user" call), up to 50 requests at a time. HTTP 2xx is a success, anything else a failure. It is the simplest of the upload commands: no failure log, no processed-files log, and the two preview prompts are always shown.

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | Prompts, request, concurrency, logging |
| [InputFormat.md](InputFormat.md) | The profile record shape and validation |
| [Troubleshooting.md](Troubleshooting.md) | Messages and how to reproduce them |
| [samples/README.md](samples/README.md) | Runnable inputs and recorded output |

## Syntax / how to run

```text
dotnet DataMonger.dll        # run from a folder that has (or whose ancestors have) LocalData/
```

Choose `API => Upload JSONL Identifies (identify user)` (number `6` in the sample build). Prompts in order:

| # | Prompt | Accepts | Default |
|---|---|---|---|
| 1 | `Choice:` | command number or `q` | none, re-prompts |
| 2 | `Choice [1-N], or type a full path to a .jsonl file or a folder (...)` | number of a listed file, a path to one `.jsonl`, or a folder (all `*.jsonl` under it, recursively) | none |
| 3 | `Choice [1-M]:` under `Available sites:` | number of a `Sites` entry | none |
| 4 | `Send this first request (of N total)? [Enter to continue, Esc to abort]` | Enter / any key = continue, Esc = abort | continue |
| 5 | `Continue with the remaining N line(s)? [Enter to continue, Esc to abort]` | same | continue |

Prompts 4 and 5 are asked **for every file**, also in folder mode (unlike [UploadJsonlEvents](../UploadJsonlEvents/Readme.md)). They use `Console.ReadKey`, so a real terminal is required; with piped stdin the process dies with an unhandled `InvalidOperationException`. To script it, drive it through a pseudo-terminal (the recorded samples were produced that way, see [samples/README.md](samples/README.md)).

File discovery: the picker lists `*.jsonl` directly inside `LocalData/jsonl/profile` (sorted by full path, case-insensitive), where `LocalData` is the folder four levels above the executable's folder if it exists, else `./LocalData`. If `LocalData/jsonl/profile` does not exist, the prompt is `JSONL directory not found (...). Enter a full path ...`. If it exists but is empty the command prints `[FAIL] No .jsonl files found in ...` and aborts. Typed paths may be quoted. Invalid input aborts (`[FAIL] Invalid choice.`, `[FAIL] Path not found: ...`, `[FAIL] No file(s) selected. Aborting.`); there is no re-prompt.

## Inputs

* JSONL file(s): [InputFormat.md](InputFormat.md).
* `appsettings.json` `Sites`; the command shows its own site list (`  1) demo  (http://localhost:5281)`) and uses the `BaseUrl`, `SiteId`, `Username`, `Password` of the entry you pick.

## Outputs

* Console only. Per file: `=== API => Upload JSONL Identifies (identify user) summary ===  Site: <id>   Success: N   Failed: N   Elapsed: hh:mm:ss.fff`; with 2+ files also a `grand total` line.
* No failure log and no processed-files log. The only file written is the shared `LocalData/logs/app.log`, which contains the HTTP client's `Executing REST method: POST <url>` line per request (this command adds no lines of its own).

## Remarks

**Request.**

| Part | Value |
|---|---|
| Method / URL | `POST {BaseUrl}/ver2/{SiteId}/subscribers/identify` |
| Auth | HTTP Basic with the chosen site's `Username` / `Password` (the mock only records that the header exists) |
| Headers | `Accept: application/json`, `Content-Type: application/json` |
| Body | the line exactly as in the file: no wrapping under `subscriber`, no renamed fields (`user_id` stays `user_id`) |

**Mock target.** `http://localhost:5281` answers `202 Accepted` `{"status":"accepted","requestId":N,"route":"identify"}`; `400` for a non-JSON body; `500` / `429` when `MockApi__FailEveryNth` / `MockApi__ThrottleEveryNth` hit.

**Ordering and concurrency.** The first non-blank line is sent alone and its response is previewed (status line plus pretty-printed body, or the error text); after confirmation the rest of the lines run in `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 50` (`MaxParallelism` constant in `Program.cs`). Output order is therefore not file order. Files are processed sequentially. If the first request fails, the failure is printed and you are still asked whether to continue.

**Rate limiting, retries, batching.** None. No `System.Threading.RateLimiting` limiter, no delay, no retry, one HTTP request per line. `429`/`500` are counted as failures; `Retry-After` is ignored (verified: 10 lines produce exactly 10 requests even with 429s injected).

**Local validation.** Lines after the first are parsed with `JObject.Parse`; a line that is not a JSON object is not sent, counted failed and reported as `[WARN] Skipping malformed line N: <message>`. The first non-blank line must parse or the preview prints `[FAIL] First line is not valid JSON: ...` and `[ABORT] Upload cancelled.` (counted `(0, 0)`). Blank lines are skipped without being counted.

**Failure output.** `[FAIL] L<line> -> <code> <StatusName>` plus the body or transport error (cut at 500 chars), printed to the console only. Status `0` means the request never got a response.

**Progress.** Every 1,000 completed lines: `  ... N/TOTAL lines processed (ok X, fail Y)`.

**Resume.** None. Nothing is recorded about what was uploaded; re-running re-sends every line.

**Run once.** `RunsPerSite` is not overridden (true), so with several `Sites` the outer loop calls the command once per site, but a static flag makes every call after the first print `[skip] UploadJsonlIdentifies already ran this session (it picks its own site).`

**Exit code.** Always 0 unless an exception escapes; the success/failure tuple is not used. An unreadable file (`File.ReadAllLinesAsync`) is not caught and ends the process with an unhandled exception.

## Examples

[samples/README.md](samples/README.md): success, 500s, 429s, with files for 10 users and a file with a malformed line.

## Related

[UploadJsonlEvents](../UploadJsonlEvents/Readme.md) (same request model plus logs), [UploadJsonlResources](../UploadJsonlResources/Readme.md), [ApiBulkRunner](../ApiBulkRunner/Readme.md)
