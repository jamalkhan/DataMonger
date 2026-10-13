# File handling

How the [Anonymize PII](Readme.md) command reads, writes, names and protects files, and what it does when
something goes wrong. Config syntax is in [AnonConfig.md](AnonConfig.md).

## Contents

* [Per-file processing model](#per-file-processing-model)
* [Byte-copy fast path](#byte-copy-fast-path)
* [Streaming](#streaming)
* [What the rewritten file looks like](#what-the-rewritten-file-looks-like)
* [Output naming](#output-naming)
* [Refusing to overwrite a source](#refusing-to-overwrite-a-source)
* [Missing columns](#missing-columns)
* [Error policy](#error-policy)
* [Edge cases](#edge-cases)

## Per-file processing model

For each entry of `files`, in order, the engine:

1. Resolves the input path and the output path ([Output naming](#output-naming)).
2. If the input does not exist: skips it (`onError: skip`) or throws (`abort`).
3. Refuses if the output path equals the input path.
4. Creates the output directory if needed.
5. If `columns` is empty: copies the file byte for byte, then moves on.
6. Otherwise opens the input, looks up each configured column in the header, builds the rule list, and streams
   records through the rules into the output file.

Files are independent of each other except for the shared random stream used by
[`seed`](AnonConfig.md#seed). There is no parallelism.

## Byte-copy fast path

A job whose `columns` is empty (or omitted) is copied with `File.Copy(input, output, overwrite: true)`. Nothing is
parsed. Consequences:

* The copy is exactly identical to the source, including a UTF-8 BOM, `\r\n` line endings, quoting style, blank
  lines, trailing data and any bytes that are not valid text. The [basic sample](samples/README.md#1-basic-everything-in-one-run)
  copies an input with a BOM and CRLF endings, and `cmp` reports the files identical.
* The delimiter, quote and `hasHeader` settings are irrelevant for such a job.
* It costs only a disk copy, so multi-gigabyte files without PII are cheap.
* It logs `[copy] src_orders.csv -> anonymized_orders.csv (no PII columns)` and reports
  `copied (no PII columns)` in the summary.
* Input existence and the same-path check still apply. An existing output is overwritten.

A job whose `columns` are all misspelled (so none match) is **not** a copy: it is fully re-parsed and re-written
without changing any cell (see [Missing columns](#missing-columns)), which also normalizes the format as
described next.

## Streaming

The input is read with a forward-only RFC 4180 style parser that holds one record in memory at a time, and the
output is written record by record through a 1 MiB buffer. Memory use is flat regardless of file size; the
design target is inputs of 20 GB and more. The parser understands:

* the configured delimiter and quote character;
* delimiters and line breaks inside quoted fields;
* doubled quotes (`""`) as a literal quote inside a quoted field;
* `\r\n`, `\n` and a lone `\r` as record terminators;
* a final record without a trailing newline.

Encoding on read: UTF-8 by default, with BOM detection (UTF-8, UTF-16 and UTF-32 byte-order marks are honored and
removed). Bytes that are not valid UTF-8 and have no BOM are decoded as the replacement character U+FFFD, so a
Latin-1 file is damaged on re-write; convert such files to UTF-8 first (or, if they contain no PII, give them an
empty `columns` list so they are copied untouched).

## What the rewritten file looks like

A file that goes through the anonymizer is regenerated, not patched, so it can differ from the source even in
cells you did not target:

| Aspect | Behavior |
|---|---|
| Encoding | UTF-8 **without** a BOM, always. |
| Record terminator | `\n` (LF), always, even if the source used CRLF. |
| Line breaks inside quoted fields | Preserved exactly as they were (`\r\n` inside a quoted cell stays `\r\n`). |
| Quoting | Minimal. A field is quoted only if it contains the delimiter, the quote character, `\r` or `\n`; quotes that were optional in the source disappear, and quotes inside a field are doubled. |
| Header | Written first, unchanged (with the reader's decoding, so BOM removed). Quoting follows the minimal rule. |
| Blank lines | Dropped. |
| Short or long rows | Written with their original number of fields. Rows are **not** padded or truncated. |
| Delimiter and quote | The same characters used to read are used to write. |
| Cell values of untouched columns | Identical text (subject to the quoting rule above). |

Real example from the samples: a ticket file with a BOM, CRLF line endings, a blank line and a cell containing
`\r\n` goes in; the output has no BOM, `\n` between records, no blank line, and the cell's inner `\r\n` intact.

The delimiter shorthand is applied to both reading and writing, so there is no way to convert between delimiters
with this command.

## Output naming

The output path for each job is computed like this:

1. **`output` is set and is a path** (rooted, starts with `~`, or contains `/` or `\`): resolved against the
   config file's folder and used as is. The output folder is not involved.
2. **`output` is set and is a bare file name:** that name inside the output folder.
3. **`output` is not set:** the input's file name (folder discarded) with the prefix rule applied, inside the output
   folder.

The prefix rule (`DeriveOutputName`):

| Condition | Result |
|---|---|
| Name starts with `run.inputPrefix` (case-insensitive) and `inputPrefix` is not empty | `outputPrefix` + name without the prefix |
| Otherwise | `outputPrefix` + the whole name |

With the defaults: `src_customers.csv` becomes `anonymized_customers.csv`; `customers.csv` also becomes
`anonymized_customers.csv`; `SRC_Orders.csv` becomes `anonymized_Orders.csv`.

The output folder is the config's `outputFolder`, else the folder typed at the prompt. The folder (and any
directory part of an explicit output path) is created automatically. Two jobs that yield the same output path do not
conflict: the later one silently overwrites the earlier one.

## Refusing to overwrite a source

Before writing, the engine compares the full input path with the full output path (case-insensitively on
Windows and macOS, case-sensitively elsewhere). If they are equal it throws

```text
Output path equals input path for '<path>'. Configure a distinct outputFolder — originals must be preserved.
```

This is treated as a configuration error: the run stops regardless of `onError`, and jobs that ran earlier keep
their outputs. This is the check that guarantees originals stay intact. It is **by file path only**: an output
folder equal to the input folder is accepted as long as the resulting file names differ (for example the default
`src_` to `anonymized_` rename). Two other things the check does not cover: different path spellings that reach the
same file through a symlink are not detected, and an output that overwrites a *different* input of the same
config (for example job B writing to job A's input path) is not detected either. Keep the output folder separate
from the input folder.

Input files are opened read-only with `FileShare.Read`, so they are never locked against other readers and never
written to.

## Missing columns

A configured `name` that is not in the header:

* is logged once per file: `[warn] src_returns.csv: column 'mobile_phone' not found in header — skipped.`
* is listed in the summary line for the file: `anonymized_returns.csv: 3 rows, 3 cells anonymized, missing columns: mobile_phone`
* does not stop the file or the run, **under both `skip` and `abort`**, and does not count as an error.

The other rules for the file still apply. Because a typo here fails open (the column stays unanonymized),
check the log for `[warn]` lines and the `missing columns` text in the summary, especially for scripted runs.
If every rule of a file is missing, the file is still rewritten (normalized) with no cells changed and no copy.

The lookup is case-insensitive; the header itself is not modified.

## Error policy

Failures fall into three classes. Only the first is controlled by [`run.onError`](AnonConfig.md#onerror).

| Failure | Detected | `skip` | `abort` |
|---|---|---|---|
| A cell throws while being anonymized (for example `fakeDate` with `minYear: 0` or a bad `format`) | Per cell | `[warn] <file> row N column '<name>': <message> (left unchanged)`; cell keeps its source value; counted in `error(s)`; continue | Throws; run stops; the partially written file stays on disk |
| An input file is missing | Per file | `[skip] <file>: input not found: <path>`; reported as `SKIPPED (...)`; continue with the next file | Throws `FileNotFoundException`; run stops |
| A configured column is not in the header | Per file | Warn and skip the column | Same: warn and skip the column |
| Unknown `method`, unrecognized `delimiter`, output path equal to input path, unparsable config, no output folder available | Per file (or at load time) | Run stops | Run stops |

When the run stops, the command prints `[FAIL] <ExceptionType>: <message>` and a stack trace; no summary is
printed, so you will not see per-file totals for files that did succeed (they remain on disk). Because the message
of a cell failure under `abort` does not include the row or column, rerun with `skip` to locate them
(the warnings carry both).

With `skip`, a **failed cell keeps its original, unanonymized value**. If `fakeDate` throws for every cell, the
whole column leaves the file as real PII. Treat any `error(s)` count in the summary as a failure to fix, not
noise. The [bad-cell samples](samples/README.md#4-bad-cell-skip-vs-abort) show both behaviors on the same data:
under `skip` the `dob` column is returned unchanged and `phone` is anonymized; under `abort` the output holds only
the header row.

The process exit code is always `0`.

## Edge cases

| Situation | Behavior |
|---|---|
| Empty (zero-byte) input with rules | Output is created and is empty (no header, no rows); `0 rows`. Column rules are all reported as missing. |
| Header only | Output has just the header; `0 rows`. |
| Duplicate header names | The last column with that name is the one a rule targets. |
| Same column in two rules | Applied in order; the second rule's input is the first rule's output. |
| Row shorter than the column's index | The column is skipped for that row; the row stays short. |
| Cell is empty | Left alone with `passthrough`; replaced with `generate`. A whitespace-only cell is not empty. |
| Stray quote character in an unquoted field (`5" nails`) | The parser starts quoted mode at that quote and runs on until the next quote (or end of file), merging following rows into one field, and the quote itself is lost. Make sure the `quote` setting matches the file. |
| File uses a different quote than `"` | Set `quote`. Only its first character is used. |
| Tab/pipe/semicolon files | Set `delimiter`; the same one is used for output. |
| `hasHeader: false` | Not supported correctly; see [AnonConfig.md](AnonConfig.md#hasheader). |
| Output file already exists | Overwritten. |
| Input in a different folder from the config | Fine; use `inputFolder` or absolute paths. |
