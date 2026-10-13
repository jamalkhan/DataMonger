# Synthesize Session Events (page views/cart/purchase, fake data)

For each customer profile in a profile `.jsonl` file (as written by
[Synthesize User Props](../SynthesizeUserProps/Readme.md)), generates one fake **shopping session** made of
`page_view`, `add_to_cart`, `remove_from_cart` and (sometimes) `purchase` events. The products come from a product
feed `.jsonl` file you choose. All events of a customer share one session id, one channel and one 45-minute time
window, and are ordered so the story makes sense (you only add what you viewed, you only remove what is in the cart, a
purchase comes last). An optional **seed** and **anchor date** make the output reproducible.

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | This page: prompts, outputs, behavior |
| [EventSchema.md](EventSchema.md) | Every event type and field, the sequencing logic, time windows and probabilities |
| [InputFormat.md](InputFormat.md) | The profile input and the product feed input |
| [samples/README.md](samples/README.md) | Runnable inputs, real expected output, and reproduction steps |

## Syntax / how to run

Start the app (`dotnet DataMonger.dll`) and choose

```
Synthesize Session Events (page views/cart/purchase, fake data)
```

(menu number 13 in the sample build; the menu is sorted alphabetically). The command is site independent
(`RunsPerSite = false`). Prompts, in order:

| # | Prompt text | Accepted input | Default |
|---|---|---|---|
| 1 | `Select a number, enter a path to your own profile .jsonl, or 'q' to cancel:` (after `Available profile jsonl files (<folder>):` or `No *.jsonl files found under LocalData/jsonl/profile.`) | A number from the list (files in `LocalData/jsonl/profile`, newest first), a path to a file (quotes stripped), or `q`. Empty input re-prompts; end of input cancels. | none |
| 2 | `Select a number, enter a path to your own product .jsonl, or 'q' to cancel:` (list from `LocalData/jsonl/products`) | Same rules. | none |
| 3 | `Output folder for the .jsonl file(s) [path]:` | A folder path. Created if missing. | `LocalData/jsonl/events/Loads` (found by walking up from the current directory and the app folder; if no `LocalData/jsonl` exists, `./LocalData/jsonl/events/Loads`) |
| 4 | `Random seed (integer; blank = random, unrepeatable):` | An integer, or blank. Anything else re-prompts. | blank = random |
| 5 | `Anchor date used as "today", UTC yyyy-MM-dd (blank = the real current time):` | A date `yyyy-MM-dd` (taken as UTC midnight), or blank. Anything else re-prompts. | blank = now |
| 6 | `Continue and generate session events for every profile in the file? [y/N]:` | `y` or `n` (empty = `n`) | `n` |
| 7 | `Upload the N generated .jsonl file(s) now via API => Upload JSONL Events (track event)? [y/N]:` | `y` or `n` (empty = `n`) | `n` |

After the product file is chosen the command prints `Loading product catalog: <path>` and
`  N purchasable products loaded (a invalid, b duplicate, c out-of-stock rows skipped).` Between prompts 5 and 6 it
prints a settings summary and writes the **preview session**.

Scripted example (the sample reproduction; see [samples/README.md](samples/README.md)):

```
13                     <- menu entry
<profiles.jsonl>       <- prompt 1
<products.jsonl>       <- prompt 2
out/events             <- prompt 3
7                      <- prompt 4 (seed)
2026-10-01             <- prompt 5 (anchor)
y                      <- prompt 6
n                      <- prompt 7 (do not upload)
```

## Inputs

