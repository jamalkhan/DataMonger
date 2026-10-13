# Samples: Anonymous purchases

Inputs are shared with the other commands: use [../../PurchaseKnown/samples/inputs/](../../PurchaseKnown/samples/inputs/)
(`products.csv`, `anonymous.csv`, `appsettings.json`); see [InputFormat](../../PurchaseKnown/InputFormat.md).

| File | Purpose |
|---|---|
| [expected-output/console.txt](expected-output/console.txt) | Successful run: 10 customers, all `202 Accepted` |
| [expected-output/console-with-injected-failures.txt](expected-output/console-with-injected-failures.txt) | Mock with `MockApi__FailEveryNth=4 MockApi__ThrottleEveryNth=5`: 6 OK, 2x 429, 2x 500 (4 failed) |
| [expected-output/mock-requests-trimmed.json](expected-output/mock-requests-trimmed.json) | 2 of the 10 requests as recorded by the mock |

## Reproduce

Scratch layout, mock start (port 5284, optional failure injection) and `DATAMONGER_HOME` isolation are the same as in the
[Known purchases samples](../../PurchaseKnown/samples/README.md#reproduce). Then, against a freshly started mock:

```
cd scratch
printf '2\n' | DATAMONGER_HOME=$PWD/home dotnet app/DataMonger.dll    # 2 = "Anonymous purchases" (verify in the menu)
curl -s http://localhost:5284/mock/requests
```

## What to look for

Customer order, items per order and `order_id` change every run. A failed request is **not retried**: the mock saw exactly
10 requests in the failure run.
