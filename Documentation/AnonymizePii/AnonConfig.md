# `.anonconfig` reference

A `.anonconfig` file is a JSON document that describes one anonymization run. Part of the
[Anonymize PII](Readme.md) command; the per-method options are in [Anonymizers.md](Anonymizers.md).

## Contents

* [File format](#file-format)
* [Document shape](#document-shape)
* [Top-level properties](#top-level-properties): `version`, `run`, `inputFolder`, `outputFolder`, `files`
* [`run` properties](#run-properties): `onError`, `seed`, `inputPrefix`, `outputPrefix`, `emailDomains`
* [`files[]` properties](#files): `input`, `output`, `delimiter`, `quote`, `hasHeader`, `columns`
* [`columns[]` properties](#columns): `name`, `method`, `options`, `whenEmpty`
* [Path resolution](#path-resolution)

## File format

* Standard JSON, with `//` line comments, `/* */` block comments and trailing commas allowed.
* Property names are **case-insensitive** (`onError`, `onerror` and `OnError` all work).
* Enum values (`onError`, `whenEmpty`) are written as camelCase strings and are matched case-insensitively;
  numbers are rejected (`"onError": 1` fails).
* **Unknown properties are silently ignored**, so a typo such as `"outputfolder"` is fine (it is case-insensitive)
  but `"outputFolders"` is dropped without a warning. Check the console header (`Output folder`, `Rename`, `On
  error`) to confirm what was read.
* A syntax error or an invalid enum value makes the command print `[FAIL] Could not parse config: <message>` and stop
  before touching any file.

## Document shape

```jsonc
{
  "version": 1,
  "run": {
    "onError": "skip",
    "seed": 42,
    "inputPrefix": "src_",
    "outputPrefix": "anonymized_",
    "emailDomains": [ "springfield.example" ]
  },
  "inputFolder": "../input",
  "outputFolder": "../output",
  "files": [
    {
      "input": "src_customers.csv",
      "output": "anonymized_customers.csv",
      "delimiter": "csv",
      "quote": "\"",
      "hasHeader": true,
      "columns": [
        { "name": "email", "method": "simpsonsEmail", "options": { "group": "person" }, "whenEmpty": "passthrough" }
      ]
    }
  ]
}
```

## Top-level properties

### version

Schema version marker.

| | |
|---|---|
| **Type** | integer |
| **Required** | No |
| **Default** | `1` |
| **Allowed values** | Any integer (not validated) |

**Remarks.** Reserved for future use. The loader stores the value but nothing reads it, so it has no effect today.
Keep it at `1`.

**Example**

```jsonc
{ "version": 1 }
```

### run

Run-wide settings shared by every file job. See [`run` properties](#run-properties).

| | |
|---|---|
| **Type** | object |
| **Required** | No |
| **Default** | All `run` defaults below |

### inputFolder

Base folder for relative `files[].input` paths.

| | |
|---|---|
| **Type** | string (path) |
| **Required** | No |
| **Default** | Not set: relative inputs resolve against the config file's own folder |
| **Allowed values** | Absolute path, `~/...`, or a path relative to the config file's folder |

**Remarks.** Whitespace-only counts as not set. A job's `input` that is itself an absolute path ignores this
folder. See [Path resolution](#path-resolution).

**Example**

```jsonc
{ "inputFolder": "../input" }
```

**See also:** [files[].input](#input), [Path resolution](#path-resolution).

### outputFolder

Default destination folder for every output file.

| | |
|---|---|
| **Type** | string (path) |
| **Required** | No (but see Remarks) |
| **Default** | Not set: the command prompts for one |
| **Allowed values** | Absolute path, `~/...`, or a path relative to the config file's folder |

**Remarks.**

* When set (non-blank), the command does not prompt for an output folder.
* When not set and the command is used, the folder typed at the prompt is used. If a job needs an output folder and
  none is available (only possible when calling the library directly with no override), the run fails with
  `No output folder configured and none supplied for '<input>'`.
* The folder is created if it does not exist.
* A job whose `output` is a path (contains `/` or `\` or is rooted) bypasses this folder; see [output](#output).
* The folder may even equal the input folder, because the default rename (`src_` to `anonymized_`) gives a
  different file name. The only hard rule enforced is that an output path may not equal its own input path. Pointing
  the output at a different folder is still the recommended practice.

**Example**

```jsonc
{ "outputFolder": "../expected-output/basic" }
```

**See also:** [Output naming](FileHandling.md#output-naming), [FileHandling: refusing to overwrite a source](FileHandling.md#refusing-to-overwrite-a-source).

### files

The list of file jobs, one per input file, processed in order.

| | |
|---|---|
| **Type** | array of [file job](#files-1) objects |
| **Required** | Yes in practice (an empty list is valid and does nothing) |
| **Default** | `[]` |

**Remarks.** Jobs run strictly in the listed order. The same input may appear in several jobs (for example to
write two differently-anonymized variants; give each an explicit `output`). Two jobs that resolve to the same
output path silently overwrite one another.

## `run` properties

### onError

What to do when anonymizing a single cell throws.

| | |
|---|---|
| **Type** | string (enum) |
| **Required** | No |
| **Default** | `skip` |
| **Allowed values** | `skip`, `abort` |

**Remarks.**

* `skip`: log `[warn] <file> row N column '<name>': <message> (left unchanged)`, leave that cell as it was in the
  source, count it in the file's error total, and carry on with the next cell.
* `abort`: rethrow. The command prints `[FAIL] <ExceptionType>: <message>` and stops; later files are not
  processed. The file being written is left on disk with only the rows written so far (possibly just the header).
  The failure message does not name the file, row or column.
* `onError` also decides what happens when an **input file is missing**: under `skip` the file is logged
  (`[skip] ...`) and reported as `SKIPPED`; under `abort` the run stops with a `FileNotFoundException`.
* `onError` does **not** govern configuration problems: an unknown `method`, an unrecognized `delimiter`, or an
  output path equal to the input path stop the whole run in both modes, and a missing column is always a warning.
  See [FileHandling.md](FileHandling.md#error-policy).
* Which cells can actually throw? The built-in anonymizers are defensive; in practice only `fakeDate` with an
  invalid `format` string or a `minYear`/`maxYear` outside 1..9999 does. Custom anonymizers can throw for any reason.

**Example**

```jsonc
{ "run": { "onError": "abort" } }
```

**See also:** [Error policy](FileHandling.md#error-policy), [samples bad-cell-skip / bad-cell-abort](samples/README.md#4-bad-cell-skip-vs-abort).

### seed

Seed for the random number generator.

| | |
|---|---|
| **Type** | integer (32-bit signed) |
| **Required** | No |
| **Default** | Not set: a time-based seed is used and every run differs |
| **Allowed values** | Any `int`, including `0` and negatives |

**Remarks.** With a seed, the same config, inputs and library version produce byte-identical output on every run
(verified in the samples by running twice and diffing). One random stream is shared by the whole run, in
the order: files as listed, rows top to bottom, columns in the order they appear in the `columns` array. Changing
that order, adding a column or file, or changing a cell from empty to non-empty (an empty cell with
`passthrough` consumes no randomness) changes every value generated afterwards. Use a seed for demos, tests and
reproducible bug reports; omit it for real anonymization runs when you do not want fakes to be guessable from the
config.

**Example**

```jsonc
{ "run": { "seed": 42 } }
```

**See also:** [Determinism](Anonymizers.md#determinism).

### inputPrefix

Prefix stripped from an input file name when deriving its output name.

| | |
|---|---|
| **Type** | string |
| **Required** | No |
| **Default** | `src_` |
| **Allowed values** | Any string; `""` disables stripping |

**Remarks.** Matched case-insensitively at the start of the file name. If the name does not start with the prefix,
nothing is stripped. Used only when the job has no `output`.

**Example**

```jsonc
{ "run": { "inputPrefix": "raw_", "outputPrefix": "clean_" } }   // raw_users.csv -> clean_users.csv
```

**See also:** [outputPrefix](#outputprefix), [Output naming](FileHandling.md#output-naming).

### outputPrefix

Prefix put in front of the derived output name.

| | |
|---|---|
| **Type** | string |
| **Required** | No |
| **Default** | `anonymized_` |
| **Allowed values** | Any string; `""` adds nothing |

**Remarks.** The prefix is added whether or not `inputPrefix` matched: `customers.csv` (no `src_`) becomes
`anonymized_customers.csv`. Setting both prefixes to `""` yields the input's own name, which is refused when the
output folder is the input folder.

**Example**

```jsonc
{ "run": { "outputPrefix": "anonymized_" } }
```

### emailDomains

Pool of fake domains used by `simpsonsEmail`.

| | |
|---|---|
| **Type** | array of strings |
| **Required** | No |
| **Default** | `["springfield.example"]` |
| **Allowed values** | Any domain strings; an empty list falls back to the default |

**Remarks.** For each generated address one domain is picked at random from the pool, unless the column sets its
own `domain` option (which wins and consumes no randomness). Use reserved or invented domains (`.example`,
`example.com`) so fake addresses can never reach a real mailbox.

**Example**

```jsonc
{ "run": { "emailDomains": [ "shop-a.example", "shop-b.example" ] } }
```

**See also:** [simpsonsEmail](Anonymizers.md#simpsonsemail).

## files[]

<a id="files"></a>

Each element of `files` describes one input file.

### input

Path of the file to read.

| | |
|---|---|
| **Type** | string (path) |
| **Required** | Yes |
| **Default** | none (an empty string resolves to the input folder itself and fails as "input not found") |
| **Allowed values** | Relative to `inputFolder` (or the config's folder when that is unset), absolute, or `~/...` |

**Remarks.** If the file does not exist the job is skipped or the run aborts, per [`onError`](#onerror). No glob
support: one job names one file.

**Example**

```jsonc
{ "input": "src_customers.csv" }
```

### output

Optional explicit output path.

| | |
|---|---|
| **Type** | string |
| **Required** | No |
| **Default** | Derived from the input name: `inputPrefix` swapped for `outputPrefix`, placed in the output folder |
| **Allowed values** | See below |

**Remarks.** Three forms:

| Value | Result |
|---|---|
| Omitted or blank | Derived name inside the output folder. |
| A bare file name (no `/` or `\`), e.g. `"customers_clean.csv"` | That name inside the output folder. |
| A rooted path, a `~/...` path, or any value containing `/` or `\` (e.g. `"exports/clean.csv"`) | Resolved against the **config file's folder** (not the output folder) and used verbatim. |

The containing directory is created if needed.

**Example**

```jsonc
{ "input": "src_customers.csv", "output": "customers_clean.csv" }
```

**See also:** [Output naming](FileHandling.md#output-naming).

### delimiter

Field delimiter of this file (the same character is used when writing).

| | |
|---|---|
| **Type** | string |
| **Required** | No |
| **Default** | `csv` (comma) |
| **Allowed values** | `csv` or `comma` (`,`); `tsv`, `tab` or `\t` (tab); `pipe` (`|`); `semicolon` (`;`); or any single character |

**Remarks.** Shorthand names are case-insensitive and trimmed. Any other string longer than one character makes
the run fail with `Unrecognized delimiter '<value>'. Use csv|tsv|pipe|semicolon or a single char.`. Note that the
single-character fallback test uses the untrimmed value, so `" ;"` is rejected.

**Example**

```jsonc
{ "input": "src_newsletter.psv", "delimiter": "pipe" }
```

### quote

Text qualifier character.

| | |
|---|---|
| **Type** | string |
| **Required** | No |
| **Default** | `"` (double quote) |
| **Allowed values** | Any string; only its first character is used. Empty means `"`. |

**Remarks.** Inside a quoted field, a doubled quote character is an escaped literal quote. Delimiters and line
breaks inside quotes are part of the field. On output a field is quoted only if it contains the delimiter, the
quote character, `\r` or `\n`.

**Example**

```jsonc
{ "quote": "'" }
```

### hasHeader

Whether the first row is a header.

| | |
|---|---|
| **Type** | boolean |
| **Required** | No |
| **Default** | `true` |
| **Allowed values** | `true`, `false` |

**Remarks.** Column names in `columns[]` are always looked up in the **first row** of the file, because the
underlying reader unconditionally treats row 1 as the header. With `hasHeader: false` the first row is still
consumed as a header (so a column rule only matches if its `name` equals a value in that row), and it is then
**not written to the output**: the first data row is lost. `false` therefore does not do what its name suggests.
Keep `true` and give header-less files a header row until this is fixed. (Verified: a 3-line file without a
header produced 2 output rows.)

**Example**

```jsonc
{ "hasHeader": true }
```

### columns

The anonymization rules for this file.

| | |
|---|---|
| **Type** | array of [column rule](#columns-1) objects |
| **Required** | No |
| **Default** | `[]` |

**Remarks.** An **empty list means "copy the file byte for byte"** to the renamed output (the fast path for large
PII-free files). Otherwise only the listed columns change. Rules are applied for each row in the order listed.
Several rules may target the same column; each one sees the result of the previous one. See
[FileHandling.md](FileHandling.md#byte-copy-fast-path).

## columns[]

<a id="columns"></a>

### name

Header of the column to anonymize.

| | |
|---|---|
| **Type** | string |
| **Required** | Yes |
| **Default** | none |
| **Allowed values** | A header present in the file |

**Remarks.** Matched case-insensitively against the header row. If the header contains the same name more than once,
the **last** matching column is used. A name that is not present is logged
(`[warn] <file>: column '<name>' not found in header — skipped.`), recorded in the end-of-run summary as `missing
columns`, and otherwise ignored under both error policies. Because a missing column is not an error, a misspelled
`name` means that column is quietly left un-anonymized, so watch for the warning. A header with a UTF-8 BOM is
handled (the BOM is stripped on read).

**Example**

```jsonc
{ "name": "email", "method": "simpsonsEmail" }
```

### method

The anonymizer to apply.

| | |
|---|---|
| **Type** | string |
| **Required** | Yes |
| **Default** | none |
| **Allowed values** | `simpsonsName`, `simpsonsEmail`, `fakePhone`, `last4`, `randomHex`, `fakeDate`, `guid`, `fakeAddress`, `constant` |

**Remarks.** Matched case-insensitively (`SimpsonsName` works). An unknown method aborts the whole run while the
file is being set up, before any rows of that file are written, with
`Unknown anonymizer method '<m>'. Known methods: constant, fakeAddress, ...`. Note this is raised per file when it
is opened, so files earlier in the list will already have been written. See [Anonymizers.md](Anonymizers.md).

**Example**

```jsonc
{ "name": "phone", "method": "fakePhone" }
```

### options

Per-method settings.

| | |
|---|---|
| **Type** | object |
| **Required** | No |
| **Default** | `{}` |
| **Allowed values** | Method-specific; see [Anonymizers.md](Anonymizers.md) |

**Remarks.** Option names are **case-sensitive** (unlike top-level property names): `Part` is not `part`. An
option that is absent, of the wrong JSON type (for example `"digits": "abc"`), or not an object at all is treated
as "not provided" and the default applies; no error is raised. Numeric options also accept numeric strings
(`"digits": "6"`). Unknown option names are ignored.

**Example**

```jsonc
{ "name": "dob", "method": "fakeDate", "options": { "minYear": 1970, "maxYear": 1990, "format": "yyyy-MM-dd" } }
```

### whenEmpty

What to do when the source cell is empty.

| | |
|---|---|
| **Type** | string (enum) |
| **Required** | No |
| **Default** | `passthrough` |
| **Allowed values** | `passthrough`, `generate` |

**Remarks.**

* `passthrough`: an empty cell stays empty and the anonymizer is not called (no data is fabricated and no
  randomness is consumed).
* `generate`: the anonymizer runs even for an empty cell, so the output is never empty (except for methods that
  themselves return empty text, such as `fakeAddress` with `part` `line2`).
* "Empty" means a zero-length value. A cell containing only spaces is not empty and is replaced normally.
* A row shorter than the file's header (missing trailing fields) is left short for any column index beyond its end:
  nothing is generated for a field that does not exist, even with `generate`.

**Example**

```jsonc
{ "name": "phone", "method": "fakePhone", "whenEmpty": "generate" }
```

**See also:** [sample: when-empty](samples/README.md#2-whenempty-passthrough-vs-generate).

## Path resolution

All path-like settings use the same rules:

1. A leading `~` followed by `/` or `\` (or alone) expands to the user's home directory.
2. A rooted path is used as is.
3. Anything else is resolved against a base directory:

| Setting | Base directory |
|---|---|
| `inputFolder`, `outputFolder` | The folder containing the `.anonconfig` file |
| `files[].input` | `inputFolder` (resolved as above), or the config's folder when `inputFolder` is unset |
| `files[].output` (path form) | The folder containing the `.anonconfig` file |
| `files[].output` (bare name) | The output folder |
| Output folder typed at the prompt | The folder containing the `.anonconfig` file (not the current directory) |

Because bases are tied to the config file, a config and its data can be moved around together without edits.
