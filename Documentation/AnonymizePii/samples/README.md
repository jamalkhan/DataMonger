# Anonymize PII: samples

Runnable inputs, configs and the real output of running them through the app. Everything is invented (people such
as Ada Lovelace and Alan Turing, `example.com` addresses, 555-01XX phone numbers). Docs: [Readme](../Readme.md),
[AnonConfig](../AnonConfig.md), [Anonymizers](../Anonymizers.md), [FileHandling](../FileHandling.md).

## Layout

```text
samples/
  input/                      source files (never modified)
    src_customers.csv         10 rows; names, e-mail, phone, dob, address, hash, card last4; some empty cells
    src_orders.csv            no PII; UTF-8 BOM + CRLF (to show the byte-copy)
    src_newsletter.psv        pipe-delimited
    src_tickets.csv           BOM + CRLF, quoted cells with embedded newlines/quotes/commas, one blank line
    src_returns.csv           small file used by the error samples
  configs/                    one .anonconfig per scenario (all use a seed)
  expected-output/            output of the real runs, one folder per config
    console/<scenario>.txt    the console output of each run, plus first-run.txt (see "Console transcripts")
```

Config paths are relative to the config file, so the samples work from any checkout location.

## How to reproduce

Run from this `samples` folder, with the built app. The command reads its configs from your **anonymization config
folder** (default `~/Documents/DataMonger/Config/AnonConfig`, asked for once on the first run and remembered in
`settings.json`; see [Configuration](../../Configuration.md#config-folders)). The samples are run by typing the path of
the config at the prompt. Point `DATAMONGER_HOME` at a scratch folder to keep the saved settings out of your real
Documents folder:

```sh
cd Documentation/AnonymizePii/samples
export DATAMONGER_HOME=./scratch-home
printf '1\n\nconfigs/basic.anonconfig\n' | dotnet path/to/DataMonger.dll   # first run: the empty line accepts the default config folder
printf '1\nconfigs/basic.anonconfig\n'    | dotnet path/to/DataMonger.dll   # later runs: no folder question
```

`1` is the menu number of **Anonymize PII (config-driven)** at the time of writing; the menu is alphabetical, so
check the list the app prints. Interactively, run `dotnet path/to/DataMonger.dll`, choose the entry, press Enter at
`Config folder [~/Documents/DataMonger/Config/AnonConfig]:` (first run only) and type `configs/basic.anonconfig` at
the *Select a number, enter a path...* prompt. The app needs its usual `appsettings.json` and `LocalData/products.csv`
in the working directory's `LocalData/` folder to start (see [Configuration](../../Configuration.md)); the command itself
uses neither. Running writes into `expected-output/<scenario>/` (the configs point there), overwriting the
committed files with, for a seeded config, identical content.

All configs set `run.seed`, so the output is reproducible. The seed values: basic 42, when-empty 7, all-methods
2024, bad-cell 1, missing 3. Rerunning `basic` twice and diffing the output folders produced no differences
(verified). Outputs are reproducible for this build of .NET and the engine; a different runtime version could in
principle draw a different sequence from the same seed. Remove the `seed` line to see different fakes each run.

### Console transcripts

`expected-output/console/*.txt` hold what the app printed from the `=== Anonymize PII ...` header onward. They
have been edited in four ways: the `Total elapsed` line (varies run to run) is removed, stack traces after a
`[FAIL]` line are removed, the absolute path of this folder is replaced by `<samples>`, and the config folder
(a scratch `DATAMONGER_HOME` in the real runs) is shown as `~/Documents/DataMonger/Config/AnonConfig`.
Every transcript therefore begins with the config-folder listing (`Available anonymization configs (...)` with the
seeded `example.anonconfig`) and the prompt. [`first-run.txt`](expected-output/console/first-run.txt) is a genuine
first run on a fresh home: it additionally shows the `Config folder [...]:` question, the copy of the example config and
the `Saved.` line (the Enter key that answered the question is not echoed by a piped run, so a newline was added after
that prompt).

## 1. Basic: everything in one run

`configs/basic.anonconfig` (seed 42), output in `expected-output/basic/`.

| Input | What it shows | Output |
|---|---|---|
| `src_customers.csv` | Group semantics: `first_name`, `last_name`, `full_name` and `email` describe one fake person per row (`group: person`); address parts agree (`group: home`); `phone` with `format: dashes`; `fakeDate`; `randomHex` for the hash column; `last4`; empty email/phone/dob stay empty | `anonymized_customers.csv` |
| `src_orders.csv` | `columns: []`: byte-copied | `anonymized_orders.csv` (identical to the source, BOM and CRLF included; check with `cmp`) |
| `src_newsletter.psv` | `delimiter: pipe`; empty cells passthrough | `anonymized_newsletter.psv` |
| `src_tickets.csv` | quoted cells with embedded newlines, quotes and commas survive; output normalized to no BOM, LF records, blank line dropped | `anonymized_tickets.csv` |

Things to look for:

* Row 1001: `Lionel,Hutz,Lionel Hutz,lionel.hutz13@springfield.example` (one person) and
  `7243 Third Street,,Shelbyville,FL,33158` (one address). Compare row 1002 (a different person).
* `customer_id` is unchanged in every row, so the file still joins to `src_orders.csv` on `customer_id`.
* Row 1004 (empty e-mail) stays empty. Row 1003 (empty phone) and 1005 (empty dob) stay empty.
* `store_pref` and `notes` are not listed in the config and are passed through unchanged (including the embedded
  `"Said ""call me Amazing Grace"""`).
* Summary: `anonymized_customers.csv: 10 rows, 119 cells anonymized`, `anonymized_orders.csv: copied (no PII columns)`.

## 2. `whenEmpty`: passthrough vs generate

`configs/when-empty.anonconfig` (seed 7) writes `expected-output/when-empty/customers_when_empty.csv` (explicit
`output` name).

| Column | `whenEmpty` | Result on the empty cell |
|---|---|---|
| `email` | default (`passthrough`) | Row 1004 stays empty |
| `phone` | `generate` | Row 1003 gets `5555550180` |
| `dob` | `generate` (with `minYear` 1990..1995 and `format: MM/dd/yyyy`) | Row 1005 gets `03/04/1990` |
| `notes` | `generate` with `constant` | Every row, including empty ones, is `[redacted]` |

## 3. All methods

`configs/all-methods.anonconfig` (seed 2024, two `emailDomains`) writes
`expected-output/all-methods/customers_all_methods.csv`. One column per method/option variant: `guid` (`n` and
`upper` formats), `simpsonsName` with `part` `first`/`middle`/`full` and no `group` (each column draws an
independent name), `simpsonsEmail` with a fixed `domain`, `fakeAddress` with `line1`/`line2`/`province`/`postalExt`,
`constant`, `randomHex` with `length: 16`, `last4` with `digits: 6`, and `fakeDate` with `yyyyMMdd`.
`address_line2` uses `whenEmpty: generate` on `fakeAddress` `line2`, which is always empty text, so empties stay empty
but non-empty `Apt`/`Suite` values (`Building B`) are blanked.

## 4. Bad cell: skip vs abort

`configs/bad-cell-skip.anonconfig` and `configs/bad-cell-abort.anonconfig` are identical except for `onError`.
Both anonymize `src_customers.csv` `dob` with `fakeDate` and `minYear: 0`, which is not a valid year, so every
non-empty cell raises `ArgumentOutOfRangeException`. `phone` is also configured. A second job anonymizes
`src_returns.csv`.

| | `skip` | `abort` |
|---|---|---|
| Console | One `[warn] ... row N column 'dob': ... (left unchanged)` per bad cell (9 of them), then `anonymized_customers.csv: 10 rows, 9 cells anonymized, 9 error(s)` | `[FAIL] ArgumentOutOfRangeException: Year, Month, and Day parameters describe an un-representable DateTime.` and nothing else |
| `anonymized_customers.csv` | All rows; `dob` **unchanged** (`1815-12-10`...), `phone` anonymized | Header row only (file left half-written) |
| `anonymized_returns.csv` | Written (second job runs) | Not created (run stopped) |

Lesson: under `skip` the failed column leaves the file with real values, so always check the `error(s)` count.

## 5. Missing column and missing input

`configs/missing.anonconfig` (seed 3, `onError: skip`):

* `mobile_phone` is not in the `src_returns.csv` header: `[warn] ... column 'mobile_phone' not found in header — skipped.`;
  the file is still written (`anonymized_returns.csv` with `contact_name` anonymized) and the summary says
  `missing columns: mobile_phone`. The same happens under `abort`.
* `src_does_not_exist.csv`: `[skip] ... input not found` and `SKIPPED (input not found: ...)`. Under `abort` this would
  be a fatal `FileNotFoundException` instead.

## Not covered by a saved sample

These were verified by running the app on scratch data (results are described in the reference pages): unknown
`method` (whole run stops, `KeyNotFoundException` listing the known methods), `delimiter` of more than one
character (stops with `ArgumentException`), output equal to input (stops with `InvalidOperationException`),
zero-byte and header-only inputs, `hasHeader: false` losing the first data row, and the output-folder prompt for
configs that omit `outputFolder`.
