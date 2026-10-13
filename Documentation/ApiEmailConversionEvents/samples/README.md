# Samples: API => Email Conversion Events

Chain: run [Generate Email Conversion data](../../GenerateEmailConversions/samples/README.md) first (seed 13), then this
command against the mock API on port 5284.

| File | Purpose |
|---|---|
| [inputs/EmailConversion.jsonl](inputs/EmailConversion.jsonl) | The seed-13 generator output (identical to the generator's `expected-output`), 10 rows / 33 events |
| [inputs/EmailConversion.edge-cases.jsonl](inputs/EmailConversion.edge-cases.jsonl) | Hand-written: blank line, non-JSON line, row without email, event without `event_type`, row with two events, row without `events` |
| [expected-output/console.txt](expected-output/console.txt) | Upload of the 33 events with delay `0`: all succeed |
| [expected-output/mock-requests-summary.json](expected-output/mock-requests-summary.json) | One summary object per request the mock received (id, path, status, auth header present, `event_type`, `recipient_email`); bodies omitted |
| [expected-output/mock-requests-sample-bodies.json](expected-output/mock-requests-sample-bodies.json) | Full recorded body of the first bounced, clicked and purchase request |
| [expected-output/console-edge-cases-with-injected-failures.txt](expected-output/console-edge-cases-with-injected-failures.txt) | Edge-case file against a mock failing every 3rd and throttling every 4th request |

## Reproduce

Scratch layout and build as in the [generator sample](../../GenerateEmailConversions/samples/README.md#reproduce)
(`scratch/app` = copy of the built app with `appsettings.json` from
[../../PurchaseKnown/samples/inputs/](../../PurchaseKnown/samples/inputs/), `scratch/LocalData/products.csv`).

```
# 1. mock API (from the MockApi build output)
dotnet DataMonger.MockApi.dll --urls http://localhost:5284 &

# 2. input
cp inputs/EmailConversion.jsonl scratch/LocalData/EmailConversion.jsonl

# 3. upload: menu 4, delay answer 0
cd scratch
printf '4\n0\n' | DATAMONGER_HOME=$PWD/home dotnet app/DataMonger.dll

# 4. inspect what the mock received
curl -s http://localhost:5284/mock/stats
curl -s http://localhost:5284/mock/requests
```

Edge-case run: stop the mock, restart it with
`MockApi__FailEveryNth=3 MockApi__ThrottleEveryNth=4 dotnet DataMonger.MockApi.dll --urls http://localhost:5284`,
copy `inputs/EmailConversion.edge-cases.jsonl` over `scratch/LocalData/EmailConversion.jsonl` and run the same command.

Answering `4` then a blank line uses the default 0-120 s pauses (the run then takes minutes).

## What to look for

* Success run: 33 requests to `/ver2/demo/activities`, all 202; per `event_type`: 10 sent, 9 delivered, 8 opened,
  3 clicked, 2 purchase, 1 bounced. Final line `Success: 33   Failed: 0`. Request ids and the order of lines vary
  between runs because rows are uploaded in parallel; elapsed time varies.
* Edge cases: `[WARN] Skipping malformed line 5` (the blank line counts), `Loaded 6 rows (6 events)`, the event without
  `event_type` is labelled `(unknown event)`, and the summary shows `Success: 3   Failed: 3`. With injected failures
  the exact events that fail depend on request arrival order.
* The progress line is rewritten in place; the transcripts show its final state only.
