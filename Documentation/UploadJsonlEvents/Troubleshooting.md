# UploadJsonlEvents troubleshooting

All reproductions use the mock API on port 5281 and the files in [samples/](samples/README.md). Restart the mock between runs so the request counter starts at 1: `MockApi__FailEveryNth` and `MockApi__ThrottleEveryNth` act on a global request counter (every Nth request received), so which *lines* fail varies run to run when 50 requests are in flight, but how many fail does not.

| What you see | Meaning | Reproduce |
|---|---|---|
| `Unhandled exception. System.InvalidOperationException: Cannot read keys when ... console input has been redirected` | A preview prompt (`Send this first request`) ran with piped stdin. | Pipe answers and choose a single file. Use a folder path or a real terminal. |
| `[skip] UploadJsonlEvents already ran this session` | Second `Sites` entry; the command runs once per process. | Configure two `Sites`. |
| `[FAIL] No .jsonl files found in <dir>.` | `LocalData/jsonl/events/Loads` exists but is empty. Aborts (you are not offered a typed path). | Create the empty folder. |
| `JSONL directory not found (...)` | Loads folder missing; type a path. | Run from a folder with no `LocalData/jsonl/events/Loads`. |
| `[FAIL] Path not found: ...`, `[FAIL] Invalid choice.` | Typed path wrong / blank input. | Type a nonexistent path. |
| `[FAIL] No .jsonl files found under <path> (searched recursively).` | Folder has no `.jsonl`. | Type an empty folder. |
| `[FAIL] Invalid choice.` + `[FAIL] No site selected. Aborting.` | Site number out of range or not a number. | Type `9`. |
| `[FAIL] First line is not valid JSON: ...` then `[ABORT] Upload cancelled.` | Preview mode and the first non-blank line does not parse. See [expected-output/console-malformed-first-line.txt](samples/expected-output/console-malformed-first-line.txt). | Pick `samples/input/events_bad.jsonl` by path in a terminal. |
| `[WARN] Skipping malformed line N: ...` | Line N not a JSON object; not sent; counted failed; in failure log with `status=n/a`. | Run the sample folder (`events_bad.jsonl`, line 4). |
| `[FAIL] L1 -> 400 BadRequest` with `invalid_json` | Folder mode sends the truncated first line unchecked; the mock rejects it. | Run the sample folder: `events_bad.jsonl` line 1. |
| `[FAIL] L7 -> 500 InternalServerError` ... `injected_failure` | Server error. Not retried. | `MockApi__FailEveryNth=4`. See [console-folder-fail-every-4th.txt](samples/expected-output/console-folder-fail-every-4th.txt). |
| `[FAIL] L3 -> 429 TooManyRequests` ... `rate_limited` | Throttled. Not retried, `Retry-After` ignored, no backoff. Lower the load by another route (split the file) and re-send the failed lines. | `MockApi__ThrottleEveryNth=3`. See [console-folder-throttle-every-3rd.txt](samples/expected-output/console-folder-throttle-every-3rd.txt). |
| `[FAIL] L5 -> 0 0` with the transport error text (status 0) | Transport failure (mock not running, wrong `BaseUrl`). | Stop the mock. |
| `[FAIL] Could not read file: ...` | `File.ReadAllLinesAsync` failed (permissions, file removed). Logged to `processed_files.log` as `(could not be read)`. | Delete a file after listing. |
| `[FAIL] File has no non-blank lines to send.` | Empty/blank file; logged as `(no non-blank lines)`. | Empty `.jsonl` file. |
| `[ABORT] Remaining lines not sent.` | You pressed Esc at the first-response preview; the first line was already sent. | Esc at the second prompt. See [console-esc-at-first-response.txt](samples/expected-output/console-esc-at-first-response.txt). |

## Quirks to be aware of

* If the **first non-blank line** fails, its failure is printed and logged as `L1` / `line=1` even when it is not on file line 1 (for example after leading blank lines).
* A failure counted for a malformed line is a *local* skip, so the API never saw that line.
* The process exit code is 0 regardless of failures; check the summary line and `failures_*.log`.
* `processed_files.log` records that a file was processed; it does not prevent re-processing.

## See also

[Readme.md](Readme.md), [InputFormat.md](InputFormat.md)
