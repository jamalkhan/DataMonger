# Samples: Known (identified) purchases

| File | Purpose |
|---|---|
| [inputs/products.csv](inputs/products.csv) | 10 invented products (shared by all purchase / email commands) |
| [inputs/identified.csv](inputs/identified.csv) | 10 invented identified customers (`first_name,last_name,email,user_id`) |
| [inputs/anonymous.csv](inputs/anonymous.csv) | 10 invented anonymous customers (used by [Anonymous purchases](../../PurchaseAnonymous/Readme.md)) |
| [inputs/appsettings.json](inputs/appsettings.json) | One site pointing at the mock API (`http://localhost:5284`, SiteId `demo`) |
| [expected-output/console.txt](expected-output/console.txt) | Console transcript of a successful run |
| [expected-output/console-with-injected-failures.txt](expected-output/console-with-injected-failures.txt) | Run against a mock that fails every 4th and throttles every 5th request: 3 customers succeed, 7 fail (4 at identify, 3 at the purchase) |
| [expected-output/mock-requests-trimmed.json](expected-output/mock-requests-trimmed.json) | Requests the mock recorded in the successful run: the first 2 identify and first 2 purchase requests in full (the other 16 omitted) |

## Reproduce

Use a scratch folder so the repository's own `LocalData` and your `Documents` folder are untouched (`<built-app>` is the
`DataMonger/bin/Debug/net10.0` folder; `<mock>` the `DataMonger.MockApi/bin/Debug/net10.0` folder):

```
mkdir -p scratch/LocalData scratch/app
cp -R <built-app>/. scratch/app/
cp inputs/{products,identified,anonymous}.csv scratch/LocalData/
cp inputs/appsettings.json scratch/app/appsettings.json

(cd <mock> && dotnet DataMonger.MockApi.dll --urls http://localhost:5284) &
# failure run instead: (cd <mock> && MockApi__FailEveryNth=4 MockApi__ThrottleEveryNth=5 dotnet DataMonger.MockApi.dll --urls http://localhost:5284) &

cd scratch
printf '10\n' | DATAMONGER_HOME=$PWD/home dotnet app/DataMonger.dll      # Known (identified) purchases
curl -s http://localhost:5284/mock/stats
curl -s http://localhost:5284/mock/requests
```

Menu choice `10` is "Known (identified) purchases" in the reference build; numbering is alphabetical, so check the menu.
For [Anonymous purchases](../../PurchaseAnonymous/Readme.md) answer `2` against a freshly started mock. The transcripts replace
the absolute scratch path with `<scratch>` where one is printed.

## What to look for

* Ten `[OK]` lines `... identify+activity (N items) -> 202/202`; order varies run to run (parallel).
* Item counts (1-3), product choice and `order_id` values differ every run (unseeded randomness).
* In the failure run an identify failure prints `... identify -> <status>` and no purchase is sent for that customer
  (identify count 10, activity count < 10 in the mock stats); `Success + Failed` always equals the number of customers.
