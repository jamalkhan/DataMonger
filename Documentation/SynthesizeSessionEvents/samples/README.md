# Samples: Synthesize Session Events

Everything here is invented (Acme Outfitters, `example.com`, fictional ids).

## Files

| File | Purpose |
|---|---|
| [profiles.jsonl](profiles.jsonl) | 10 profiles. Lines 1-3 are real output of [Synthesize User Props](../../SynthesizeUserProps/samples/README.md) (`CUST_1001`..`CUST_1003`). Lines 4-10 are small hand-written profiles that exercise the edge cases listed below. |
| [products.jsonl](products.jsonl) | 15-row product feed: 5 products (a jacket, a tee, a kids' boot, a kids' fleece top, jeans), several color/size variants. Three rows are deliberately unusable (see below). |
| [expected-output/session_events.jsonl](expected-output/session_events.jsonl) | The 65 events the command wrote with seed 21 and anchor date 2026-10-01. |
| [expected-output/preview-session/](expected-output/preview-session/) | The 12 pretty-printed files of the first customer's preview session (one file per event). |

The real output files carry a run timestamp (`session_events_<yyyyMMdd_HHmmss>.jsonl`); the committed copies do not.

### Profile edge cases ([profiles.jsonl](profiles.jsonl))

| Line | `user_id` | What it shows |
|---|---|---|
| 1-3 | `CUST_1001`..`CUST_1003` | Full generated profiles (two memberships, consent, ...) |
| 4 | `CUST_2001` | Minimal profile in `Asia/Tokyo` (offset `+09:00`) |
| 5 | `CUST_2002` | No `timezone`: falls back to `America/New_York` |
| 6 | `CUST_2003` | Unknown zone id `Not/AZone`: falls back to `America/New_York` |
| 7 | `CUST_2004` | Phone identifier only, no email: never produces events |
| 8 | (none) | No `user_id`: events carry `"user_id": null` |
| 9 | `CUST_2006` | `country` `CA` and an Acme Kids membership: `currency` `CAD`, `storefront_id` `acme-kids-us` |
| 10 | `CUST_2007` | Legacy flat profile: only a top-level `email` |

### Product feed edge cases ([products.jsonl](products.jsonl))

| Row | What it shows |
|---|---|
| `TJ-100-SLA-L` | `"in_stock": false`: skipped (out of stock) |
| second `TE-200-WHT-M` | repeated sku: skipped (duplicate) |
| `BAD-1` | no `price`: skipped (invalid) |
| `KB-300-*` | `category` is `Footwear\|Kids` (one is picked per view); the url host `kids.` makes the brand `Acme Kids` |
| `KT-310-GRN-S` | explicit `"brand": "Acme Kids"` on a url that would otherwise give `Acme` |

## Reproduce

1. Build and create a scratch folder (the app needs a `LocalData/products.csv` at startup; this command does not use it):

   ```sh
   dotnet build DataMonger.slnx
   mkdir -p work/LocalData && cd work
   printf 'Id,ResourceType,Price,Quantity,ProductName,Category,Subcategory,Size,Url\n' > LocalData/products.csv
   export DATAMONGER_HOME="$PWD/home"
   ```

2. Run, piping the answers (the first line is the menu number of "Synthesize Session Events", 13 in the sample build):

   ```sh
   S=../Documentation/SynthesizeSessionEvents/samples
   printf "13\n$S/profiles.jsonl\n$S/products.jsonl\nout/events\n21\n2026-10-01\ny\nn\n" \
     | dotnet ../DataMonger/bin/Debug/net10.0/DataMonger.dll
   ```

   | Line | Prompt |
   |---|---|
   | `$S/profiles.jsonl` | profile file (a path; no `LocalData` folder is needed) |
   | `$S/products.jsonl` | product file |
   | `out/events` | output folder |
   | `21` | random seed |
   | `2026-10-01` | anchor date ("today") |
   | `y` | generate the full set after the preview |
   | `n` | do not upload |

3. Compare (no difference expected):

   ```sh
   diff out/events/session_events_*[0-9].jsonl $S/expected-output/session_events.jsonl
   ```

   The preview folder `out/events/session_events_<timestamp>_user_events/` matches `expected-output/preview-session/`.

## What a run prints

```
  12 purchasable products loaded (1 invalid, 1 duplicate, 1 out-of-stock rows skipped).
...
Seed            : 21
Anchor (now)    : 2026-10-01 00:00:00Z
...
Preview session written to: out/events/session_events_<timestamp>_user_events
    CUST_1001_20260917_104320_page_view.json  (page_view)
    ...
    CUST_1001_20260917_110823_purchase.json  (purchase)

=== Done ===
Profiles processed: 10
  page_view         : 40
  add_to_cart       : 16
  purchase          : 6
  remove_from_cart  : 3
Total events      : 65 across 1 file(s):
```

## What the output shows

| Customer | Events | Observation |
|---|---|---|
| `CUST_1001` | 12 | The forced preview session: starts with `page_view`, ends with a `purchase`. Offset `-07:00` (its zone is `America/Los_Angeles`). |
| `CUST_1003` | 12 | Ends with a `purchase`. |
| `CUST_2002` | 4 | No time zone: offset `-04:00` (Eastern daylight time). No purchase this time. |
| `CUST_2003` | 3 | Unknown zone: also Eastern. |
| (no user id) | 13 | `"user_id": null`; its zone is `America/Denver` (offset `-06:00`). |
| `CUST_2006` | 9 | `"currency": "CAD"` and `"storefront_id": "acme-kids-us"`. |
| `CUST_2007` | 12 | The flat profile works; zone `America/New_York`. |
| `CUST_1002`, `CUST_2001` | 0 | Their random page-view count was 0 in this run. |
| `CUST_2004` | 0 | No email. |

Things to verify in the file:

* The events of one customer are consecutive, share one `session_id` and `channel`, and the `event_time` values never
  decrease and span at most 45 minutes.
* Every `items[].sku` in an `add_to_cart` or `purchase` event appeared in an earlier `page_view` of the same session.
* A `purchase`, when present, is the last event of its customer; `order.total = subtotal - discount + shipping + tax`.
* No `event_time` is later than 2026-10-01 00:00 UTC.
* Change the seed (for example `22`) and the content changes; keep seed and anchor and it does not.

## Uploading the output

The events are plain JSON objects, so the [Upload JSONL Events](../../UploadJsonlEvents/Readme.md) command sends them as
they are. Against the mock API (`dotnet DataMonger.MockApi.dll --urls http://localhost:PORT`) all 65 lines were accepted
(`Success: 65   Failed: 0`). Point the upload command at the `expected-output` folder to repeat it (a folder upload skips
the key-press previews).
