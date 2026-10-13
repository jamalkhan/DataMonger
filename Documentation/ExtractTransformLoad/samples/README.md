# Extract / Transform / Load: samples

Runnable examples for the [Extract / Transform / Load](../Readme.md) command. Every folder holds a small set of
invented inputs (a fictional retailer, "Acme Outfitters"), one or more `.etlconfig` files and an
`expected-output/` folder with the **real output** the command produced from them.

| Folder | Feature | Documented in |
| --- | --- | --- |
| [`data/`](data/) | Shared inputs used by most scenarios | below |
| [`01-basic-object/`](01-basic-object/) | One record per row; typed input and output fields; blank and null values | [Outputs](../Outputs.md) |
| [`02-nested-children/`](02-nested-children/) | Children, `one`/`many`, three levels, filter, shared `as`, object group, inline relationship, `inline: true` | [Relationships](../Relationships.md) |
| [`03-keyed-map/`](03-keyed-map/) | `keyedMap` output, `keyExpr`, `dedup`, `filter` + `filterExcludedFile` | [Outputs](../Outputs.md#keyedmap-mode) |
| [`04-lookup/`](04-lookup/) | Lookups: single, array, `exists`, `expr`, `lookups`, `coalesce` | [Outputs](../Outputs.md#lookup-fields) |
| [`05-keyed-relations/`](05-keyed-relations/) | `keyBy`, `keyedSingle`, `keyedSingleBestOf`, `keysOf` | [Relationships](../Relationships.md#keyby) |
| [`06-merge-by/`](06-merge-by/) | `mergeBy` + `mergePrefix` across two sources | [Relationships](../Relationships.md#mergeby) |
| [`07-external-merge/`](07-external-merge/) | `joinStrategy: externalMerge`, composite key | [RunOptions](../RunOptions.md#joinstrategy) |
| [`08-load-sampling/`](08-load-sampling/) | `sampling`, `richSampling`, relation `limit` | [Load](../Load.md#sampling) |
| [`09-run-limits/`](09-run-limits/) | `run.sample`, `run.keyFilter`, `run.maxRecords`, `splitEvery` | [RunOptions](../RunOptions.md) |
| [`10-filter-distinct-require/`](10-filter-distinct-require/) | Output `filter`, `filterExcludedFile`, `distinctBy`, `requireRelations` | [Outputs](../Outputs.md#filter) |
| [`11-errors/`](11-errors/) | `skip` / `nullify` / `abort`, step and field `onError`, rejects and warnings files, the `record_key` column | [Errors](../Errors.md) |
| [`12-delimited-sources/`](12-delimited-sources/) | Delimiters, quotes, BOM/CRLF, gzip | [Sources](../Sources.md) |
| [`13-joins-advanced/`](13-joins-advanced/) | `caseInsensitiveJoin`, composite key, `shareDistinct`, lookup reuse | [Relationships](../Relationships.md#caseinsensitivejoin) |
| [`14-load-folders/`](14-load-folders/) | `{run_timestamp}`, per-output folders, the output-folder prompt | [Load](../Load.md#folder) |
| [`transforms/`](transforms/README.md) | Every transform op and filter operator | [Transforms](../Transforms/Readme.md) |

## How to run a sample

The command reads its configs from your **ETL config folder** (default `~/Documents/DataMonger/EtlConfig`,
chosen on the first run and remembered in `settings.json`; see [Configuration](../../Configuration.md#config-folders)).
The samples are run by typing the path of the config at the prompt, so the config folder is not used to hold them.
To keep your real Documents folder out of it, point `DATAMONGER_HOME` at a scratch folder.

From the `ExtractTransformLoad` documentation folder (so that `samples/...` paths resolve), with a built app and the
usual `appsettings.json` and `LocalData/products.csv` that the app reads at start-up (see the
[Documentation overview](../../Readme.md#quick-start); `LocalData/` is found in the working directory):

```sh
cd Documentation/ExtractTransformLoad
export DATAMONGER_HOME=./scratch-home      # optional: keeps settings.json and the config folder out of ~/Documents
dotnet path/to/DataMonger.dll
```

(Alternatively run from the repository root, where the quick start puts `LocalData/`, and prefix every config path
with `Documentation/ExtractTransformLoad/`; the console then shows the longer paths.)

1. Choose **Extract / Transform / Load (config-driven)** from the menu (`8` at the time of writing; the menu is
   alphabetical, so read the list).
2. On the **first run only**, the app asks for the config folder:

   ```
   First run: choose the folder where your ETL config files (.etlconfig) live.
   Config folder [~/Documents/DataMonger/EtlConfig]:
   ```

   Press Enter. It creates the folder, copies `example.etlconfig` into it and saves the choice. The complete
   transcript of such a first run is [`first-run-console.txt`](first-run-console.txt).
3. At the config prompt

   ```
   Select a number, enter a path to your own .etlconfig, 'c' to change the config folder, or 'q' to cancel:
   ```

   enter the path of a sample config, for example `samples/01-basic-object/basic.etlconfig`.

The same thing piped, for a run where the config folder has already been saved (so there is no first-run question):

```sh
printf '8\nsamples/01-basic-object/basic.etlconfig\n' | dotnet path/to/DataMonger.dll
```

On a brand-new `DATAMONGER_HOME` add one empty line after the menu number to accept the default config folder:
`printf '8\n\nsamples/01-basic-object/basic.etlconfig\n'`.

Only [`14-load-folders`](14-load-folders/load-folders.etlconfig) asks a further question, for an output folder:
answer `output/fallback`.

Every config writes to an `output/` folder next to the config file, in a new `run_<timestamp>/` subfolder per
run (see [Load](../Load.md#folder)). Compare it with `expected-output/`. Delete the `output/` folders when you
are done; they are not meant to be committed.

## About `expected-output/`

* Each **config** has its own subfolder, `expected-output/<config name>/`, containing the files of one run:
  `run_20260101_000000/...` (the run folder) and `console-output.txt` (what the app printed from the
  `=== Extract / Transform / Load ...` header onward, config-folder listing included).
* The real timestamp in file names and console text was replaced by `20260101_000000`, and the elapsed time by
  `00:00:00.000`, so the folders are stable. A real run has your current time instead.
* Paths in `console-output.txt` are shown relative to the working directory (the `ExtractTransformLoad` folder),
  `[~/Desktop]` stands for your Desktop folder, and `~/Documents/DataMonger/EtlConfig` stands for the config
  folder (the real transcripts used a scratch `DATAMONGER_HOME` whose `settings.json` already pointed at a folder
  holding the seeded `example.etlconfig`). The first line of every transcript after the header is therefore the
  config-folder listing, then the prompt.
* **Random parts.** In [`08-load-sampling`](08-load-sampling/) the `random-sample/` files are one possible result
  of a random pick (2 of 6 records); the file names and contents vary run to run. Everything else is deterministic
  (`richSampling` included).
* [`first-run-console.txt`](first-run-console.txt) is a genuine first run of `basic.etlconfig` on a fresh home. It adds the
  `Config folder [...]:` question, the copy of the bundled example and the `Saved.` line (the Enter key that answered
  the question is not echoed by a piped run, so a newline was added after that prompt).
* The abort sample's console output has its stack trace replaced by `[stack trace omitted]`.
* The decompression cache `12-delimited-sources/products.csv.gz_decrypted/` is created next to the gzip file when
  you run that sample; it is not part of the expected output.

## The shared data (`data/`)

All values are invented. `customer_id` is the common key.

| File | Columns | Rows | Notes |
| --- | --- | --- | --- |
| `customers.csv` | `customer_id, first_name, middle_name, last_name, email, phone, birth_date, signup_ts, is_member, lifetime_spend, tier` | 6 | Blank `middle_name`, `phone`, `tier`, `email` cells; `is_member` written as `true`/`false`/`0`/`1`; customer 1001's email has mixed case |
| `orders.csv` | `order_id, customer_id, order_date, status, subtotal, store_id` | 8 | Statuses `shipped`/`returned`/`pending`; an order for unknown customer 9999 (an orphan); one blank `store_id` |
| `order_lines.csv` | `line_id, order_id, sku, quantity, unit_price` | 11 | |
| `products.csv` | `sku, product_name, category, brand` | 6 | |
| `stores.csv` | `store_id, store_name, region` | 3 | |
| `emails.csv` | `email_id, customer_id, email_address, status, status_date, brand` | 7 | Brands `Acme` and `Acme Kids`; customer 1001 has the same address under both brands; 1003 has the same address twice with different statuses |
| `email_prefs.csv` | `pref_id, customer_id, email_address, brand, channel, opted_in` | 3 | |
| `phones.csv` | `customer_id, phone_number, brand, phone_type, priority` | 6 | Customer 1002 has two `Acme` rows (priority 2 and 1) |

Customers 1004 and 1005 have no shipped orders; 1005 has no email; 1001 has the richest record.

---

## 01-basic-object

| File | Purpose |
| --- | --- |
| `basic.etlconfig` | Types four source columns, maps them to typed output fields, `concat`/`lower`/`multiply` pipelines |
| `empty-and-null.etlconfig` | `run.emitEmpty: true` and a field with `emitNull: true` |

Run: `basic.etlconfig` then (separately) `empty-and-null.etlconfig`.

Look for: in `basic`, missing `middle_name` and `email` where cells were blank; `lifetime_spend` numbers,
`birth_date` as `yyyy-MM-dd`, `signed_up_at` as `...T09:15:00Z`. In `empty-and-null`, `"middle_name":""` and
`"nickname":null`.

## 02-nested-children

`nested.etlconfig`: two outputs, `customers_nested` (customer -> orders -> lines -> product, plus `store`, merged
`contacts`, a `profile` group and an inline `mobile_numbers` array) and `order_lines_flat` (order and product
fields spliced into each line with `inline: true`).

Look for: returned order 5003 absent (child `filter`); `contacts` mixing email and phone entries from two sources
sharing `as`; `mobile_numbers` appearing between `profile` and `signed_up`; order 5008 (unknown customer) never
appearing; `order_lines_flat` records with `order_date` and `product_name` at the top level.

## 03-keyed-map

`keyed-map.etlconfig`: output `email_map` (only active addresses; excluded rows go to `..._filtered.csv`) and
`email_map_dedup` (no filter; `dedup` keeps the latest row per address).

Look for: properties named `email::<address>`; customer 1003 keeping only the 2024 row after `dedup`; customer
1001's two brand rows collapsing to the 2025 `Acme Kids` row.

## 04-lookup

`lookup.etlconfig`: one output exercising every lookup form and `coalesce`.

Look for: `has_shipped_order` false for 1004/1005; `order_summaries` like `5001:shipped`; `order_status_codes`
`["S","R"]`; `brands` the union of email and phone brands; `contact_value`/`contact_type` falling back to the phone
for customer 1005 (no email); fields omitted when there is no match.

## 05-keyed-relations

`keyed-relations.etlconfig`.

Look for: `emails_by_brand` as `{"ACM":[...],"KID":[...]}`; `phones_by_brand` one object per key and, for customer
1002, the priority-1 row (`555-0112`) winning key `ACM`; `brand_codes` listing the keys; empty `{}` for customers with
no rows.

## 06-merge-by

`merge-by.etlconfig`.

Look for: customer 1001's `contacts["acme-shop"]` containing **one** entry for `ada.lovelace@example.com` whose
`properties` carry `acme_status`, `kids_status` and the preference columns `acme_channel`/`acme_opted_in`, plus a
single un-prefixed `order_ids` array.

## 07-external-merge

`external-merge.etlconfig` over `customers_shuffled.csv` (rows deliberately out of order).

Look for: records sorted by `customer_id` although the file is not; the console lines `orders: sorted 8 records into
1 run(s)` and `externalMerge: levels below 'orders' are nested using in-memory indexes.`; the composite-key join in
`email_with_prefs`.

## 08-load-sampling

`load-sampling.etlconfig`: `sampling` (2 random records into `random-sample/`) and `richSampling` (the 2 richest
into `richest-records/`), plus `limit: 2` on orders.

Look for: `richest-records/1001_Lovelace.json` and `1002_Turing.json`; customer 1001's orders capped at 2.

## 09-run-limits

Three configs over the same data:

| Config | Setting | Result |
| --- | --- | --- |
| `sample.etlconfig` | `run.sample` first 2 primary rows | customers 1001 and 1002 with all their orders and emails |
| `key-filter.etlconfig` | `run.keyFilter` `["1003","1006"]` | exactly those two customers |
| `max-records.etlconfig` | `run.maxRecords: 5`, `splitEvery: 2` | files `_1`, `_2` (2 records) and `_3` (1 record); `stopped at run.maxRecords = 5` in the console |

## 10-filter-distinct-require

`filters.etlconfig`: three outputs.

Look for: `orders_filtered` writing 5 records and 3 excluded rows
(5003 is `returned`, 5007 has a blank `store_id`, 5008's subtotal is below 50) to `..._filtered.csv`; `order_events` writing 7 records (one per order) from 11 order lines, each with all its lines;
`customers_with_orders` dropping 1004 and 1005 (to its `_filtered.csv`) and listing only shipped orders.

## 11-errors

Five configs over `messy_orders.csv` (a non-numeric quantity, a non-numeric price, a month-day-only date, a value in
scientific notation):

| Config | Policy | Result |
| --- | --- | --- |
| `skip.etlconfig` | `onError: skip` | 2 records written, 3 rows in `..._rejects.csv` |
| `nullify.etlconfig` | `onError: nullify` | 5 records, bad fields simply missing; 1 warning (scientific notation) |
| `abort.etlconfig` | `onError: abort` | `[FAIL] FormatException` at the first bad row; only the first record written |
| `field-and-step.etlconfig` | `skip` + step `default`/`null` + field `warnAndNull` | 5 records, 2 warnings, no rejects |
| `record-key.etlconfig` | `skip` + field `warnAndNull`, with relationships (customers with their orders; preferences with emails on a two-column key) | 6 + 3 records; 4 + 3 warnings whose `record_key` is `1001`, `1003`, `1004`, `1004` and `1001\|Acme`, `1002\|Acme`, `1006\|Acme Kids` |

The warnings files of the other four configs have a blank `record_key`: their output has no relationship, so there is
no join key to report ([Errors](../Errors.md#how-record_key-is-derived)).

## 12-delimited-sources

`delimited.etlconfig` reads six differently formatted files into identically shaped records: pipe-delimited with a
delimiter and a line break inside quoted cells; tab-separated with doubled quotes; semicolon; `~` delimiter with a
`'` quote character; BOM + CRLF + a blank line; and a gzip file.

## 13-joins-advanced

`joins-advanced.etlconfig`: three outputs sharing sources.

Look for: customer 1001 matching `email_match_ignoring_case` but not `email_match_exact`; `known_addresses` listing
each address once although it exists in two files; the second output's console line
`lookup reused from an earlier output in this run`.

## 14-load-folders

`load-folders.etlconfig`: run it with the prompt answer `output/fallback`.

Look for: `customers` and `orders` under one shared `run_<ts>/` folder, `orders` split into two files, and `products`
(no `load.folder`) under `output/fallback/run_<ts>/`.