* A profile file: one JSON object per line. The fields it reads are listed in
  [InputFormat.md](InputFormat.md#profile-input) (email, user id, time zone, locale/country, first membership's storefront).
* A product feed: one product variant (sku) per line. See [InputFormat.md](InputFormat.md#product-feed-input).

## Outputs

| Output | When | Content |
|---|---|---|
| `session_events_<timestamp>_user_events/` (folder) | Always, if the first profile has an email | One pretty-printed `.json` file per event of the **first** profile's session, named `<user_id>_<local yyyyMMdd_HHmmss of the event>_<event_type>[_<n>].json`. A review aid; it is not uploaded. |
| `session_events_<timestamp>.jsonl` | After you answer `y` | One compact event per line; the events of a profile are consecutive and chronological |
| `session_events_<timestamp>_<n>.jsonl` | More than 50,000 events | The same, split every 50,000 events (not configurable) |

`<timestamp>` is the local time the run started (`yyyyMMdd_HHmmss`); it is not part of the reproducible content. The
shape of each event is in [EventSchema.md](EventSchema.md). The console summary lists the number of profiles processed,
the count per event type (descending, ties alphabetical) and the output files.

## Remarks

* **Preview, then replay.** The first usable profile is generated with a *forced full funnel* (2-10 page views, 1-5
  adds, and a purchase whenever the cart is not empty at the end) so the preview shows every event type. Answering `y`
  writes that same session as the first events of the `.jsonl` (verbatim), then continues with the remaining profiles from
  the same random generator. Because of this, the first customer always has a longer session than average.
* **Which profiles produce events.** A profile with no resolvable email never produces events. Other profiles produce
  no events with the probability that the random page-view count is 0 (1 in 11). Lines that are blank are ignored; lines
  that are not a JSON object print `[WARN] Skipping line N: not a JSON object.` and are counted in the summary
  (`Profiles processed: N (k line(s) skipped)`).
* **Determinism.** With the same seed, anchor date, profile file, product file and file order, two runs write
  byte-identical events (only the timestamp in the file names differs). Without a seed the run is random. With a seed but
  no anchor the event times depend on the current time, so the run repeats only within the same UTC day (the command
  prints a `[note]`). Event times are never later than the anchor.
* **Time zones.** Each session's times are expressed in the profile's own `timezone` (offset in the `event_time` string).
  A missing, blank or unknown zone id falls back to `America/New_York`.
* **Sessions are independent.** There is one session per profile per run, within the last 14 days before the anchor.
* **Products.** Rows without a sku, name or numeric price, repeated skus and rows with `"in_stock": false` are skipped
  (reported on screen); if nothing usable is left the command fails with
  `[FAIL] InvalidOperationException: No purchasable products found in <path> ...`.
* **Errors.** `[FAIL] No profile file selected. Aborting.`, `[FAIL] No product file selected. Aborting.`,
  `[FAIL] Profile file has no usable profiles.`, or `[FAIL] <ExceptionType>: <message>` plus the stack trace.
* **Upload.** Answering `y` at prompt 7 hands the generated files to
  [Upload JSONL Events](../UploadJsonlEvents/Readme.md), which asks for a site and then (for files it was given, not a
  folder) previews the first request and waits for a key press. That command needs an interactive console.

## Compatibility with the upload commands

Each line is one self-contained JSON object. [Upload JSONL Events](../UploadJsonlEvents/Readme.md) POSTs every line
unchanged to `{BaseUrl}/ver2/{SiteId}/activities`, and the mock API accepts any well-formed JSON body. Verified against the
mock API: the 65 events of the committed sample output (`Success: 65   Failed: 0`) and the 3 events of the
[opt-in sample](../SynthesizeMyAccountOptins/samples/README.md) were all accepted with HTTP 202. The events deliberately carry no vendor-specific wrapper; if the service you target expects another
envelope, reshape the file first.

## Examples

[samples/README.md](samples/README.md): 10 profiles and a 15-row product feed in, 65 events out, with the exact commands.

## Related

* [Synthesize User Props](../SynthesizeUserProps/Readme.md): produces the profile file
* [Synthesize MyAccount Optin Events](../SynthesizeMyAccountOptins/Readme.md)
* [Upload JSONL Events](../UploadJsonlEvents/Readme.md), [Mock API](../MockApi/Readme.md)
