# UploadJsonlIdentifies troubleshooting

Restart the mock between reproductions: `MockApi__FailEveryNth` / `MockApi__ThrottleEveryNth` count requests globally, so which lines fail is not deterministic with 50 requests in flight (how many fail is).

| What you see | Meaning | Reproduce |
|---|---|---|
| `Unhandled exception. System.InvalidOperationException: Cannot read keys ...` | A preview prompt ran with redirected stdin. | `printf '6\n1\n1\n' \| dotnet DataMonger.dll` |
| `[skip] UploadJsonlIdentifies already ran this session` | Second `Sites` entry; runs once per process. | Two `Sites` |
| `[FAIL] No .jsonl files found in ...` / `JSONL directory not found` | Empty / missing `LocalData/jsonl/profile`. | |
| `[FAIL] Invalid choice.`, `[FAIL] Path not found: ...`, then `[FAIL] No file(s) selected. Aborting.` or `[FAIL] No site selected. Aborting.` | Bad input; no re-prompt. | Type `9` |
| `[FAIL] First line is not valid JSON: ...` / `[ABORT] Upload cancelled.` | First non-blank line does not parse. | A file starting with a bad line |
| `[WARN] Skipping malformed line N: ...` | Line N not a JSON object; never sent; counted failed. | `samples/input/identifies_bad.jsonl` (line 2) |
| `[FAIL] L5 -> 500 InternalServerError` + `injected_failure` | Server error, no retry. | `MockApi__FailEveryNth=4` ([recording](samples/expected-output/console-folder-fail-every-4th.txt)) |
| `[FAIL] L7 -> 429 TooManyRequests` + `rate_limited` | Throttled; no retry/backoff, `Retry-After` ignored. | `MockApi__ThrottleEveryNth=3` ([recording](samples/expected-output/console-single-file-throttle-every-3rd.txt)) |
| `[FAIL] L<n> -> 0 0` plus an error text | No HTTP response (mock down / wrong `BaseUrl`). | Stop the mock |
| `[FAIL] File has no non-blank lines to send.` | Empty file. | |
| `[ABORT] Remaining lines not sent.` | Esc at the second prompt; the first line was already sent. | |
| Unhandled exception with an IO message | File unreadable (not caught in this command). | |

## Quirks

* A failure of the first non-blank line is always printed as `L1`, even if it is not on file line 1.
* Failures are only on the console: nothing is written to disk. Redirect or `tee` the transcript if you need it, and note failures by line number to build a retry file.
* Exit code is 0 regardless of failures.

See also [Readme.md](Readme.md).
