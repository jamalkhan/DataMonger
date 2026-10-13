# Samples: Generate Email Conversion data

Inputs are shared with the purchase commands: [../../PurchaseKnown/samples/inputs/](../../PurchaseKnown/samples/inputs/)
(`products.csv`, `identified.csv`; see [InputFormat](../../PurchaseKnown/InputFormat.md)).

| File | Purpose |
|---|---|
| [expected-output/EmailConversion.jsonl](expected-output/EmailConversion.jsonl) | Real output for seed 13: 10 rows, 33 events (10 sent, 9 delivered, 1 bounced, 8 opened, 3 clicked, 2 purchases). Field reference: [EventSchema](../EventSchema.md) |
| [expected-output/console.txt](expected-output/console.txt) | Console transcript (absolute path replaced by `<scratch>`) |

## Reproduce

Run from a scratch folder so the real `Documents` and `LocalData` folders are untouched. `<built-app>` is the
`DataMonger/bin/Debug/net10.0` output folder; the app resolves `LocalData` four levels above its bin folder if that exists,
otherwise under the current directory.

```
mkdir -p scratch/LocalData scratch/app
cp -R <built-app>/. scratch/app/
cp ../../PurchaseKnown/samples/inputs/{products,identified}.csv scratch/LocalData/
cp ../../PurchaseKnown/samples/inputs/appsettings.json scratch/app/appsettings.json   # points at the mock on port 5284
cd scratch
printf '9\n13\n' | DATAMONGER_HOME=$PWD/home dotnet app/DataMonger.dll
```

Answers: `9` = menu entry "Generate Email Conversion data" (verify the number in the menu), `13` = seed. No mock API is
needed. The output is `scratch/LocalData/EmailConversion.jsonl`.

`DATAMONGER_HOME` relocates the app's per-user home folder (settings and config folders). This command does not use it,
but setting it is a safe habit when scripting the app.

## What to look for

* `cmp scratch/LocalData/EmailConversion.jsonl expected-output/EmailConversion.jsonl` reports no difference (seed
  determinism). Change the seed and the file changes; leave it blank and it differs every run.
* Row order equals `identified.csv` order.
* `grace.hopper` (row 3) has the full chain `email_sent, email_delivered, email_opened, email_clicked, purchase`;
  `alan.turing` bounced and has only `email_sent, email_bounced`.
* In every row `event_time` is non-decreasing and `message_id` is constant.
* The console prompt text runs into the next line in the transcript because piped input is not echoed.
