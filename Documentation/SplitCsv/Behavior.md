# Split CSV: behavior and edge cases

Every case below was run through the app (or is read directly from the code where stated). Prompts and options are
on the [Readme](Readme.md); runnable samples are in [samples](samples/README.md). "Part" means one output file.

## Contents

* [Row counting and boundaries](#row-counting-and-boundaries)
* [Empty and header-only files](#empty-and-header-only-files)
* [Exact multiples and remainders](#exact-multiples-and-remainders)
* [Line endings: CRLF, LF and CR](#line-endings-crlf-lf-and-cr)
* [Quoted fields and embedded newlines](#quoted-fields-and-embedded-newlines)
* [Encoding and BOM](#encoding-and-bom)
* [Header handling](#header-handling)
* [Uneven rows and blank lines](#uneven-rows-and-blank-lines)
* [File names and part numbering](#file-names-and-part-numbering)
* [Re-running and the output folder](#re-running-and-the-output-folder)
* [Other delimiters](#other-delimiters)
* [Malformed quoting](#malformed-quoting)
* [Prompt edge cases](#prompt-edge-cases)

## Row counting and boundaries

A *row* is one logical CSV record as parsed by the library's `CsvReader`: fields separated by commas, terminated by
a line break that is **not** inside a quoted field. The header is the first record and is never counted. A part holds
up to *N* rows; when it is full, the next row opens a new part. Counting is in records, so splitting at 2 rows never
cuts a multi-line cell in half.

## Empty and header-only files

| Input | Console | Files written |
|---|---|---|
| Zero-byte file (`empty.csv`) | `[split] empty.csv has no data rows — nothing written.`, then `0 data rows split across 0 file(s):` | None. The output folder is **created** (empty). |
| Header only (`header_only.csv`) | Same message | None. Output folder created (empty). |

A header on its own never produces a part: a part is only created when there is a first data row to put in it.
(The saved samples omit the empty folders because version control does not keep empty directories.)

## Exact multiples and remainders

| Data rows | Rows per file | Parts | Rows in each |
|---|---|---|---|
| 25 | 10 | 3 | 10, 10, 5 |
| 6 | 3 | 2 | 3, 3 (no empty third part) |
| 4 | 3 | 2 | 3, 1 |
| 3 | 1,000 | 1 | 3 |
| 10,002 | 1 | 10,002 | 1 each |

A new part is opened only when a row arrives and the current part is already full, so an exact multiple ends cleanly
with no trailing header-only file. When rows per file is larger than the file, there is a single part, which is a
normalized copy of the input.

## Line endings: CRLF, LF and CR

The reader accepts `\r\n`, `\n` and a lone `\r` as record terminators (a `\r\n` pair is one terminator). Every
record in every part is terminated with `\n`, a single line feed, with no `\r`. Verified: a file with CRLF endings
produced parts whose record separators are LF, and a file using bare `\r` endings split correctly into rows.

Line breaks **inside quoted cells are preserved exactly**, including `\r\n`, because they are part of the field
value (see below).

## Quoted fields and embedded newlines

Parsing follows RFC 4180: inside a quoted field the delimiter and line breaks are literal text, and a doubled quote
(`""`) is one literal quote. On output a field is quoted only when it contains a comma, a quote, `\r` or `\n`, and its
inner quotes are doubled.

From `input/quoted_tickets.csv` (BOM and CRLF in the source) split at 2 rows per file, three parts result:

| Part | Rows | Note |
|---|---|---|
| `quoted_tickets_part0001.csv` | tickets 9001, 9002 | Ticket 9001's message spans three lines (`\r\n` inside the quotes) and stays one row; 9002 contains `""wrong size""` |
| `quoted_tickets_part0002.csv` | 9003, 9004 | The blank line between 9002 and 9003 in the source is gone; 9004's message contains a bare `\n` |
| `quoted_tickets_part0003.csv` | 9005 | The source's last record has no trailing newline; it is still read |

Header repeated in each part, no BOM, record separators `\n`, and the cell breaks `\r\n`/`\n` unchanged
(`od -c` on the outputs shows this).

## Encoding and BOM

| Input | Result |
|---|---|
| UTF-8 with BOM | BOM removed from the first header cell; parts have no BOM. |
| UTF-8 without BOM | Read as UTF-8; written as UTF-8. |
| UTF-16 (with BOM) | Detected via the BOM and converted to UTF-8 output. |
| Latin-1 / other single-byte encodings with no BOM | Not detected; bytes above 0x7F that are not valid UTF-8 become the replacement character U+FFFD (`Caf\xE9` becomes `Caf` + `EF BF BD`). Convert to UTF-8 first. |

Output is always UTF-8 without a BOM. If a downstream tool (older Excel, for example) needs a BOM to detect UTF-8,
add one after splitting; the command has no option for it.

## Header handling

* The first record is the header. It is written at the top of every part.
* It is re-serialized, so a quoted header such as `"id","full name"` is written as `id,full name` (quotes only when
  needed).
* Duplicate column names are preserved as they are (`a,a,B` stays `a,a,B`), since the header is only passed
  through.
* There is no "no header" mode in the command. `CsvSplitter.Split` has an `includeHeader` parameter, but with
  `false` the library still consumes the first record as a header and drops it, so the first data row would be lost.
  The command always passes `true`.

## Uneven rows and blank lines

* **Blank lines** between records are dropped. They do not count as rows.
* **Rows with fewer or more fields than the header** are written with exactly the fields they had (no padding or
  truncation). Verified with `a,b,c` / `1,2` / `3,4,5,6` / `7,,`: parts contain `1,2`, `3,4,5,6` and `7,,` as they were.
  The splitter does not validate column counts.
* A row consisting only of delimiters (`,,`) is a real row, not a blank line.

## File names and part numbering

| Source name | Part names |
|---|---|
| `customers_2024_01.csv` | `customers_2024_01_part0001.csv`, `..._part0002.csv`, ... |
| `my.data.2024.csv` | `my.data.2024_part0001.csv` (only the last extension is split off) |
| `noext` (no extension) | `noext_part0001` (no extension added) |
| `data.PSV` | `data_part0001.PSV` (extension kept, case and all) |

Numbers are zero-padded to a minimum of four digits, so the 10,000th part is named `..._part10000` (five digits).
From part 10,000 on, a plain text sort no longer matches numeric order (`part10000` sorts before `part9999`), so use a numeric ("natural") sort, or pick a larger rows-per-file value to stay at or below 9,999 parts. Verified with a 10,002-row file at 1 row per file: the names ran `big_part9999.csv`,
`big_part10000.csv`, `big_part10001.csv`, `big_part10002.csv`.

## Re-running and the output folder

* The output folder is created if needed (`Directory.CreateDirectory`), including intermediate directories.
* Part files are created with `FileMode.Create`: an existing part with the same name is **overwritten** without
  warning.
* Other files in the folder are untouched. Parts from an earlier run that no longer exist in this run remain.
  Verified: with `s_part0001.csv` and a stale `s_part0009.csv` present, a new 2-row split at 1 row per file rewrote
  parts 0001 and 0002 and left 0009 untouched.
* Using the source's own folder as the output folder is allowed and safe: part names always contain `_partNNNN`, so
  the source cannot be overwritten (a source named `x_part0001.csv` yields `x_part0001_part0001.csv`).
* A split is not atomic. If the process dies mid-run, the parts written so far remain and the last one may be
  incomplete.

## Other delimiters

The command always parses with a comma delimiter and `"` quote. A pipe-, tab- or semicolon-delimited file is read as a
file with a single wide column, and quoting rules are applied with the wrong delimiter, which can silently alter
data. From `input/pipe_orders.psv`:

```text
5002|SKU-101|has, a comma      ->  5002|SKU-101|has, a comma        (the comma splits it into two fields, rejoined unchanged)
5003|SKU-102|"quoted|pipe"     ->  5003|SKU-102|quoted|pipe         (quotes stripped: the pipe is no longer protected)
```

Pipe files whose cells contain no quotes and no commas split correctly (the rows are rejoined unchanged); the
extension (`.psv`) is preserved. Anything with quoted cells will be corrupted, and quoted cells containing the
real delimiter lose their protection. The library supports other delimiters (`delimiter` and `quote` parameters of
`CsvSplitter.Split`), but the console command does not expose them. Convert the file to comma-separated first, or
call the library directly.

## Malformed quoting

A `"` that appears in the middle of an unquoted field (such as `5" nails`) starts quoted mode: the parser then reads on
until the next `"` or the end of the file, treating commas and line breaks as part of the field. Result for
`input/stray_quote.csv`: the three data rows are merged into **one** row whose last cell holds the rest of the file,
the stray quote is dropped, and the output cell is quoted:

```text
order_id,sku,note
5001,SKU-100,"5 nails
5002,SKU-101,fine
5003,SKU-102,also fine
"
```

Console summary: `1 data rows split across 1 file(s)` for a 3-row file. No error is raised, so compare the reported
row count with the expected one (`wc -l` for simple files). The remedy is to fix the source: quote the cell
and double the inner quote (`"5"" nails"`).

## Prompt edge cases

| Situation | Behavior |
|---|---|
| Glob without a directory (`customers_*.csv`) | `[warn] directory not found:` (the directory part is empty). Use `./customers_*.csv`; verified that `./cr*.csv` resolves with `[match] ./cr.csv`. |
| Glob with wildcard in the folder part | `[warn] directory not found: <dir>` |
| Glob matches nothing | `[warn] no files match: <glob>` |
| Glob matches several | Pick by number, or `b` to go back |
| Rows per file `0`, `-5`, `abc`, `1.5` | `[warn] enter a positive whole number.` and re-prompt |
| Rows per file `1,000` or `1000` | 1000 |
| Rows per file larger than the file | One part |
| Output folder left blank | `<source folder>/split` |
| Output folder `~/out` | Expanded to the home directory |
| `q` at the source prompt | Aborts with `[FAIL] No source CSV provided or it does not exist. Aborting.` |

The `[FAIL] <ExceptionType>: <message>` handler also catches unexpected exceptions (for example a locked output file
or permission error), printing the type, message and stack trace; no summary is printed and the exit code stays `0`.
