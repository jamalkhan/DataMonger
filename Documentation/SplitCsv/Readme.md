# Split CSV into N-row files

Splits one large CSV file into a series of smaller CSV files, each holding at most a fixed number of **data rows**
(100,000 by default). The header row is repeated at the top of every part so each part is a complete, valid CSV on
its own. The source file is read once, as a stream, so the command scales to multi-gigabyte inputs without loading
them into memory, and it never modifies or deletes the original.

It is a local, site-independent file transform: it does not call an API and does not use the site settings in
`appsettings.json` (the app still needs to start; see [Configuration](../Configuration.md)). The work is done by
`CsvSplitter.Split` in `bleak.Data.InfiniteDataLoader`, which uses the library's streaming `CsvReader` and
`CsvWriter`.

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | This page: prompts, naming, output format, remarks. |
| [Behavior.md](Behavior.md) | Verified edge cases: empty files, exact multiples, CRLF, quoted newlines, encodings, malformed input, re-runs. |
| [samples/README.md](samples/README.md) | Runnable inputs and real expected output. |

## Syntax / how to run

Start the app and choose **Split CSV into N-row files**. The menu is sorted alphabetically, so choose by name
rather than by number.

```text
Available commands (will run against all 1 site(s)):
  ...
  11) Split CSV into N-row files
  ...
Choice: 11
```

The command runs once (it does not run per site). A second invocation in the same process prints
`[skip] SplitCsv already ran this session (it is site-independent).`

### Prompts, in order

**1. Source CSV**

```text
Source CSV path (a glob like .../customers_*.csv is allowed), or 'q' to cancel:
```

