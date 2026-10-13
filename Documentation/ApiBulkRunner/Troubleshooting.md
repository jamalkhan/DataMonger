# ApiBulkRunner troubleshooting

Reproduce with the mock on port 5281 and [samples/input/massive_apis.jsonl](samples/input/massive_apis.jsonl). The mock's counters are global, so which calls get an injected 500/429 varies between runs (the count is stable); restart the mock between runs.

| What you see | Meaning | Reproduce |
|---|---|---|
| `[FAIL] <path> not found.` | `LocalData/massive_apis.jsonl` missing | run without the file |
| `[WARN] Skipping line N: missing 'url'.` | no `url` | sample line 9 |
| `[WARN] Skipping line N: unknown verb 'X'.` | verb not in `HttpVerbs` | sample line 8 (`FETCH`) |
| `[WARN] Skipping malformed line N: ...` | not JSON | sample line 10 |
| `[FAIL] No valid API calls parsed from massive_apis.jsonl.` | every line was skipped | file of bad lines only |
| `[FAIL] L1 GET file:///ver2/demo/ping -> 0 0` / `The 'file' scheme is not supported.` | relative URL with a leading `/` on macOS/Linux | put `"url":"/ver2/{site}/ping"` in the file |
| `[FAIL] L11 GET http://localhost:5281/ver2/demo/missing-route -> 404 NotFound` | route does not exist on the mock (empty body) | sample line 11 ([recording](samples/expected-output/console-success.txt)) |
| `[FAIL] ... -> 500 InternalServerError` + `injected_failure` | server error, no retry | `MockApi__FailEveryNth=3` ([recording](samples/expected-output/console-fail-every-3rd.txt)) |
| `[FAIL] ... -> 429 TooManyRequests` + `rate_limited` | throttled; no limiter, no retry | `MockApi__ThrottleEveryNth=2` ([recording](samples/expected-output/console-throttle-every-2nd.txt)) |
| `-> 0 0` with a connection error text | no HTTP response (mock stopped / wrong `BaseUrl`) | stop the mock |
| `=== ... -> site: X ===` printed more than once | one run per `Sites` entry | several `Sites` |

Notes: skipped lines are not failures (the `ok`/`fail` totals cover valid calls only); the process exit code is 0 regardless; with real targets 1,000 concurrent requests can exhaust sockets or trigger throttling, there is no way to lower the cap without editing `BulkParallelism` in the source.

See also [Readme.md](Readme.md), [InputFormat.md](InputFormat.md).
