# Synthesize MyAccount Optin Events (fake data)

Reads a file of customer profiles (one JSON object per line, as written by
[Synthesize User Props](../SynthesizeUserProps/Readme.md)) and generates, for roughly 70% of them, one synthetic
**marketing opt-in event** (`marketing_opt_in`): a record that the customer subscribed to marketing on one of the
channels they consent to, at some moment in the last four years. Unlike
[Synthesize Session Events](../SynthesizeSessionEvents/Readme.md), these events are not part of a browsing session and
need no product feed. An optional **seed** and **anchor date** make the output reproducible.

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | This page: prompts, outputs, behavior |
| [EventSchema.md](EventSchema.md) | Every field of the event, the time-window logic and the probabilities |
| [InputFormat.md](InputFormat.md) | The profile fields this command reads |
| [samples/README.md](samples/README.md) | Runnable input, real expected output, and reproduction steps |

## Syntax / how to run

Start the app (`dotnet DataMonger.dll`) and choose

```
Synthesize MyAccount Optin Events (fake data)
```

(menu number 12 in the sample build; the menu is sorted alphabetically). It runs once (not per site) and never calls
an API. Prompts, in order:

| # | Prompt | Accepted | Default |
|---|---|---|---|
| 1 | `Select a number, enter a path to your own profile .jsonl, or 'q' to cancel:` (after `Available profile jsonl files (<folder>):` or `No *.jsonl files found under LocalData/jsonl/profile.`) | A number from the list (files in `LocalData/jsonl/profile`, newest first), a file path (quotes stripped), or `q`. Empty re-prompts; end of input cancels. | none |
| 2 | `Output folder for the .jsonl file(s) [path]:` | A folder (created if needed) | The user's Desktop folder, or the current directory if the OS reports none |
| 3 | `Random seed (integer; blank = random, unrepeatable):` | An integer or blank; anything else re-prompts | blank = random |
| 4 | `Anchor date used as "today", UTC yyyy-MM-dd (blank = the real current time):` | A date `yyyy-MM-dd` (UTC midnight) or blank; anything else re-prompts | blank = now |
| 5 | `Continue and generate opt-in events for every profile in the file? [y/N]:` | `y` / `n`; Enter means `n` | `n` |

Note the default output folder differs from the Session Events command (which defaults to `LocalData/jsonl/events/Loads`).

Scripted example:

```
12                  <- menu entry
<profiles.jsonl>    <- prompt 1
out/optins          <- prompt 2
21                  <- prompt 3 (seed)
2026-10-01          <- prompt 4 (anchor)
y                   <- prompt 5
```

## Inputs

A profile file; the fields used are listed in [InputFormat.md](InputFormat.md). Blank lines are ignored; lines that are
not a JSON object print `[WARN] Skipping line N: not a JSON object.` and are counted in the summary.

## Outputs

| File | When | Content |
|---|---|---|
| `marketing_opt_in_events_<timestamp>.json` | If the first profile rolled an opt-in | That event, pretty-printed, as a preview (written before prompt 5) |
| `marketing_opt_in_events_<timestamp>.jsonl` | After you answer `y` | One compact event per line, in profile order |
| `marketing_opt_in_events_<timestamp>_<n>.jsonl` | More than 50,000 events | The same, split every 50,000 events |

`<timestamp>` is the local start time (`yyyyMMdd_HHmmss`); it is only in the file names. The console summary shows the
number of profiles processed and the number of opt-in events written. The event layout is in
[EventSchema.md](EventSchema.md).

## Remarks

* **Preview, then replay.** The first usable profile is rolled and previewed (or the command says it rolled no opt-in);
  on `y` that same event is written first and the remaining profiles continue from the same random generator. Answering
  `n` writes no `.jsonl`.
* **Who gets an event.** A profile needs an email and a `user_id`, at least one consented channel, and a successful 70% roll.
  All others are silently skipped. So the number of events is lower than the number of profiles; profiles with every
  consent flag `false` never get one.
* **Determinism.** Same seed, anchor date and profile file give byte-identical events (only the file-name timestamp
  differs). Without a seed the run is random; with a seed but no anchor the times depend on the current time, so the run
  repeats only within the same UTC day (the command prints a `[note]`). No event is later than the anchor.
* **Time zone.** The event time uses the profile's top-level `timezone`; a missing, blank or unknown zone falls back to
  `America/New_York`.
* **Errors.** `[FAIL] No profile file selected. Aborting.`, `[FAIL] Profile file has no usable profiles.`, or
  `[FAIL] <ExceptionType>: <message>` plus the stack trace.

## Compatibility with the upload commands

Each line is a self-contained JSON object. [Upload JSONL Events](../UploadJsonlEvents/Readme.md) sends every line unchanged to
`{BaseUrl}/ver2/{SiteId}/activities`; the mock API accepts any well-formed JSON body. Verified: the 3 events of the
committed sample output were all accepted (`Success: 3   Failed: 0`, HTTP 202). The events carry no vendor-specific
wrapper; reshape the file first if your target service expects another envelope.

## Examples

[samples/README.md](samples/README.md): 10 profiles in, 3 events out, with the exact commands.

## Related

* [Synthesize User Props](../SynthesizeUserProps/Readme.md): produces the profile file
* [Synthesize Session Events](../SynthesizeSessionEvents/Readme.md)
* [Upload JSONL Events](../UploadJsonlEvents/Readme.md)
