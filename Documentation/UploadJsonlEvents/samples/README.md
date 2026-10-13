# UploadJsonlEvents samples

| File | Purpose |
|---|---|
| `appsettings.json` | One site: `http://localhost:5281`, site `demo`, user `demo`, password `REPLACE_WITH_PASSWORD`. Copy next to `DataMonger.dll` |
| `input/events_a.jsonl` | 10 valid events |
| `input/events_bad.jsonl` | truncated first line, blank line, non-JSON line, 2 valid lines |
| `expected-output/` | Real output of the runs below (paths shortened; timestamps/elapsed replaced by `<timestamp>`/`<elapsed>`; `mock-requests-*.json` are `GET /mock/requests` sorted by `id` with `at` removed) |

## Setup

```bash
# 1. mock API (from the mock's build output folder); restart it between runs to reset its counter
dotnet DataMonger.MockApi.dll --urls http://localhost:5281
#    failure / throttle variants:  MockApi__FailEveryNth=4 dotnet ...   MockApi__ThrottleEveryNth=3 dotnet ...

# 2. a working folder; the app finds ./LocalData when run from here
mkdir -p work/LocalData/jsonl/events/Loads work/samples
cp -R input work/samples/input
cp input/events_a.jsonl work/LocalData/jsonl/events/Loads/
# the app also needs LocalData/products.csv (header only is fine):
echo "Id,ResourceType,Price,Quantity,ProductName,Category,Subcategory,Size,Url" > work/LocalData/products.csv
cp appsettings.json <folder containing DataMonger.dll>/
cd work
```

The menu number below (`5`) is the position in this build's alphabetical list; check the menu.

## Run 1: single file, success (needs a terminal)

Answers: `5`, `1` (file), `1` (site), Enter (first request), Enter (continue). The preview prompts read single keys, so run it in a real terminal (piped stdin crashes, see [Troubleshooting](../Troubleshooting.md)).
Expect `Success: 10   Failed: 0`. Output: `expected-output/console-single-file.txt`, `expected-output/mock-requests-single-file.json` (10 x `POST /ver2/demo/activities`, status 202, `hadAuthorizationHeader: true`).

## Run 2: folder, injected 500s (works with piped stdin)

```bash
MockApi__FailEveryNth=4 dotnet DataMonger.MockApi.dll --urls http://localhost:5281   # other shell
printf '5\nsamples/input\n1\n' | dotnet DataMonger.dll
```

Folder mode skips the previews. Expect both files processed, grand total `Success: 9   Failed: 5` (in the recorded run: 2 x 500 in `events_a`, 400 + 500 + local skip in `events_bad`). Which lines get the 500 varies from run to run; the count does not.
Output: `console-folder-fail-every-4th.txt`, `mock-requests-folder-fail-every-4th.json`, `failures.log` (the per-run `LocalData/logs/events_upload/failures_<timestamp>.log`), `processed_files.log`.

## Run 3: folder, injected 429s

Same as run 2 with `MockApi__ThrottleEveryNth=3`. Expect `[FAIL] ... 429 TooManyRequests`, no retries (13 requests for 13 sent lines). Output: `console-folder-throttle-every-3rd.txt`, `mock-requests-folder-throttle-every-3rd.json`.

## Run 4: Esc at the first-response preview

Terminal: `5`, path `samples/input/events_a.jsonl`, `1`, Enter, then Esc. Only 1 request is sent and `processed_files.log` records `(cancelled at first-response preview)`. Output: `console-esc-at-first-response.txt`.

## Run 5: malformed first line, preview mode

Terminal: `5`, path `samples/input/events_bad.jsonl`, `1`. Output: `console-malformed-first-line.txt`.

## Resume

There is no resume: `processed_files.log` is not read back. Re-running a file re-sends every line.
