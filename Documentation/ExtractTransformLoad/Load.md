# Load settings (`load`)

Each output has a `load` object that controls **where and how its records are written**: the format, the folder,
the file names, how files are split, and the optional "eyeball" samples. All properties are optional.

```jsonc
"load": {
  "format": "jsonl",
  "folder": "output/{run_timestamp}/customers",
  "fileBaseName": "customers",
  "splitEvery": 1000000,
  "sampling":     { "enabled": true,  "count": 50, "folder": "sampling",      "fileNameFields": [ "user_id", "first_name", "last_name" ] },
  "richSampling": { "enabled": false, "count": 50, "folder": "sampling/full", "fileNameFields": [ "user_id", "first_name", "last_name" ] }
}
```

| Property | Type | Default | Section |
| --- | --- | --- | --- |
| [`format`](#format) | string | `"jsonl"` | Output format (only JSONL exists) |
| [`folder`](#folder) | string | none (prompted) | Output folder |
| [`fileBaseName`](#filebasename) | string | `"output"` | First part of every file name |
| [`splitEvery`](#splitevery) | integer | `1000000` | Records per file |
| [`sampling`](#sampling) | object | **enabled** | Random sample of records as pretty JSON files |
| [`richSampling`](#richsampling) | object | disabled | The most fully populated records as pretty JSON files |

Contents: [File names](#file-names) · [JSON formatting](#json-formatting) · [Examples](#examples)

---

### format

| | |
| --- | --- |
| **Type** | string |
| **Default** | `"jsonl"` |
| **Allowed values** | `"jsonl"` |

The only writer today is JSONL (one compact JSON document per line). The engine's `IOutputWriter` is the seam
for other formats, but the `format` value is **not read**: any string is accepted and JSONL is written anyway.
Do not rely on it to select a format.

---

### folder

The folder this output writes into.

| | |
| --- | --- |
| **Type** | string |
| **Required** | No |
| **Default** | none: the command prompts for a fallback folder (only if some output lacks one) |

#### Remarks

* **Path rules:** a leading `~` expands to the home folder; a **relative path is relative to the `.etlconfig`
  file's folder** (not the working directory); absolute paths are used as given. A blank value counts as not set.
* **The per-run subfolder.** Files are never written straight into `folder`. A subfolder named
  `run_<yyyyMMdd_HHmmss>` (the run timestamp) is created for every run so successive runs do not mix:

  | `folder` | Files land in |
  | --- | --- |
  | `"output"` | `<config folder>/output/run_<ts>/` |
  | `"output/{run_timestamp}/customers"` | `<config folder>/output/run_<ts>/customers/` |
  | not set; prompt answered `output/fallback` | `<config folder>/output/fallback/run_<ts>/` |

* **The `{run_timestamp}` token.** If `folder` contains the literal text `{run_timestamp}` it is replaced by
  `run_<ts>` and **no extra subfolder is appended**. Use it to place the run folder in the middle of the path so
  that several outputs share one per-run folder (`output/{run_timestamp}/customers`, `output/{run_timestamp}/orders`).
  The token may appear anywhere in the path; only that exact spelling is recognized.
* All outputs of one run use the same timestamp.
* Every side file (`_rejects.csv`, `_filtered.csv`, `_warnings.csv`) and sample folder is created inside the same
  resolved folder as the JSONL files.
* Missing folders are created (including parents).
* The fallback folder prompt (`Output folder for the .jsonl files [~/Desktop]:`) is covered in the
  [Readme](Readme.md#prompts-in-order). With `~/Desktop` as the default answer a bare Enter writes to your Desktop.

#### Example

```jsonc
"load": { "folder": "output/{run_timestamp}/orders", "fileBaseName": "orders" }
```

Sample: [14-load-folders](samples/14-load-folders/load-folders.etlconfig) (token, per-output folders and the prompt).

---

### fileBaseName

The base of every file name written for this output.

| | |
| --- | --- |
| **Type** | string |
| **Default** | `"output"` |

#### Remarks

* Used verbatim in `<fileBaseName>_<timestamp>[_<n>].jsonl` and in the side-file names
  (`..._rejects.csv`, `..._filtered.csv`, `..._warnings.csv`).
* Do not put path separators or characters that are illegal in file names in it.
* **Give every output a distinct `fileBaseName`** (or folder). Two outputs that resolve to the same file name in the
  same run folder overwrite each other silently; with the default `output` that happens as soon as two outputs omit
  the setting.

---

### splitEvery

Roll over to a new file after this many records.

| | |
| --- | --- |
| **Type** | integer (64-bit) |
| **Default** | `1000000` |
| **Allowed values** | 1 or more (zero or negative fails at the first record: `recordsPerFile must be greater than zero.`) |

#### Remarks

* The count is **records per file**, per output. A new file starts only when another record is about to be
  written, so an exact multiple of `splitEvery` does not leave an empty last file.
* To get exactly one file of at most N records, set [`run.maxRecords`](RunOptions.md#maxrecords) and `splitEvery` to the same N.
* Records are never split across files. Per-file size depends on the records.
* Under `externalMerge` the primary's key order determines which records fall in which file.

#### Example

[samples/09-run-limits/max-records.etlconfig](samples/09-run-limits/max-records.etlconfig): `maxRecords: 5`,
`splitEvery: 2` gives `customers_<ts>_1.jsonl`, `_2.jsonl` (2 records each) and `_3.jsonl` (1 record).

---

### sampling

Writes a **uniform random sample** of N output records as individual pretty-printed JSON files, so you can look
at a few real records without opening a multi-gigabyte `.jsonl`. **It is on by default.**

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `enabled` | bool | `true` | Turn the sample off with `"sampling": { "enabled": false }` |
| `count` | integer | `50` | How many records to sample (zero or less: none) |
| `folder` | string | `"sampling"` | Subfolder of the output's run folder |
| `fileNameFields` | array of string | `["user_id", "first_name", "last_name"]` | Top-level output fields whose values name each file |

#### Remarks

* **Object-mode outputs only.** keyedMap outputs never write samples.
* The sample is drawn from the records **actually written** (after filters, `requireRelations`, `distinctBy` and
  skipped rows), using reservoir sampling, so each written record is equally likely to be chosen even though the
  number of records is not known in advance. If there are fewer records than `count`, **all** of them are kept.
* The sample is **random**: two runs over the same data pick different records (when there are more records than
  `count`). It is rewritten, not appended; each run has its own folder.
* Each sample is the record re-rendered with indentation (2 spaces; no trailing newline). The files are written
  when the output finishes.
* **File names.** The values of the `fileNameFields` that exist at the **top level** of the record (string, number
  or boolean values that are not empty) are joined with `_` and given a `.json` extension:
  `["customer_id", "last_name"]` gives `1001_Lovelace.json`. Characters illegal in file names become `-`. If a name
  is already taken, `_2`, `_3`, ... is added. **If none of the fields is present, the file name is a random GUID**,
  so list fields your records actually have. (The defaults `user_id`, `first_name`, `last_name` fit a user-profile
  feed; most other feeds will want their own.)
* `folder` is relative to the run folder; an absolute path here would be used as is.
* Because the setting is on by default, **every object-mode output produces a `sampling/` folder of up to 50
  files**. Disable it for production feeds or large runs you do not want to clutter.

#### Example

```jsonc
"sampling": { "enabled": true, "count": 2, "folder": "random-sample", "fileNameFields": [ "customer_id", "last_name" ] }
```

Sample: [08-load-sampling](samples/08-load-sampling/load-sampling.etlconfig) (the files in its `random-sample/`
folder are one possible random outcome).

#### See also

[richSampling](#richsampling), [`run.sample`](RunOptions.md#sample) (restricts the *input*, a different feature)

---

### richSampling

Writes the **N richest records**, those with the most populated nested structures, as individual pretty-printed
JSON files. Where `sampling` shows what a *typical* record looks like, `richSampling` shows what a *fully
populated* one looks like.

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `enabled` | bool | `false` | **Off by default** |
| `count` | integer | `50` | How many records to keep |
| `folder` | string | `"sampling/full"` | Subfolder of the run folder |
| `fileNameFields` | array of string | `["user_id", "first_name", "last_name"]` | As for `sampling` |

#### Remarks

* **Object-mode only**, independent of `sampling` (either, both or neither may be enabled).
* **Richness score.** For each record the engine counts every relationship, at any depth, that has at least one
  matching child after its filters/`distinctBy`/`limit`, and adds the score of each matching child's own
  relationships (inside object groups too). Scalar fields, lookups and the number of children do not count, only
  *populated relationships*. A customer with orders (each with lines) outscores one with orders but no lines.
* The top `count` scores are kept; a later record must score **strictly higher** than the current worst kept
  record to replace it, so among equal scores the earliest records win. The result is deterministic for a given
  input order.
* File names and uniqueness work exactly as for [`sampling`](#sampling).

#### Example

```jsonc
"richSampling": { "enabled": true, "count": 2, "folder": "richest-records", "fileNameFields": [ "customer_id", "last_name" ] }
```

[08-load-sampling](samples/08-load-sampling/load-sampling.etlconfig): customer 1001 (orders and emails) and 1002
are the richest.

---

## File names

For an output with `fileBaseName` `B` and run timestamp `T` (`yyyyMMdd_HHmmss`), in the run folder:

| File | Name |
| --- | --- |
| Records, one file | `B_T.jsonl` |
| Records, split into several | `B_T_1.jsonl`, `B_T_2.jsonl`, ... (the ordinal starts at 1, unpadded) |
| Skipped rows | `B_T_rejects.csv` |
| Filtered-out rows | `B_T_filtered.csv` |
| Field warnings | `B_T_warnings.csv` |
| Random sample | `<sampling.folder>/<fileNameFields joined by _>.json` |
| Rich sample | `<richSampling.folder>/<...>.json` |

A single-file result is first written as `B_T_1.jsonl` and renamed to `B_T.jsonl` when the output closes; so the
final report always shows the final names. An output with **no records writes no `.jsonl` file** (the report says
`0 records across 0 file(s)`), but the run folder is created. The side files exist only when something was
rejected/filtered/warned.

## JSON formatting

* **One compact document per line**, no spaces, terminated by `\n` (also on Windows); UTF-8 **without** a byte-order
  mark.
* **Relaxed escaping:** `@ + ' < > &` and non-ASCII letters (`é`, `日本`) are written literally rather than as
  `\uXXXX`; characters outside the Basic Multilingual Plane (emoji) are still written as `\uD83D\uDE00`-style surrogate
  pairs (valid JSON). Quotes, backslashes and control characters (including newlines inside a value, written `\n`) are still escaped,
  so every record stays on one line.
* Numbers are written as JSON numbers (decimals keep the scale of their source text); dates/datetimes as shown in
  [Outputs.md#type](Outputs.md#type).
* Property order is the order of the config ([Outputs.md#fields](Outputs.md#fields)).

## Examples

| Sample | Load features |
| --- | --- |
| [14-load-folders](samples/14-load-folders/load-folders.etlconfig) | `{run_timestamp}`, fallback folder, `splitEvery` |
| [09-run-limits/max-records](samples/09-run-limits/max-records.etlconfig) | `splitEvery` with `maxRecords` |
| [08-load-sampling](samples/08-load-sampling/load-sampling.etlconfig) | `sampling`, `richSampling` |

## See also

[Readme.md](Readme.md#output-folder-and-run-timestamp), [RunOptions.md](RunOptions.md), [Errors.md](Errors.md)
