# UploadJsonlEvents

Menu entry: **`API => Upload JSONL Events (track event)`**
Source: `DataMonger/Commands/UploadJsonlEvents.cs`

Reads one or more `.jsonl` files and sends **each line, unchanged, as the body of one `POST {BaseUrl}/ver2/{SiteId}/activities` request** (the "track an event" call). Requests for a file are sent concurrently (up to 50 at a time). The command counts HTTP 2xx responses as successes, everything else as failures, writes a per-run failure log and appends a line per processed file to a cumulative log.

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | This page: prompts, request, concurrency, logging, exit behavior |
| [InputFormat.md](InputFormat.md) | The JSONL line shape, what is and is not validated |
| [Troubleshooting.md](Troubleshooting.md) | Every message the command prints and how to reproduce it with the mock API |
| [samples/README.md](samples/README.md) | Runnable inputs, config, and recorded expected output |

## Syntax / how to run

```text
cd <folder that contains (or whose ancestors contain) LocalData/>
dotnet DataMonger.dll
```

Menu numbers depend on the alphabetical list of commands (see [Remarks](#remarks)); choose the entry with the text above. In the sample build it is `5`.

Prompts, in order:

| # | Prompt (as shown) | Accepts | Default |
|---|---|---|---|
| 1 | `Choice:` (main menu) | the number of the command, or `q` | none (re-prompts on invalid input) |
| 2 | `Choice [1-N], or type a full path to a .jsonl file or a folder (...)` | a number from the listed files, **or** a path to one `.jsonl` file, **or** a path to a folder | none |
| 3 | `Choice [1-M]:` (under `Available sites:`) | the number of a `Sites` entry | none |
| 4 | `Send this first request (of N total)? [Enter to continue, Esc to abort]` | Enter = continue, Esc = abort, any other key = continue | continue |
| 5 | `Continue with the remaining N line(s)? [Enter to continue, Esc to abort]` | same | continue |

Notes on the prompts:

* Prompt 2 first lists `*.jsonl` files found **directly** in `LocalData/jsonl/events/Loads` (sorted by full path, case-insensitive). If that folder does not exist, the prompt is instead `JSONL directory not found (...). Enter a full path ...` and only a typed path is accepted. If the folder exists but holds no `.jsonl` file the command prints `[FAIL] No .jsonl files found in ...` and aborts without letting you type a path.
* A typed path may be wrapped in double quotes (they are stripped). A **folder** path selects every `*.jsonl` file under it **recursively** (other extensions are ignored), ordered by full path; the command prints `Found N .jsonl file(s) under ...`.
* A file picked by number or typed as a single file path shows prompts 4 and 5 (the "preview" prompts). A **folder** selection skips prompts 4 and 5 for every file and prints `(folder upload: skipping the first-request/first-response preview prompts for every file)`.
* Prompts 4 and 5 use `Console.ReadKey`. They need a real terminal: with redirected stdin the process crashes with an unhandled `InvalidOperationException` ("Cannot read keys ..."). To script the command with piped stdin use a folder path (see [samples/README.md](samples/README.md)), or drive it through a pseudo-terminal.
* Invalid or blank input at prompt 2 or 3 prints `[FAIL] Invalid choice.` / `[FAIL] Path not found: ...` and the command aborts (`[FAIL] No file(s) selected. Aborting.` / `[FAIL] No site selected. Aborting.`); it does not re-prompt.
* The command is also called programmatically by *Synthesize Session Events* with a ready list of files (`Execute(IReadOnlyList<string>)`); in that mode only the site prompt is shown and the previews are shown for each file.

## Inputs

* One or more JSONL files, see [InputFormat.md](InputFormat.md).
* `appsettings.json` next to `DataMonger.dll`, `Sites` array (`BaseUrl`, `SiteId`, `Username`, `Password`). The command ignores the site the outer loop gives it and asks you to pick one of `Sites` by number.

## Outputs

| Output | Location | Notes |
|---|---|---|
| Console transcript | stdout | Per-file summary line and, with more than one file, a grand total |
| `processed_files.log` | `LocalData/logs/events_upload/processed_files.log` | Appended to, one tab-separated line per file: `<UTC timestamp>\t<path>\tsuccess=N failed=N elapsed=hh:mm:ss.fff[ (reason)]`. Reasons: `could not be read`, `no non-blank lines`, `cancelled at first-response preview` |
| `failures_<yyyyMMdd_HHmmss>.log` | `LocalData/logs/events_upload/` | One file per run (UTC time the run started). Created **only** when a failure is logged. One line per failure: `<timestamp>\tfile=..\tline=N\tstatus=NNN|n/a\treason=...` (reason newline-flattened and cut at 500 chars) |
| `app.log` | `LocalData/logs/app.log` | General application log (this command logs run start, file start/finish, malformed lines, progress, and the HTTP client logs one `Executing REST method: POST <url>` line per request) |

`LocalData/` is resolved as: the folder `LocalData` four levels above the executable's folder if it exists (the repo layout `bin/Debug/net10.0`), otherwise `./LocalData` relative to the current directory.

## Remarks

**Request sent.** For every non-blank line:

| Part | Value |
|---|---|
| Method / URL | `POST {BaseUrl without trailing /}/ver2/{SiteId}/activities` (the same URL for every line; nothing from the record goes into the URL) |
| Auth | HTTP Basic using the chosen site's `Username` / `Password` (set by the HTTP client; the mock API records only that an `Authorization` header was present) |
| Headers | `Accept: application/json`, `Content-Type: application/json` |
| Body | the line, byte for byte, as the file contains it (no reshaping, no added fields) |

**Mock target.** The sample `appsettings.json` points at `DataMonger.MockApi` (`http://localhost:5281`). It answers `202 Accepted` with `{"status":"accepted","requestId":N,"route":"activities"}`, answers `400` for a body that is not valid JSON, and can inject `500` / `429` with `MockApi__FailEveryNth` / `MockApi__ThrottleEveryNth`.

**Ordering within a file.** The first non-blank line is sent alone, before the rest, so its response can be previewed (or, in folder mode, simply so it goes first). The remaining lines (including blank ones, which are skipped) are then processed with `Parallel.ForEachAsync`, `MaxDegreeOfParallelism = 50`. Completion order, and therefore console and failure-log order, is not file order. Files are processed one after another.

**Parallelism.** 50 is the constant `MaxParallelism` in `Program.cs`, passed to every command through `CommandContext.MaxParallelism`; there is no setting to change it.

**Rate limiting and retries.** None. This command uses no `System.Threading.RateLimiting` limiter (that package is used only by *API => Email Conversion Events*), adds no delay, and does not retry: each line is sent exactly once. A `429` or `500` is counted as a failure; a `Retry-After` header is ignored. This was verified against the mock (13 requests received for 13 lines sent, with 429s injected).

**Batching.** There is no batching: one HTTP request per line.

**Response handling.** HTTP status 200-299 is a success; any other status, or a transport error (connection refused, DNS failure), is a failure. For a failure the console shows `[FAIL] L<line> -> <code> <StatusName>` followed by the response body (cut at 500 chars) or the transport error text. The response body of successful requests is discarded (except the first one, shown in the preview).

**Local JSON check.** Before each line after the first is sent, it is parsed with `JObject.Parse`. A line that is not a JSON object is **not sent**; it is counted as failed, printed as `[WARN] Skipping malformed line N: ...` and written to the failure log with `status=n/a`. The first line is handled differently: in preview mode it must parse or the file is aborted (`[FAIL] First line is not valid JSON`, `[ABORT] Upload cancelled.`, counted `(0, 0)`, nothing logged to `processed_files.log`); in folder mode (no previews) it is sent as-is and the server decides.

**Progress.** Every 1,000 completed lines the console prints `  ... N/TOTAL lines processed (ok X, fail Y)`. `TOTAL` counts all lines of the file including blank ones.

**Summary.** After each file: `=== API => Upload JSONL Events (track event) summary ===  Site: <id>   Success: N   Failed: N   Elapsed: hh:mm:ss.fff`. With two or more files also `... grand total ===  Site: <id>   Success: N   Failed: N`, and, if any failure was logged, `Failure detail log: <path>`.

**Resume / processed-files log.** `processed_files.log` is an audit trail only. The command never reads it: re-running the same file sends every line again (the API call is not made idempotent by the tool). To retry only failed lines, build a new file from the `line=` numbers in the failure log.

**Per-site loop and "run once".** The command does not set `RunsPerSite = false`, so `Program.cs` calls it once per `Sites` entry and prints `=== <name> -> site: <SiteId> ===` each time. Because the command shows its own site picker, a static flag lets it run only once per process: later iterations print `[skip] UploadJsonlEvents already ran this session (it picks its own site).` The flag is set before anything can fail, so with several sites an aborted first iteration is not retried for the next site.

**Menu numbering.** `Program.cs` lists commands sorted by `Name`, so numbers change when commands are added.

**Exit code.** `Main` ignores the `(success, failure)` result and the process exits 0 even if every request failed. Only an unhandled exception gives a non-zero exit.

## Examples

* [samples/README.md](samples/README.md) runs this command against the mock API for success, injected 500s, injected 429s, a malformed first line, and Esc at the first-response preview.

## Related

* [UploadJsonlIdentifies](../UploadJsonlIdentifies/Readme.md), [UploadJsonlResources](../UploadJsonlResources/Readme.md), [ApiBulkRunner](../ApiBulkRunner/Readme.md)
* Mock API: `DataMonger.MockApi/Program.cs` (header comment lists routes and settings)
