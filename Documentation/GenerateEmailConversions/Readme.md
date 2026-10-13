# Generate Email Conversion data

Writes a **JSON Lines file of simulated email-marketing histories**, one line per customer in `identified.csv`. Each line
holds the recipient and an ordered, time-stamped list of events: `email_sent`, then either `email_bounced` or
`email_delivered`, then possibly `email_opened`, `email_clicked` and `purchase`. Nothing is sent over the network. The
file is meant to be replayed against an API by [API => Email Conversion Events](../ApiEmailConversionEvents/Readme.md).

## Contents

* [EventSchema.md](EventSchema.md) - field-level reference of the output (row, event types, campaign, order)
* [Input CSV formats](../PurchaseKnown/InputFormat.md) (`identified.csv` and `products.csv` are used)
* [samples/](samples/README.md) - runnable inputs, seeded output and the exact reproduction steps

## Syntax / how to run

Menu entry text: `Generate Email Conversion data` (class `GenerateEmailConversions`; number `9` in the reference build).
The menu is alphabetical, so check the number in your build.

| Prompt | Default | Accepted |
|---|---|---|
| `Choice:` | none (re-asked on invalid input) | menu number, or `q` to quit |
| `Random seed (integer; blank = unseeded, different every run):` | blank = unseeded | any 32-bit integer (negative allowed); anything else is rejected and the prompt repeats |

The command runs **once**, regardless of how many sites are configured (`RunsPerSite` is `false`): it never contacts a
site, so the console header is `=== Generate Email Conversion data ===` with no `-> site:` suffix. (Earlier versions ran it
once per site, overwriting the same file each time.)

```
printf '9\n13\n' | dotnet DataMonger.dll
```

## Inputs

* `LocalData/identified.csv` - [format](../PurchaseKnown/InputFormat.md#identifiedcsv): first name, last name, email, user id.
* `LocalData/products.csv` - [format](../PurchaseKnown/InputFormat.md#productscsv), loaded at app start-up. An empty
  product list prints `[FAIL] products.csv is empty: cannot pick featured products.`, returns (0, 1) and writes nothing
  (checked before the seed prompt).

A malformed `identified.csv` row (fewer than four columns) stops the command with an `InvalidDataException` naming the
file and line; blank lines are skipped. A missing `identified.csv` is an unhandled `FileNotFoundException`.

## Outputs

`LocalData/EmailConversion.jsonl`, UTF-8 without BOM, `\n` line endings, one JSON object per line, created (with its
folder) and **overwritten** on every run. Console:

```
Seed 13: output is reproducible (events are anchored at 2025-01-15T12:00:00.000Z).
Wrote 10 rows (33 events total) to <path>/LocalData/EmailConversion.jsonl
```

Unseeded runs print `No seed: output differs on every run (events are anchored at the current time).` instead. The
return value is (rows written, 0). Nothing is written to `app.log`.

## Seed and determinism

* **Seeded** (`Random(seed)`): every random choice (campaign, timing, bounce reason, ids, order contents) comes from
  that one generator and all timestamps are computed relative to a **fixed anchor, 2025-01-15T12:00:00Z**. The same seed
  with the same `identified.csv` and `products.csv` therefore produces a **byte-identical** file (verified by running it
  three times and comparing checksums). Changing either CSV changes the output.
* **Unseeded** (`new Random()`): different every run; the anchor is the current UTC time.

The anchor is "now" for the generator: each recipient's `email_sent` time is uniform in the 7 days before it. Later events
of a row (opens, clicks) can fall after the anchor because open delays are up to 48 hours.

## Generation model

Per recipient, in this order (all probabilities are hardcoded):

1. A **campaign** is drawn uniformly from a pool of eight (see below). Every row has one `message_id`
   (`msg-` plus 16 hex characters) shared by all its events, and one `device_type` used for its opens and clicks.
2. `email_sent` at `anchor - U(0, 7 days)`.
3. **5% bounce** (row ends): `email_bounced` at sent + U(0, 30 min) with a `bounce_type` and `bounce_reason`.
4. Otherwise `email_delivered` at sent + U(0, 30 min).
5. **20%** of delivered rows end here (never opened). Otherwise `email_opened` at delivered + U(0, 48 h).
6. After an open, up to 20 steps while the row is "open": 60% stop, 10% another `email_opened` after U(0, 48 h) more,
   30% `email_clicked` after U(0, 2 min) with `link_url`.
7. After a click: 40% `purchase` after U(0, 10 min) containing the clicked product plus 0 to 2 others; 60% stop.

Event times are therefore non-decreasing within a row. Expected shape of 10 rows is roughly 10 sent, 9 delivered, 7 opened,
1 to 3 clicked, 0 to 2 purchases (see [samples](samples/README.md) for one real run).

### Campaign pool

Eight campaigns are created per run, one per subject line below, each with a **featured product** drawn at random from
`products.csv`. The featured product determines the campaign's `category` (its category) and `brand` (`Acme Kids` when
its subcategory is `Kids` or `Baby`, case-insensitive; otherwise `Acme`), and the click's `link_url`. Ids are 5001 to 5008;
`name` is `<slug>-<yyyyMMdd of the anchor date>`.

| id | slug | subject | preheader |
|---|---|---|---|
| 5001 | spring-arrivals | New Spring Arrivals: 30% Off | Refresh your wardrobe with this season's must-haves |
| 5002 | denim-days | Denim Days: Up to 50% Off | Find your perfect fit |
| 5003 | cozy-layers | Cozy Layers for Cooler Days | Hoodies, sweaters, and more |
| 5004 | final-sale | Last Chance: Final Sale | Limited stock, shop before it's gone |
| 5005 | wardrobe-refresh | Your Wardrobe Refresh Awaits | Easy looks for every occasion |
| 5006 | kids-collection | New Collection for Kids | Playful styles for the whole family |
| 5007 | essentials-sale | Acme Essentials, On Sale | Soft layers you'll live in |
| 5008 | workwear | Workwear Reimagined | Polished pieces for desk to dinner |

### Bounce reasons

`hard`/`mailbox does not exist`, `soft`/`mailbox full`, `soft`/`temporary server error`, `hard`/`domain not found`
(uniform).

## Remarks

* Writing is sequential; the file is produced in `identified.csv` order.
* The command is idempotent for a given seed and inputs; unseeded it is not.
* Rows are self-contained: every event repeats the recipient email, user id, message id and campaign, so any single
  event can be posted on its own, which is what the uploader does.
* The output uses neutral names only; there is no wrapper or vendor envelope.

## Examples

See [samples/README.md](samples/README.md) (seed 13; committed output [samples/expected-output/EmailConversion.jsonl](samples/expected-output/EmailConversion.jsonl)).

## Related

[API => Email Conversion Events](../ApiEmailConversionEvents/Readme.md) | [Known purchases](../PurchaseKnown/Readme.md) (shares `identified.csv`)
