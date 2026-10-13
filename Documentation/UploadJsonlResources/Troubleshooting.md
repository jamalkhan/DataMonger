# UploadJsonlResources troubleshooting

Restart the mock between reproductions; the failure/throttle settings count requests globally, so which lines fail varies between runs (how many fail does not).

| What you see | Meaning | Reproduce |
|---|---|---|
| `[skip] UploadJsonlResources already ran this session` | Second `Sites` entry | two `Sites` |
| `[FAIL] No .jsonl files found in ...` / `JSONL directory not found` | `LocalData/jsonl/products` empty / missing | |
| `[FAIL] Invalid choice.` + `[FAIL] No file selected. Aborting.` | Number out of range, or a typed path (not accepted when the folder exists) | type `9` or a path |
| `[WARN] Skipping malformed line N: ...` | Not a JSON object; failed locally | [console-bad-file.txt](samples/expected-output/console-bad-file.txt) (line 5) |
| `[WARN] Skipping line N: missing 'resource_id'.` | No/blank `resource_id`; failed locally | same file, line 2 |
| `[FAIL] L2 resource_id=TENT-4P -> 500 InternalServerError` | server error, no retry | `MockApi__FailEveryNth=4`, [recording](samples/expected-output/console-fail-every-4th.txt) |
| `[FAIL] L? resource_id=... -> 429 TooManyRequests` | throttled, no retry/backoff | `MockApi__ThrottleEveryNth=3`, [recording](samples/expected-output/console-throttle-every-3rd.txt) |
| `[FAIL] ... -> 0 0` + error text | no HTTP response (mock down, wrong `BaseUrl`) | stop the mock |
| Unhandled exception | file unreadable (`File.ReadAllLinesAsync` not guarded) | |

Quirks: failures exist only on the console; the exit code is 0 regardless; the first line is sent in parallel like the rest (no preview); only files directly in `LocalData/jsonl/products` can be picked.

See also [Readme.md](Readme.md).
