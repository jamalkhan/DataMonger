# Errors, rejects and warnings

How the ETL command reacts when something is wrong: a config mistake, a bad cell, a missing file, an unusual
value. There are two very different families:

* **Configuration errors** are structural: they are found when an output is compiled, stop the run, and are always
  fatal (no policy can soften them).
* **Data errors** happen while records are rendered and are handled by an *error policy* with three layers (step,
  field, run), with side files that record what was dropped or flagged.

## Contents

[Overview matrix](#overview-matrix) · [Run policy](#run-policy) · [Overrides](#overrides) ·
[The rejects file](#the-rejects-file) · [The warnings file](#the-warnings-file) · [The filtered file](#the-filtered-file) ·
[Failures that ignore the policy](#failures-that-ignore-the-policy) · [Compile-time errors](#compile-time-errors) ·
[Things that are not validated](#things-that-are-not-validated) · [Troubleshooting](#troubleshooting)

Per-step behavior (`onError` on a transform step, what each op throws) is in
[Transforms/Readme.md](Transforms/Readme.md#error-behavior). This page covers the engine-level behavior.

## Overview matrix

| What goes wrong | Where it surfaces | Result |
| --- | --- | --- |
| The config is not valid JSON, has an unknown enum value or a wrong value type | Before anything runs | `[FAIL] Could not parse config: <reason>`; nothing written |
| Structural config problem (unknown alias, missing column in a join, unknown op ...) | When the output is compiled | `[FAIL] EtlConfigException: <reason>`; nothing written for that output (earlier outputs may be complete) |
| A source file does not exist | When an output that uses it is compiled | `[FAIL] FileNotFoundException: source file not found: <path>` |
| A field's pipeline or type conversion throws | While rendering that field | Handled by the [run policy](#run-policy) unless overridden |
| A `keyBy` or `mergePrefix` pipeline throws for one child row | While rendering | The child is dropped, a [warning](#the-warnings-file) is recorded, the record is kept (always) |
| A lookup's `expr` throws for one lookup-source row | While building the lookup index | The row is skipped and one `[warn]` line is logged (always) |
| A keyedMap `keyExpr` pipeline throws | While rendering a group | **The run aborts**, whatever the policy says ([details](#keyedmap-key-failures)) |
| A decimal/double field's source text is in scientific notation (`1.5e-05`) | While rendering | The value is written; a [warning](#the-warnings-file) is recorded |
| `gpg` fails or is missing | When an encrypted source is opened | `[FAIL] EtlConfigException: gpg failed ...` ([Sources.md](Sources.md#decrypting-gpg-and-keys)) |
| `splitEvery` is zero or negative | At the first record written | `[FAIL] ArgumentOutOfRangeException: recordsPerFile must be greater than zero.` |
| Any other unexpected exception | Anywhere | `[FAIL] <ExceptionType>: <message>` followed by a stack trace; files written so far remain |

When the command fails it reports one failure and returns; there is no partial-success exit code.

## Run policy

`run.onError` decides what happens when a **field fails** and nothing more specific handled it. A "field failure"
is an exception thrown while computing a field's value: a step that throws (parse failure, divide by zero, an
unmapped `map` key with no default, a null operand in arithmetic) or the final coercion to the field's `type`
failing (`"three"` into an `integer`). It applies to fields of the primary record and of every nested child, group
or keyed relationship in it.

| `onError` | Effect on the record | Recorded where | Run continues |
| --- | --- | --- | --- |
| `skip` (default) | The **entire primary record is dropped**, including everything nested under it | [Rejects file](#the-rejects-file); counted as "skipped" | Yes |
| `nullify` | Only the **failing field is omitted**; the rest of the record is written | Nowhere | Yes |
| `abort` | Nothing more is written | Console `[FAIL] ...` | **No** |

#### skip

The record is not written and the **source row of the primary** is appended to `<fileBaseName>_<ts>_rejects.csv`
with the reason. The console prints `N record(s) skipped -> <path>` and the report shows `, N skipped`. Only the
first failure of a record is reported: rendering stops at the first failing field. In keyedMap mode the
skipped unit is the whole group of rows, and the rejects file receives the group's **first row** only.

#### nullify

The failing field simply does not appear. Because nothing is recorded, a typo in a pipeline can lose data without a
trace; prefer a field-level [`warnAndNull`](#field-level-onerror) when you want the same behavior with a log.

#### abort

The exception propagates out of the engine. The command prints `[FAIL] <ExceptionType>: <message>` and the stack
trace. Files already written are closed and kept: the JSONL contains every record before the failing one
(the failing record is not written), and a single-file feed is still renamed to its final name. Rejects and warnings recorded before the failure stay on disk, but the random/rich
samples of that output are not written (they are produced at the end of the output). The run does not continue with
later outputs.

```
[FAIL] FormatException: The input string 'three' was not in a correct format.
   at ...
```

Samples: [11-errors/skip](samples/11-errors/skip.etlconfig), [nullify](samples/11-errors/nullify.etlconfig),
[abort](samples/11-errors/abort.etlconfig) (same input, three policies).

## Overrides

Two finer-grained settings take precedence over the run policy.

### Step-level `onError`

On a single pipeline step: `"throw"` (default; escalate to the field/run policy), `"null"` (the step yields null
and the pipeline continues) or `{ "default": <value> }` (the step yields that value). The value then still goes
through the type coercion. Full details: [Transforms/Readme.md](Transforms/Readme.md#error-behavior).

```jsonc
{ "name": "quantity", "from": "quantity", "type": "integer",
  "pipeline": [ { "op": "toInteger", "onError": { "default": 1 } } ] }
```

### Field-level `onError`

On an output field: `"onError": "warnAndNull"`. When the field fails, **only that field is omitted, the record is
kept, and a row is written to the warnings file**, regardless of `run.onError`.

| | |
| --- | --- |
| **Type** | string |
| **Allowed values** | `"warnAndNull"` (case-insensitive). Anything else fails: `field '<name>': unknown onError value '<v>' (expected 'warnAndNull').` |
| **Default** | none (the run policy applies) |

It covers every failure while computing that field (pipeline, coercion, lookup conversion, coalesce). Use it for a
column that is *sometimes* malformed (a date that occasionally has no year) when losing the field is better than
losing the record, but you want to be told about it.

```jsonc
{ "name": "ship_date", "from": "ship_date", "type": "date", "onError": "warnAndNull" }
```

Order of precedence for a failure: step `onError` -> field `onError` -> `run.onError`.

Sample: [11-errors/field-and-step](samples/11-errors/field-and-step.etlconfig): one bad quantity is defaulted
(step), one bad price nulled (step), one bad date warned (field); no record is lost.

## The rejects file

`<fileBaseName>_<timestamp>_rejects.csv`, created on the first skipped record (and not at all otherwise).

```
# reason	row
# field 'quantity': The input string 'three' was not in a correct format.
6002,1002,three,5.00,2025-01-11,0.40
# field 'unit_price': The input string 'n/a' was not in a correct format.
6003,1003,1,n/a,2025-01-12,2.00
```

* The first line is a header comment (`# reason<TAB>row`).
* Then, for each rejected record, a `# <reason>` line followed by the **primary source row** as a CSV line
  (comma-separated and quoted as needed, whatever the source's delimiter, without a header).
* The reason names the failing field and the underlying message. If the failing field was in a *child* or group,
  the printed row is still the primary row; the field name tells you where to look.
* It is not strict CSV because of the `#` lines. To feed the rows back through a config, strip lines starting with
  `#` and re-add the source's header row.

## The warnings file

`<fileBaseName>_<timestamp>_warnings.csv`: **tab-separated**, created on the first warning. Despite the `.csv`
extension the separator is a tab, and no quoting is applied.

```
record_key	field	reason	raw_value
1001	middle_rank	The input string 'Augusta' was not in a correct format.	Augusta
1004	ship_date	'12-25' has no year (month-day only) — refusing to guess a year.	12-25
1004	weight_kg	scientific notation in a numeric column	1.5e-05
```

| Column | Meaning |
| --- | --- |
| `record_key` | Identifies the record the warning came from, derived from the **join keys** of the relationships (rules below); blank when no key is known |
| `field` | The field name; for `keyBy`/`mergePrefix` failures, the relationship's `as` |
| `reason` | The message |
| `raw_value` | The raw source text of the field's `from` column (blank when not applicable) |

### How `record_key` is derived

The value is read from the **raw source text** of the row that raised the warning, so it is not affected by typing or
transforms (`1001`, not `1001.0`). The columns used depend on which node of the output the row belongs to:

| The warning came from... | `record_key` is the value of... |
| --- | --- |
| The primary record of an `object` output that has at least one relationship | The parent-side join column(s) of its **first** relationship (`on.parent` / `on.parents` of the first entry in `children`) |
| The primary record of a `keyedMap` output | The `groupBy` column (always, even when relationships exist) |
| A row of a relationship (child), at any depth | That relationship's own **child-side** join column(s) (`on.child` / `on.children`) |
| A primary record with no relationships | Nothing: blank |

Further rules:

* A **composite key** (`on.parents` / `on.children`) is written with its parts joined by `|`, in the order the columns
  are listed: `1001|Acme Kids`.
* The key is blank when no key columns apply, when a key column is missing from the row's source, or when every part
  of the key is empty.
* Only the first relationship counts for the primary record. A different `on` in a later relationship does not
  change the key (the primary row's key is still the first relationship's parent-side column).
* There is no column named `customer_key` any more, and no column name is special: any key name works
  (`customer_id`, `person_ref`, ...). Earlier versions wrote a header `customer_key` and filled it only when the
  source had a column literally named so (so it was blank for most feeds).

Runnable example: [11-errors/record-key](samples/11-errors/record-key.etlconfig) writes
[`customers_with_orders_..._warnings.csv`](samples/11-errors/expected-output/record-key/run_20260101_000000/customers_with_orders_20260101_000000_warnings.csv)
and [`prefs_with_emails_..._warnings.csv`](samples/11-errors/expected-output/record-key/run_20260101_000000/prefs_with_emails_20260101_000000_warnings.csv):

```
record_key	field	reason	raw_value
1001	middle_rank	The input string 'Augusta' was not in a correct format.	Augusta
1003	middle_rank	The input string 'Brewster' was not in a correct format.	Brewster
1004	ship_date	'12-25' has no year (month-day only) — refusing to guess a year.	12-25
1004	weight_kg	scientific notation in a numeric column	1.5e-05
```

* `middle_rank` is a field on the **customer** (primary) record whose first relationship joins on `customer_id`, so the
  key is `1001`, `1003`.
* `ship_date` and `weight_kg` are fields of the **orders** relationship; its child-side join column is `customer_id`,
  so the key is the customer the order belongs to (`1004`), not the order id.

```
record_key	field	reason	raw_value
1001|Acme	channel_rank	The input string 'newsletter' was not in a correct format.	newsletter
1002|Acme	channel_rank	The input string 'newsletter' was not in a correct format.	newsletter
1006|Acme Kids	channel_rank	The input string 'promos' was not in a correct format.	promos
```

Here the primary source's relationship joins on two columns (`customer_id`, `brand`), hence the composite key.

Warnings with a blank key are normal for single-source configs: see the `_warnings.csv` files of
[11-errors/field-and-step](samples/11-errors/expected-output/field-and-step/run_20260101_000000/orders_20260101_000000_warnings.csv)
and [nullify](samples/11-errors/expected-output/nullify/run_20260101_000000/orders_20260101_000000_warnings.csv),
whose output has no relationship.

A warning is written for:

1. a field with [`onError: "warnAndNull"`](#field-level-onerror) that failed;
2. a successful decimal/double field (with a `from`) whose raw text contains an `e`/`E`
   (`scientific notation in a numeric column`): the value is written correctly but is unusual for a numeric feed;
3. a [`keyBy`](Relationships.md#keyby) child whose key pipeline threw (the child is dropped);
4. a [`mergePrefix`](Relationships.md#mergeprefix) pipeline that threw (that row is dropped from the merge).

The console shows `N warning(s) (record kept) -> <path>` and `, N warnings` in the report.

Known inaccuracy: warnings raised while rendering a record that is *then* skipped (a field earlier in the record
warned, a later one failed under `skip`) are still written and counted, although the record was not kept.

## The filtered file

`<fileBaseName>_<timestamp>_filtered.csv` holds rows excluded by `filter` or `requireRelations` when
`filterExcludedFile` is `true`. Not an error file; see [Outputs.md#filterexcludedfile](Outputs.md#filterexcludedfile).

## Failures that ignore the policy

### keyedMap key failures

If a [`keyExpr`](Outputs.md#keyexpr) pipeline *throws* (not "returns empty"), the exception is not converted into a
row skip: the run **aborts** with `[FAIL] EtlRowException: transform 'map' failed: ...` even under
`onError: skip` or `nullify` (verified with a `map` that has no default and no matching case). Make the key pipeline
total (give `map` a `default`, or use `concat` of literals and fields) so that it cannot throw; a null or empty result is
fine and simply drops that row.

### Dropped children

`keyBy` and `mergePrefix` failures drop only the affected child (see above). They are always warnings, never row
skips or aborts.

### Lookup expression rows

A lookup `expr` that throws on a lookup-source row leaves that row out of the index and logs
`[warn] lookup on '<child key column>': skipped rows the projection couldn't compute - <distinct reasons>`; it
is never fatal and never written to a file.

## Compile-time errors

Raised as `EtlConfigException` (printed `[FAIL] EtlConfigException: <message>`) before the output writes
anything. The message always names the offending alias, relationship or field.

| Message (abridged) | Cause / fix |
| --- | --- |
| `run.sample and run.keyFilter are mutually exclusive.` | Use only one |
| `an output is missing 'name'.` / `a field is missing 'name'.` | Add the name |
| `unknown source alias '<a>'.` | Output/child/lookup `source` is not a key of `sources` |
| `source format '<f>' is not supported yet (only 'delimited').` | Fix `format` |
| `unrecognized delimiter '<d>'.` | Use a shorthand or a single character |
| `source file not found: <path>` (FileNotFoundException) | Path wrong; relative to the config file's folder |
| `keyedMap mode requires 'groupBy'.` / `groupBy column '<c>' not found in source '<a>'.` | |
| `keyedMap mode requires a 'keyExpr' pipeline.` | |
| `requireRelations is only supported in object mode.` | Remove it from keyedMap outputs |
| `requireRelations references '<n>', which is not a relation on the primary node.` | Name must be a relationship `as` on the primary |
| `distinctBy column '<c>' not found in source '<a>'.` | Output- or relationship-level |
| `filter column '<c>' not found in the primary source.` | Filter on any node/lookup source: the column must exist there |
| `a filter condition is missing 'column'.` / `unknown filter op '<o>'.` / `filter on '<c>' requires a 'value'.` / `filter 'in' on '<c>' requires an array value.` / `filter value '<v>' is not a valid date or relative expression ...` | Fix the condition; see [Transforms/Filters.md](Transforms/Filters.md) |
| `run.sample requires 'keyColumn'` / `run.sample: keyColumn '<c>' not found in primary source '<a>'.` | |
| `run.keyFilter requires 'keyColumn' ...` / `run.keyFilter: keyColumn '<c>' not found in primary source '<a>'.` | |
| `a relationship on source '<s>' is missing 'as'.` | |
| `relationship '<as>' requires 'on.parent'/'on.child' (or 'on.parents'/'on.children').` | |
| `join '<as>': 'on.parents' and 'on.children' must have the same number of columns.` | |
| `join '<as>': parent column '<c>' not found on the parent source.` / `child column '<c>' not found in source '<a>'.` | |
| `join '<as>': 'mergeBy' and 'distinctBy' are mutually exclusive.` / `mergeBy column ...` / `mergePrefix requires a 'pipeline'.` / `mergePrefix.from column ...` | [Relationships.md#mergeby](Relationships.md#mergeby) |
| `join '<as>': keyBy requires a 'pipeline'.` / `keyBy.from column '<c>' not found ...` | [Relationships.md#keyby](Relationships.md#keyby) |
| `join '<as>': 'inline' requires cardinality 'one' ...` / `relationship '<as>': 'inline' must be the only source sharing this 'as' name ...` | [Relationships.md#inline](Relationships.md#inline) |
| `join '<as>': 'keyedSingleBestOf' requires 'keyedSingle: true'.` / `keyedSingleBestOf column '<c>' not found ...` | |
| `relationship '<as>': all sources sharing this 'as' must either all use 'keyBy' or none.` / `... must agree on 'keyedSingle'.` | |
| `field '<f>': keysOf references '<n>', which is not a keyed relation on this node.` | [Outputs.md#keysof](Outputs.md#keysof) |
| `field '<f>': lookup requires 'source' and 'column' (or 'expr', unless mode is 'exists').` / `lookup requires 'on.parent' and 'on.child'.` / `lookup parent column '<c>' not found on the current source.` / `lookup child column ... not found in source '<s>'.` / `lookup column ...` / `lookup 'from' column ...` | [Outputs.md#lookup-fields](Outputs.md#lookup-fields) |
| `field '<f>': unknown coalesceEmit '<v>' (expected 'value' or 'label').` / `each coalesce candidate needs exactly one of 'from' or 'lookup'.` / `coalesceEmit 'label' requires every candidate to have a 'label'.` / `coalesce column '<c>' not found on the current source.` | [Outputs.md#coalesce-fields](Outputs.md#coalesce-fields) |
| `field '<f>': unknown onError value '<v>' (expected 'warnAndNull').` | |
| `unknown transform op '<o>'.` / `<op> requires '<param>'.` / `invalid onError value '<v>'.` / `an operand must specify one of: field, literal, or value:true.` | [Transforms](Transforms/Readme.md) |
| `externalMerge requires all direct children of '<a>' to join on the same parent key(s) ...` | Use `hashInMemory`, or align the join keys |
| `pgpKeyFile not found: <path>` / `gpg failed to import key ...` / `gpg failed to decrypt ...` / `could not run 'gpg' ...` | [Sources.md](Sources.md#decrypting-gpg-and-keys) |

Because each output compiles right before it runs, an error in the *second* output appears only after the first has
finished writing; fix the config and rerun (the earlier output will be written again into a new run folder).

## Things that are not validated

The engine does **not** report these; they have silent effects:

| Mistake | Effect |
| --- | --- |
| A plain field's `from` names a column that does not exist | The field is null for every record and is omitted |
| A misspelled property anywhere in the config | Ignored (default used) |
| `sources.<alias>.hasHeader: false` | Ignored; the first row is always the header |
| `load.format` other than `jsonl` | Ignored; JSONL is written |
| `fields` entry for a column that is not in the header | Ignored |
| `mergeBy` / `mergePrefix` without `keyBy`, `keyedSingle` without `keyBy` | Ignored |
| `distinctBy` or `children` on a keyedMap output | Ignored |
| `lookup.mode` other than `values`/`exists` | Treated as `values` |
| Two outputs with the same `fileBaseName` and folder | The later overwrites the earlier |
| Duplicate property names (two fields, or colliding keyed-map keys) | Both are written (duplicate JSON keys) |
| Join/filter on a column with typed `fields` | Typing is ignored for joins and filters (raw text) |
| `run.sample`/`keyFilter` with `primaryRows <= 0` or an empty `values` list | The run is a full run |

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| A field is missing from *every* record | Wrong `from` spelling; or the value is empty; or the type conversion failed under `nullify` |
| Records are missing and the report shows `N skipped` | Open `..._rejects.csv`; fix the data or add a `default`/`warnAndNull` |
| `0 records across 0 file(s)` | Empty/header-only source, a filter that excludes everything, `requireRelations` that never matches, a `keyFilter` with no matching key, or a wrong `groupBy` |
| A child array is missing for a customer who has rows | The child's `filter` removed them; `cardinality: one` picked a first row that fails the filter; the join keys differ in case/format; or the key cell is blank |
| Everything has the same child | A blank key matches every blank key; see [Relationships.md#on](Relationships.md#on) |
| One huge string field containing the rest of the file | A stray quote character in the data; see [Sources.md](Sources.md#how-a-file-is-read) |
| `OutOfMemoryException` | A relationship or lookup source too large to index; see [RunOptions.md](RunOptions.md#memory-and-temp-space) |
| Output order differs from the input | `externalMerge` sorts by key text ([RunOptions.md](RunOptions.md#joinstrategy)) |
| `gpg failed to decrypt ... No secret key` | The private key is not in the keyring; add `decrypt.pgpKeyFile` |
