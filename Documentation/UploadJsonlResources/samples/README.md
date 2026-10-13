# UploadJsonlResources samples

| File | Purpose |
|---|---|
| `appsettings.json` | site `demo` at `http://localhost:5281`, user `demo`, password `REPLACE_WITH_PASSWORD` |
| `input/products.jsonl` | 10 Acme Outfitters products |
| `input/products_bad.jsonl` | 6 lines: valid, missing id, blank, id needing URL escaping, truncated JSON, numeric id |
| `expected-output/` | Recorded runs (paths shortened, `<elapsed>`; `mock-requests-*.json` = `GET /mock/requests` sorted by id, `at` removed) |

## Setup (no TTY needed)

```bash
dotnet DataMonger.MockApi.dll --urls http://localhost:5281       # restart between runs
mkdir -p work/LocalData/jsonl/products
cp input/*.jsonl work/LocalData/jsonl/products/
echo "Id,ResourceType,Price,Quantity,ProductName,Category,Subcategory,Size,Url" > work/LocalData/products.csv
cp appsettings.json <folder with DataMonger.dll>/ ; cd work
printf '7\n1\n1\n' | dotnet <path>/DataMonger.dll        # 7 = menu number of this command; 1 = products.jsonl; 1 = site
```

| Run | Mock | Stdin | Expect | Files |
|---|---|---|---|---|
| success | default | `7`,`1`,`1` | `Success: 10   Failed: 0`; 10 `PUT /ver2/demo/resources/<id>` with 200 | `console-success.txt`, `mock-requests-success.json` |
| 500s | `MockApi__FailEveryNth=4` | same | 2 failures (`500`, which lines vary) | `console-fail-every-4th.txt`, `mock-requests-fail-every-4th.json` |
| 429s | `MockApi__ThrottleEveryNth=3` | same | 3 failures (`429`), no retries (10 requests) | `console-throttle-every-3rd.txt`, `mock-requests-throttle-every-3rd.json` |
| bad lines | default | `7`,`2`,`1` | `Success: 3   Failed: 2`; 3 requests incl. `/resources/LAMP%201%2F2` and `/resources/77` | `console-bad-file.txt`, `mock-requests-bad-file.json` |

No resume, failure log or processed-files log exists for this command.
