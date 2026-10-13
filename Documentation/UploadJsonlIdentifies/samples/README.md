# UploadJsonlIdentifies samples

| File | Purpose |
|---|---|
| `appsettings.json` | site `demo` at `http://localhost:5281`, user `demo`, password `REPLACE_WITH_PASSWORD` |
| `input/identifies.jsonl` | 10 invented users (Ada Lovelace, Alan Turing, ...) |
| `input/identifies_bad.jsonl` | 3 lines, line 2 truncated |
| `expected-output/` | Real recorded output (paths shortened, elapsed replaced by `<elapsed>`; `mock-requests-*.json` = `GET /mock/requests` sorted by id, `at` removed) |

## Setup

```bash
dotnet DataMonger.MockApi.dll --urls http://localhost:5281           # restart between runs
mkdir -p work/LocalData/jsonl/profile work/samples
cp input/identifies.jsonl work/LocalData/jsonl/profile/
cp -R input work/samples/input
echo "Id,ResourceType,Price,Quantity,ProductName,Category,Subcategory,Size,Url" > work/LocalData/products.csv
cp appsettings.json <folder with DataMonger.dll>/ ; cd work
dotnet <path>/DataMonger.dll
```

The previews need a real terminal (single-key reads). The recorded runs were driven through a pseudo-terminal that answered the prompts below.

## Run 1: success

Answers: `6`, `1`, `1`, Enter, Enter. Expect `Success: 10   Failed: 0`. Files: `console-single-file.txt`, `mock-requests-single-file.json` (10 x `POST /ver2/demo/subscribers/identify`, 202).

## Run 2: folder with both files, injected 500s

Mock started with `MockApi__FailEveryNth=4`. Answers: `6`, `samples/input`, `1`, then Enter, Enter for each of the two files (previews are asked per file). Expect per-file `Success: 8 Failed: 2` and `Success: 1 Failed: 2` (one 500, one local malformed skip), grand total `Success: 9   Failed: 4`. Files: `console-folder-fail-every-4th.txt`, `mock-requests-folder-fail-every-4th.json`.

## Run 3: injected 429s

Mock with `MockApi__ThrottleEveryNth=3`, answers as run 1 with the file `samples/input/identifies.jsonl`. Expect `429 TooManyRequests` on some lines, no retries. Files: `console-single-file-throttle-every-3rd.txt`, `mock-requests-single-file-throttle-every-3rd.json`.

There is no resume and no failure/processed log for this command.
