# Split CSV: samples

Runnable inputs and the real output of running them through the app. All data is invented. Docs:
[Readme](../Readme.md), [Behavior](../Behavior.md).

## Layout

```text
samples/
  input/
    customers_2024_01.csv   25 data rows (customer_id, names, e-mail, city, lifetime_spend)
    customers_2024_02.csv   4 data rows (second file for the glob example)
    exact.csv               6 data rows (exact multiple of 3)
    quoted_tickets.csv      BOM, CRLF endings, cells with embedded line breaks and quotes, a blank line,
                            and no trailing newline on the last record
    empty.csv               zero bytes
    header_only.csv         header row only
    pipe_orders.psv         pipe-delimited (shows a limitation)
    stray_quote.csv         unbalanced quote in an unquoted cell (shows a limitation)
  expected-output/
    customers/ exact/ quoted/ glob/ pipe/ stray-quote/     the parts written by each run
    console/<scenario>.txt                                  what the app printed (see below)
```

`quoted_tickets.csv` deliberately has CRLF line endings. If your version control converts line endings on checkout
(`core.autocrlf`), mark it binary or re-create it, or the example will not reproduce exactly.

## How to reproduce

Run from this `samples` folder with the built app, answering the three prompts (source, output folder, rows per
file). Menu entry **Split CSV into N-row files** is number 11 at the time of writing; the menu is alphabetical, so
check the list the app prints.

```sh
cd Documentation/SplitCsv/samples
printf '11\ninput/customers_2024_01.csv\nexpected-output/customers\n10\n' | dotnet path/to/DataMonger.dll
```

The app needs its usual `appsettings.json` and `products.csv` to start (see [Configuration](../../Configuration.md));
the command uses neither. The output is deterministic (there is no randomness), so rerunning reproduces the
committed files exactly.

`expected-output/console/*.txt` are the console transcripts from the `=== Split CSV ...` header on. The
`Total elapsed` line (varies per run) is removed and the absolute path of this folder is replaced by `<samples>`
(it appears in the default-output-folder hint of the second prompt).

## Scenarios

| # | Prompt answers (source / output folder / rows) | Result | Look for |
|---|---|---|---|
| 1 | `input/customers_2024_01.csv` / `expected-output/customers` / `10` | 3 parts: 10, 10, 5 rows (`customers_2024_01_part0001.csv`...) | The same header at the top of each part; row ids 1001-1010, 1011-1020, 1021-1025. Transcript: `console/customers.txt` |
| 2 | `input/exact.csv` / `expected-output/exact` / `3` | 2 parts of 3 rows | No empty third part. `console/exact.txt` |
| 3 | `input/quoted_tickets.csv` / `expected-output/quoted` / `2` | 3 parts (2, 2, 1 rows) | Ticket 9001's three-line message stays one row; the blank line is dropped; no BOM; LF between records, CRLF kept inside the quoted cell. `console/quoted.txt` |
| 4 | `input/customers_2024_*.csv` / `2` (menu pick) / `expected-output/glob` / `3` | Picks `customers_2024_02.csv` from the two matches; 2 parts (3, 1 rows) | The numbered match list. `console/glob.txt` |
| 5 | `input/empty.csv` or `input/header_only.csv` / any folder / `5` | `0 data rows split across 0 file(s)`; nothing written (the folder is created, empty) | `console/empty.txt`, `console/header-only.txt` |
| 6 | `input/pipe_orders.psv` / `expected-output/pipe` / `10` | 1 part, but the quotes around `"quoted\|pipe"` are lost | Limitation: the command always reads commas. `console/pipe.txt` |
| 7 | `input/stray_quote.csv` / `expected-output/stray-quote` / `1` | 3 rows reported as `1 data rows`, merged into one cell | Limitation: unbalanced quote. `console/stray-quote.txt` |

For scenario 4 the source answer is followed by `2` at the `Select 1-2, or 'b' to re-enter:` prompt:

```sh
printf '11\ninput/customers_2024_*.csv\n2\nexpected-output/glob\n3\n' | dotnet path/to/DataMonger.dll
```

Quick checks after a run:

```sh
wc -l expected-output/customers/*.csv     # 11, 11, 6 lines (header + 10/10/5 rows)
head -1 expected-output/customers/*.csv   # identical header in every part
```

Not saved as samples but verified on scratch data (see [Behavior.md](../Behavior.md)): CR-only line endings,
Latin-1 and UTF-16 inputs, uneven rows, a file with no extension, more than 9,999 parts, stale parts left by an earlier
run, and the glob-without-directory warning.