| Answer | Result |
|---|---|
| Path to an existing file (absolute, relative to the current directory, or starting with `~/`); surrounding double quotes are trimmed | Used. |
| A glob containing `*` or `?` in the **file name** part | See [Glob support](#glob-support). |
| `q` (any case) | Cancels; prints `[FAIL] No source CSV provided or it does not exist. Aborting.` |
| Empty line | Ignored; the prompt repeats. |
| A path that does not exist | `[warn] file not found: <input>` and the prompt repeats. |
| End of input | Cancels, same as `q`. |

Only the source prompt accepts `q`. The next two prompts have no cancel option; Enter accepts their defaults.

**2. Output folder**

```text
Output folder for the split files [<folder of the source file>/split]:
```

Default: a folder named `split` next to the source file (the source's full path is resolved first, so the default is
displayed as an absolute path). Enter accepts the default; otherwise type an absolute, `~/`, or relative path (a
relative path is relative to the **current directory**). The folder is created if it does not exist.

**3. Rows per file**

```text
Rows per file [100,000]:
```

Default 100,000. A positive whole number: any commas are removed first, so `5,000` is read as 5000. Zero, negatives,
decimals and text print `[warn] enter a positive whole number.` and re-prompt. The count is of **data rows**,
not text lines, and does not include the header.

### Glob support

A source answer containing `*` or `?` is treated as a glob.

* The wildcard may only appear in the **file name**; the folder part must exist literally. `exports/customers_*.csv`
  works; `exp*/customers.csv` does not.
* A glob with **no directory part** (`customers_*.csv`) is not accepted and prints `[warn] directory not found:`
  (see the discrepancy below). Prefix it with `./` (`./customers_*.csv`) or use a full path.
* Matching is non-recursive, files only, and sorted by path ignoring case.
* **Exactly one match:** it is used directly (`[match] <path>`).
* **Several matches:** they are listed with numbers and you pick one: `Select 1-N, or 'b' to re-enter:`.
  `b` returns to the source prompt; an invalid answer repeats the selection prompt.
* **No match:** `[warn] no files match: <glob>` and the source prompt repeats.

The command processes exactly one file per run, even when the glob matches several. To split several files, run the
command several times.

```text
Source CSV path (a glob like .../customers_*.csv is allowed), or 'q' to cancel: input/customers_2024_*.csv
  2 files match:
    1) customers_2024_01.csv
    2) customers_2024_02.csv
  Select 1-2, or 'b' to re-enter: 2
```

## Inputs

* One delimited text file. The command always reads it as a **comma-separated** file with `"` as the quote
  character and the **first row as the header**. These are not configurable from the console (the library method has
  `delimiter`, `quote` and `includeHeader` parameters, but the command does not expose them; see [Behavior.md](Behavior.md#other-delimiters)).
* Encoding is auto-detected from a byte-order mark (UTF-8, UTF-16, UTF-32) and otherwise read as UTF-8.

## Outputs

* Zero or more part files in the output folder, named

  ```text
  <name>_part<NNNN><ext>        e.g.  customers_2024_01_part0001.csv
  ```

  where `<name>` is the source's file name without its **last** extension, `<ext>` is that extension including the
  dot (empty if there was none), and `NNNN` is the part number starting at 1, zero-padded to **at least** four
  digits (`part0001` ... `part9999`, then `part10000`, which no longer sorts correctly as text).
* Each part starts with the header row, followed by up to *rows per file* data rows. The last part holds the
  remainder, and no empty trailing part is written (an exact multiple produces exactly `rows / N` parts).
* Parts are written as UTF-8 **without a BOM**, with `\n` record terminators, quoting a field only when it
  contains a comma, a double quote, a carriage return or a line feed.

A run prints a line per part, then a summary:

```text
Source        : input/customers_2024_01.csv
Output folder : expected-output/customers
Rows per file : 10

[split] writing customers_2024_01_part0001.csv
[split] writing customers_2024_01_part0002.csv
[split] writing customers_2024_01_part0003.csv

=== Done ===
25 data rows split across 3 file(s):
    expected-output/customers/customers_2024_01_part0001.csv
    expected-output/customers/customers_2024_01_part0002.csv
    expected-output/customers/customers_2024_01_part0003.csv
Total elapsed: 00:00:00.011
```

`Source` is echoed as typed; `Output folder` and the file paths use the folder as typed or, for the default, its
absolute path. When there are no data rows the log says `[split] <file> has no data rows — nothing written.` and
the summary reads `0 data rows split across 0 file(s):`.

## Remarks

* **The original is never modified or deleted.** It is opened for reading only.
* **Rows, not lines.** Row boundaries are logical CSV records. A quoted cell with embedded line breaks counts as one
  row and is never cut in half by a split.
* **Header repetition.** The header is read from the first record of the file and written, re-quoted as needed, at
  the top of each part, so a part always parses on its own.
* **Re-running.** Parts that already exist are overwritten (same name = same part number). Parts left from an
  earlier run with *more* parts, or any other file in the folder, are **not** removed, so splitting the same file with a
  larger rows-per-file value into the same folder leaves stale higher-numbered parts behind. Use a fresh folder
  or clear the old one.
* **Output next to the source is safe.** Part names always include `_partNNNN`, so a part cannot collide with its
  own source file. The folder may even be the source's own folder.
* **Streaming and memory.** One record in memory at a time plus a 1 MiB read buffer and a 1 MiB write buffer for
  the current part. Throughput is bounded by disk speed; it is single threaded.
* **Normalization.** Because rows are re-serialized, the output is not a byte-exact slice of the input: line endings
  become `\n`, BOMs are dropped, optional quotes are removed, blank lines are dropped, and invalid bytes become
  U+FFFD. See [Behavior.md](Behavior.md) for each case.
* **Row counter limit.** Total row count is a 32-bit integer; inputs with more than about 2.1 billion data rows would
  overflow the reported count. (Splitting itself is unaffected until the part counter exceeds its own limits.)
* **Exit code.** The app's process exit code is `0` even after a `[FAIL]`; check the output.
* **No log file** is written; everything is on the console.

## Examples

Reproducible examples with real expected output are in [samples](samples/README.md):

| Example | Source | Rows/file | Result |
|---|---|---|---|
| 25 rows into 10 | `customers_2024_01.csv` | 10 | parts of 10, 10 and 5 rows |
| Exact multiple | `exact.csv` | 3 | 2 parts of 3 rows, no empty third part |
| Quoted newlines, CRLF, BOM | `quoted_tickets.csv` | 2 | 3 parts, embedded line breaks intact |
| Glob with several matches | `customers_2024_*.csv` | 3 | pick file 2 of 2 |
| Empty and header-only files | `empty.csv`, `header_only.csv` | 5 | no files written |
| A pipe-delimited file (a limitation) | `pipe_orders.psv` | 10 | quotes silently lost |
| A stray quote (a limitation) | `stray_quote.csv` | 1 | rows merged into one |

## Related

* [Anonymize PII](../AnonymizePii/Readme.md): another local, streaming file transform in the same app.
* [Extract / Transform / Load](../ExtractTransformLoad/Readme.md)
* [Configuration](../Configuration.md)
