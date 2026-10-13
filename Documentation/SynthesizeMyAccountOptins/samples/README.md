# Samples: Synthesize MyAccount Optin Events

Everything here is invented (Acme Outfitters, `example.com`, fictional ids).

## Files

| File | Purpose |
|---|---|
| [profiles.jsonl](profiles.jsonl) | 10 profiles, identical to the one in the [session-events sample](../../SynthesizeSessionEvents/samples/README.md): three generated profiles (`CUST_1001`..`CUST_1003`) plus seven small ones for edge cases (Tokyo time zone, no time zone, unknown zone, phone only, no user id, all consent withdrawn, flat legacy profile). |
| [expected-output/marketing_opt_in_events.jsonl](expected-output/marketing_opt_in_events.jsonl) | The 3 events the command wrote with seed 21 and anchor date 2026-10-01. |
| [expected-output/preview_event.json](expected-output/preview_event.json) | The pretty-printed preview (the first profile's event). |

The real output files carry a run timestamp (`marketing_opt_in_events_<yyyyMMdd_HHmmss>.jsonl`); the committed copies do not.

## Reproduce

1. Build and create a scratch folder (the app needs a `LocalData/products.csv` at startup; this command does not use it):

   ```sh
   dotnet build DataMonger.slnx
   mkdir -p work/LocalData && cd work
   printf 'Id,ResourceType,Price,Quantity,ProductName,Category,Subcategory,Size,Url\n' > LocalData/products.csv
   export DATAMONGER_HOME="$PWD/home"
   ```

2. Run, piping the answers (the first line is the menu number of "Synthesize MyAccount Optin Events", 12 in the sample build):

   ```sh
   S=../Documentation/SynthesizeMyAccountOptins/samples
   printf "12\n$S/profiles.jsonl\nout/optins\n21\n2026-10-01\ny\n" | dotnet ../DataMonger/bin/Debug/net10.0/DataMonger.dll
   ```

   | Line | Prompt |
   |---|---|
   | `$S/profiles.jsonl` | profile file (a path) |
   | `out/optins` | output folder |
   | `21` | random seed |
   | `2026-10-01` | anchor date |
   | `y` | generate the full set after the preview |

3. Compare (no difference expected):

   ```sh
   diff out/optins/marketing_opt_in_events_*[0-9].jsonl $S/expected-output/marketing_opt_in_events.jsonl
   ```

## What a run prints

```
Seed            : 21
Anchor (now)    : 2026-10-01 00:00:00Z
...
Preview event written: out/optins/marketing_opt_in_events_<timestamp>.json
...
=== Done ===
Profiles processed: 10
Opt-in events     : 3 across 1 file(s):
    out/optins/marketing_opt_in_events_<timestamp>.jsonl
```

## What the output shows

| Event | Observation |
|---|---|
| `CUST_1001`, 2026-06-01 `-07:00` | Channel `postal`; `contact_value` is the billing address; Los Angeles offset. |
| `CUST_2001`, 2024-01-10 `+09:00` | The Tokyo profile: the offset is `+09:00` (the time zone is honoured). |
| `CUST_2002`, 2023-09-19 `-04:00` | No time zone, so Eastern. Channel `sms`, but the profile has no phone, so `contact_value` falls back to the email. |

Profiles that never produce an event: `CUST_2004` (no email), the profile without a `user_id`, and `CUST_2006` (every
consent flag is `false`). The others either failed the 70% roll or, in this seeded run, simply did not get one.

Things to verify: every `consent.channel` is a channel the profile consents to; no `event_time` is after
2026-10-01 00:00 UTC or before the profile's `created_at`; `event_time` equals `consent.subscribed_at`.

## Uploading the output

The events are plain JSON objects; the [Upload JSONL Events](../../UploadJsonlEvents/Readme.md) command sends them as they
are. Against the mock API all 3 lines were accepted (`Success: 3   Failed: 0`). Point the command at the `expected-output`
folder to repeat it.
