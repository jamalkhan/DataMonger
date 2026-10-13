# ApiBulkRunner samples

| File | Purpose |
|---|---|
| `appsettings.json` | site `demo` at `http://localhost:5281`, user `demo`, password `REPLACE_WITH_PASSWORD` |
| `input/massive_apis.jsonl` | 11 lines: 7 valid calls (GET/POST/PUT/DELETE, object and string payloads, default verb, one absolute URL), 3 lines the runner skips (unknown verb, missing url, not JSON), 1 valid call to an unknown route (404) |
| `expected-output/` | Recorded runs; `mock-requests-*.json` = `GET /mock/requests` sorted by id, `at` removed |

## Setup (piped stdin works)

```bash
dotnet DataMonger.MockApi.dll --urls http://localhost:5281       # restart between runs
mkdir -p work/LocalData
cp input/massive_apis.jsonl work/LocalData/
echo "Id,ResourceType,Price,Quantity,ProductName,Category,Subcategory,Size,Url" > work/LocalData/products.csv
cp appsettings.json <folder with DataMonger.dll>/ ; cd work
printf '3\n' | dotnet <path>/DataMonger.dll        # 3 = menu number of this command
```

| Run | Mock | Expect | Files |
|---|---|---|---|
| success | default | `Loaded 8 API calls`, `ok 7  fail 1` (the 404 route), 3 warnings; mock receives 7 requests (GET ping 202, POST identify 202, PUT x2 200, POST activities x2 202, DELETE ping 202) | `console-success.txt`, `mock-requests-success.json` |
| 500s | `MockApi__FailEveryNth=3` | extra `500` failures (`ok 5  fail 3` recorded) | `console-fail-every-3rd.txt`, `mock-requests-fail-every-3rd.json` |
| 429s | `MockApi__ThrottleEveryNth=2` | `429` failures (`ok 4  fail 4` recorded) | `console-throttle-every-2nd.txt`, `mock-requests-throttle-every-2nd.json` |

The `\r` progress line is flattened to its own line in the recordings. Which calls fail under injection varies between runs. No resume, no log files.
