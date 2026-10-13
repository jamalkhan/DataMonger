# ApiBulkRunner

Menu entry: **`API => Bulk Runner (stress test)`**
Source: `DataMonger/Commands/ApiBulkRunner.cs`

A stress-test runner. It reads `LocalData/massive_apis.jsonl`, where each line fully describes one HTTP call (`verb`, `url`, optional `payload`), and fires all calls with a concurrency cap of **1,000**, no delay, and no rate limit. It prints a live progress bar and each failure.

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | Prompts, URL resolution, concurrency, output |
| [InputFormat.md](InputFormat.md) | The line format: `verb`, `url`, `payload` |
| [Troubleshooting.md](Troubleshooting.md) | Messages and reproductions |
| [samples/README.md](samples/README.md) | Inputs and recorded output |

## Syntax / how to run

```text
dotnet DataMonger.dll      # from a folder with (or under) LocalData/
```

Choose `API => Bulk Runner (stress test)` (number `3` in the sample build). **There is only one prompt, the main menu `Choice:`.** No file picker (the input path is fixed), no site picker, no confirmation. Piped stdin works: `printf '3\n' | dotnet DataMonger.dll`.

## Inputs

* `LocalData/massive_apis.jsonl` (`LocalData` resolved as the folder four levels above the executable's folder if it exists, else `./LocalData`). If missing: `[FAIL] <path> not found.` and the command ends.
* `appsettings.json` `Sites`. **Which site is used:** `RunsPerSite` is not overridden (true), so `Program.cs` runs the command **once per `Sites` entry, in order**, printing `=== API => Bulk Runner (stress test) -> site: <SiteId> ===` before each. For each run the site's `BaseUrl`, `SiteId`, `Username`, `Password` are used for relative URLs, the `{site}` token, and Basic auth. All lines are re-read and fired again for every site (so with 2 sites the file is sent twice). Absolute URLs in the file ignore the site's `BaseUrl` but are still sent with that site's credentials.

Format: [InputFormat.md](InputFormat.md).

## Outputs

Console only (no files written):

```text
=== API => Bulk Runner (stress test) -> site: demo ===
[WARN] Skipping line 8: unknown verb 'FETCH'.
Loaded 8 API calls from LocalData/massive_apis.jsonl
Firing with MaxDegreeOfParallelism = 1000. No delay, no rate limit.
Progress refreshes every ~3 seconds. Failures will scroll past the progress line.

[FAIL] L11 GET http://localhost:5281/ver2/demo/missing-route -> 404 NotFound
       
[##############################] 8/8 (100.0%)  ok 7  fail 1  elapsed 00:00:00  ETA 00:00:00
```

The progress line is rewritten with `\r` every ~3 seconds by a timer and once more at the end, so runs shorter than 3 s show only the final line. Fields: bar, `done/total (pct)`, `ok`, `fail`, `elapsed hh:mm:ss`, `ETA hh:mm:ss` (or `??:??:??` before anything completes). Per-call failure: `[FAIL] L<line> <VERB> <resolved url> -> <code> <StatusName>` then the response body (or transport error text, or `(no response body)`), cut at 500 chars. Status `0` means no HTTP response.

## Remarks

**Request per line.** Verb and URL from the line; body is the `payload` (empty string if absent); HTTP Basic auth with the current site's credentials; headers `Accept: application/json`, `Content-Type: application/json`. Responses 200-299 count as ok; everything else (including 404) is a failure.

**Concurrency and rate limiting.** `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 1000` (the private constant `BulkParallelism`, deliberately independent of the global 50). There is no `System.Threading.RateLimiting` limiter, no delay and no retry; a `429` is a failure. Call completion order is not file order. Use it only against targets you own.

**Parsing is done up front.** The whole file is read and validated first; invalid lines are skipped with a warning and do not count as failures. Only after parsing does `Loaded N API calls` print. If zero valid calls: `[FAIL] No valid API calls parsed from massive_apis.jsonl.`

**Unix gotcha: leading slash.** Relative URLs are resolved with `Uri.TryCreate(url, UriKind.Absolute, ...)` first. On macOS/Linux a string such as `/ver2/{site}/ping` is a valid absolute *file* URI, so it is used as `file:///ver2/demo/ping` and every such call fails with status `0`: `The 'file' scheme is not supported.` Write relative URLs **without** a leading slash (`ver2/{site}/ping`) or use absolute `http://` URLs. (On Windows the leading-slash form works.) The samples use the slash-less form.

**Exit / resume.** The `(ok, failed)` result is not used for the process exit code (always 0). No log files, no resume; every run re-sends everything.

## Examples

[samples/README.md](samples/README.md): 8 valid calls (GET/POST/PUT/DELETE) plus bad lines, with 500 and 429 injection.

## Related

[UploadJsonlEvents](../UploadJsonlEvents/Readme.md), [UploadJsonlIdentifies](../UploadJsonlIdentifies/Readme.md), [UploadJsonlResources](../UploadJsonlResources/Readme.md)
