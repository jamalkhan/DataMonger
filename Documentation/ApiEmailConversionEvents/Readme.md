# API => Email Conversion Events

Uploads the file written by [Generate Email Conversion data](../GenerateEmailConversions/Readme.md) to the activities
endpoint, **one HTTP request per event**, replaying each recipient's events in order with random real-time pauses
between them to simulate user behavior (the pauses are configurable and can be skipped). Shows a live progress bar,
prints failures as they happen and ends with a summary line.

## Contents

* [samples/](samples/README.md) - chained run, edge cases and failure injection
* [Event schema](../GenerateEmailConversions/EventSchema.md) - what the events in the file look like
* [Input CSV formats](../PurchaseKnown/InputFormat.md) (relevant to the generator that produces this command's input)

## Syntax / how to run

Menu entry text: `API => Email Conversion Events` (class `ApiEmailConversionEvents`; number `4` in the reference build).

| Prompt | Default | Accepted |
|---|---|---|
| `Choice:` | none (re-asked on invalid input) | menu number, or `q` |
| `Max random delay between events in seconds [120] (0 = no delay):` | `120` (blank accepts it) | a non-negative number of seconds, fractions allowed (`2.5`); `0` disables the pauses. Negative, non-numeric, `NaN` and `Infinity` are rejected and the prompt repeats |

The delay prompt is asked after the input file has been found. With the default of 120 the behavior is the same as before
the prompt existed (uniform random pause of 0 to 120 s). The command runs **once per configured site**, uploading the same
file to each site in turn (and asking the delay question once per site).

```
printf '9\n13\n' | dotnet DataMonger.dll    # generate (seeded)
printf '4\n0\n'  | dotnet DataMonger.dll    # upload with no delays
```

## Inputs

`LocalData/EmailConversion.jsonl` ([schema](../GenerateEmailConversions/EventSchema.md)). Per line the command reads only:

| Path | Use |
|---|---|
| `recipient.email` | label in failure messages; `(unknown)` if absent |
| `events` (array) | the bodies to POST; missing or not an array gives zero events for that row |
| `events[].event_type` | label in failure messages; `(unknown event)` if absent or not a string |

Other fields are ignored. Blank lines are skipped silently. A line that is not valid JSON prints
`[WARN] Skipping malformed line <n>: <message>` (`n` counts blank lines too) and is skipped; only JSON syntax errors are
caught. If the file is missing: `[FAIL] <path> not found. Run 'Generate Email Conversion data' first.` and the command
returns (0, 1) without prompting for the delay or sending anything.

Start-up lines: `Loaded R rows (E events) from <path>`, then `Posting with random 0-<max>s delays between events to simulate
user behavior.` (or `Posting with no delay between events.`) and a note about progress refresh.

## What is sent

For each element of `events`: `POST {BaseUrl}/ver2/{SiteId}/activities`, `Accept` and `Content-Type: application/json`,
credentials from the site config, and the **event object serialized verbatim** (compact JSON) as the body. **There is no
wrapper**: the body is the bare event, for example `{"event_id":...,"event_type":"email_clicked",...}`. The command does
not modify, validate or re-time the event; the `event_time` values are those written by the generator, not "now". The
route is the generic activities endpoint used by the other commands; the [mock API](../MockApi/Readme.md) accepts any JSON
body on it, and a real API would need to accept this neutral event shape (or a transform in between).
[samples/expected-output/mock-requests-sample-bodies.json](samples/expected-output/mock-requests-sample-bodies.json) shows
recorded bodies.

## Concurrency, pacing and rate limits

| Aspect | Behavior |
|---|---|
| Across rows | `Parallel.ForEachAsync`, `MaxDegreeOfParallelism = 50` (`Program.cs`) |
| Within a row | strictly sequential, in file order |
| Delay | before every event except the first of a row: uniform random 0 to *max* seconds (*max* from the prompt, default 120; `0` means no sleep at all). Unseeded `Random.Shared`; not tied to the payload timestamps |
| Global limiter | token bucket, 50,000 requests per minute (833 per second), unbounded queue. It only smooths bursts |
| Retries | none. A failed event does **not** stop later events of the same row |

With the default delay, total time is roughly the longest row, about (events - 1) x 60 s on average per row, so a
10-row sample takes minutes. Use `0` for demos and tests. Ctrl+C is not handled (the process is terminated).

## Success / failure counting and console output

* 2xx is success; anything else (including 429 and transport errors) is a failure.
* Progress: every ~3 seconds a timer rewrites one line (carriage return):
  `[####........] done/total (pct%)  elapsed hh:mm:ss  ETA hh:mm:ss`, plus a final refresh at the end.
* Failure: a line `[FAIL] <recipient email> <event_type> -> <code> <StatusName>` followed by the first 500 characters of
  the response body, indented. `<event_type>` is the event's `event_type` string, else `(unknown event)`.
* Final line: `=== API => Email Conversion Events summary ===  Success: S   Failed: F   Elapsed: hh:mm:ss.fff`. The returned
  tuple (S, F) is still ignored by `Program.cs`, so the process exit code is 0 either way.

## Outputs

No files. REST-client diagnostics go to `LocalData/logs/app.log`. Nothing is written back to the JSONL, so the command is
**not idempotent**: running it twice sends every event twice.

## Remarks

* Changes from earlier versions: the event name in failure messages now comes from `event_type` (the old lookup used a field
  the generator never wrote), a summary line is printed, and the inter-event delay is a prompt. The default behavior
  (0-120 s random pauses) is unchanged.
* The mock records the bodies it receives (`GET /mock/requests`); see the samples for per-type counts.

## Examples

[samples/README.md](samples/README.md): uploading 33 events of the seeded sample and an edge-case file with injected 429/500 failures.

## Related

[Generate Email Conversion data](../GenerateEmailConversions/Readme.md) | [Known purchases](../PurchaseKnown/Readme.md) | [Mock API](../MockApi/Readme.md)
