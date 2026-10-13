# Run options (`run`)

The optional top-level `run` object holds settings that apply to the whole run (every output). Every property
has a default, so `run` can be omitted entirely.

```jsonc
"run": {
  "joinStrategy": "hashInMemory",
  "onError": "skip",
  "emitEmpty": false,
  "sample":    { "primaryRows": 100, "keyColumn": "customer_id" },
  "keyFilter": { "keyColumn": "customer_id", "values": [ "1003", "1006" ] },
  "maxRecords": 5
}
```

| Property | Type | Default | Section |
| --- | --- | --- | --- |
| [`joinStrategy`](#joinstrategy) | `hashInMemory` \| `externalMerge` | `hashInMemory` | How children are attached to the primary |
| [`onError`](#onerror) | `skip` \| `abort` \| `nullify` | `skip` | What a failing field does to its record |
| [`emitEmpty`](#emitempty) | bool | `false` | Keep blank strings as `""` |
| [`sample`](#sample) | object | none | Run on the first N primary rows plus all their related rows |
| [`keyFilter`](#keyfilter) | object | none | Run on specific named key values |
| [`maxRecords`](#maxrecords) | integer | none | Stop each output after N records |

[Memory and temp space](#memory-and-temp-space) summarizes the resource behavior. There are no settings for
memory limits, temp folders, parallelism or buffer sizes: those are fixed in the engine.

---

### joinStrategy

How the engine attaches child rows (and, in keyedMap mode, groups rows) for **every output** in the config.

| | |
| --- | --- |
| **Type** | enum string |
| **Required** | No |
| **Default** | `hashInMemory` |
| **Allowed values** | `hashInMemory`, `externalMerge` (case-insensitive) |

#### Remarks

The choice is run-wide: you cannot mix strategies between outputs of one config. Split the work into two config
files if one output needs the other strategy. The content of the output records is identical for the two
strategies (apart from the ordering rules below); the difference is memory use, speed and record order.

| | `hashInMemory` | `externalMerge` |
| --- | --- | --- |
| **Primary source** | Streamed row by row in **file order**; never buffered | External-sorted by the join key, then streamed in **key order** |
| **Child sources** | Each child source is read **completely into memory** (a dictionary from join key to rows) before the first record is written | Each *direct* child is external-sorted by its join key and merge-joined in one pass; never fully in memory |
| **Grandchildren and deeper** | In memory | In memory (the engine logs `externalMerge: levels below '<as>' are nested using in-memory indexes.`) |
| **Peak memory** | About the size of all child and grandchild sources (as parsed rows, which is considerably more than the files' size on disk) | One sort chunk (200,000 rows) per source being sorted, plus the in-memory grandchildren and lookups |
| **Disk use** | None | Temporary sorted "run" files in the system temp directory, roughly the size of the data being sorted |
| **Record order** | Primary file order | Ordinal order of the join key (see below) |
| **Composite keys** (`on.parents`/`on.children`) | Yes | Yes |
| **`caseInsensitiveJoin`** | Yes, per relation | Yes, but all direct children of a node must use the same setting |
| **Different parent column per child** | Yes | **No**: every direct child of a node must join on the same parent column(s) |
| **keyedMap mode** | Reads the *whole primary into memory* to group it | Sorts the primary by `groupBy` and groups consecutive rows (bounded memory) |
| **Lookups** | Index built in memory | Index built in memory (same as `hashInMemory`) |

**When to choose which.**

* Use **`hashInMemory`** (the default) when the files you *join to* (children, lookups) comfortably fit in RAM, no
  matter how big the primary is. It is simpler, needs no temp space, keeps source order and is the only strategy
  that allows children joining on different parent columns. A 50 GB event file joined to a 20 MB product table is
  a perfect `hashInMemory` job.
* Use **`externalMerge`** when a *child* source is too large to index in memory (for example a multi-gigabyte
  orders table nested under customers), or for a **keyedMap** output over a very large primary. You trade extra
  disk I/O and a sorted output for bounded memory.
* The two strategies do not help lookups: **lookup sources are always loaded into memory** (see
  [Outputs.md](Outputs.md#lookup-fields)), as are levels below the first nesting level under `externalMerge`.
  If those are what is too big, no setting here fixes it; restructure the config (for example pre-split or
  pre-filter the lookup file, see [Split CSV](../SplitCsv/Readme.md)).

**Ordering under `externalMerge`.** Join keys are compared as **ordinal strings**, not numbers. With numeric ids
`9`, `10`, `100`, the order is `10`, `100`, `9`. (The sample's four-digit ids sort the same either way, which can
hide this.) Rows that share a key come out in no guaranteed order relative to the source file, because the sort
is not stable: if a `cardinality: "one"` relation has several matching rows, which one is "first" can differ from
what `hashInMemory` picks. If you rely on source order within a key, use `hashInMemory`, or order your files so
that the choice does not matter. Rows whose key cell is empty sort first. A primary row with no matching
children is still emitted. Child rows with no matching parent (orphans) are ignored.

**Temp files.** `externalMerge` writes sorted runs (200,000 rows each) as CSV files under
`<system temp>/extsort_<guid>/` (`Path.GetTempPath()`, so `TMPDIR` on Linux/macOS and `TEMP` on Windows). The
folder is deleted when that sort finishes or is abandoned (for example by [`maxRecords`](#maxrecords)); a process
crash can leave one behind. Make sure the temp volume has room for roughly the combined size of the primary and
its direct children. The chunk size and temp location cannot be configured.

**Order of work under `externalMerge`.** All sources of one output are sorted *before* the first record is
written, so the first record appears only after the sorts finish, and [`maxRecords`](#maxrecords) does not avoid
that sorting cost (a [`sample`](#sample) does reduce it, because the sources are filtered while they are read).

**Errors specific to this setting.**

```
externalMerge requires all direct children of 'customers' to join on the same parent key(s) with the same
caseInsensitiveJoin setting; found [customer_idFalse | emailFalse]. Use joinStrategy 'hashInMemory' for this output.
```

(`<columns><caseInsensitive>` is listed for each distinct combination found, with `False`/`True` appended; the
text is ugly but the meaning is "two children join on different parent columns".) An invalid enum value stops the
run at parse time: `Could not parse config: The JSON value could not be converted to ...JoinStrategyKind`.

#### Example

```jsonc
"run": { "joinStrategy": "externalMerge" }
```

See [samples/07-external-merge](samples/07-external-merge/external-merge.etlconfig): the shuffled primary comes
out sorted by `customer_id`, orders and lines are nested, and a second output joins on the composite key
`customer_id` + `brand`.

#### See also

[Relationships](Relationships.md), [Memory and temp space](#memory-and-temp-space),
[Outputs.md#keyedmap-mode](Outputs.md#keyedmap-mode).

---

### onError

What happens to a record when a field fails: a step throws, a type coercion fails, a lookup value cannot be
converted. It is the run-wide default that [step-level and field-level overrides](Errors.md#overrides) refine.

| | |
| --- | --- |
| **Type** | enum string |
| **Required** | No |
| **Default** | `skip` |
| **Allowed values** | `skip`, `abort`, `nullify` |

| Value | Effect |
| --- | --- |
| `skip` | The whole record is **dropped**, the source row is appended to `<fileBaseName>_<timestamp>_rejects.csv` together with the reason, and the run continues. Counted as "skipped". |
| `nullify` | Only the failing **field is omitted**; the record is written with its other fields. No reject row and no warning is written, so nothing records that it happened. |
| `abort` | The run **stops immediately** with `[FAIL] <exception type>: <message>`. Files written so far stay on disk (closed, complete up to the last good record). |

#### Remarks

* This policy applies to errors raised while rendering a field's value. It does **not** govern compile-time
  mistakes (those always stop the run), a failing `keyBy` / `mergePrefix` pipeline (those drop the child and write
  a warning), a failing `lookup.expr` row (skipped, logged) or a failing [`keyedMap` `keyExpr`](Errors.md#keyedmap-key-failures)
  (which aborts regardless of this setting). See [Errors.md](Errors.md) for the full matrix.
* In `keyedMap` mode a `skip` rejects the **entire group** (all rows of the key become one skipped record) and
  writes only the group's first row to the rejects file.
* Choose `skip` for production feeds where bad rows should not stop the load but must be reviewable, `abort`
  while developing a config or for a feed that must be all-or-nothing, and `nullify` only when losing a value is
  acceptable and does not need to be reported.

#### Example

```jsonc
"run": { "onError": "nullify" }
```

#### See also

[Errors.md](Errors.md), [samples/11-errors](samples/11-errors/), [Transforms](Transforms/Readme.md#error-behavior)

---

### emitEmpty

Whether blank string values are written as `""` or left out.

| | |
| --- | --- |
| **Type** | bool |
| **Required** | No |
| **Default** | `false` |

#### Remarks

* `false` (default): a field whose final value is an empty string is **omitted** from the record.
* `true`: a **string** field whose value is empty is written as `""`. A plain pass-through field (`from` a column,
  no `pipeline`, `type: "string"`) that points at an empty cell keeps its `""`. A field that has pipeline steps
  does not (by the time the pipeline runs, an empty cell has already become null), so for those the field is still
  omitted.
* Regardless of this setting, `null` values and empty arrays are **always omitted**, and an empty object
  group stays `{}`. Use a field's [`emitNull`](Outputs.md#emitnull) to force a JSON `null`.
* A keyed relation always writes its object (`{}` when it has no keys); see [Relationships](Relationships.md#keyby).

#### Example

```jsonc
"run": { "emitEmpty": true }
```

Input row `1002,Alan,,Turing,...` with a pass-through `middle_name` field produces
`"middle_name":""` with `emitEmpty: true` and no `middle_name` property otherwise.
See [samples/01-basic-object/empty-and-null.etlconfig](samples/01-basic-object/empty-and-null.etlconfig).

#### See also

[Outputs.md#null-and-empty-handling](Outputs.md#null-and-empty-handling), [Transforms/Readme.md](Transforms/Readme.md#null-and-empty-semantics)

---

### sample

Runs the whole config on a **parent-scoped sample**: the first N rows of each output's primary source plus *every*
related row (children at any depth, and lookups) belonging to those primary rows. Use it to try a new config
quickly on a huge dataset, with complete (not truncated) records.

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `primaryRows` | integer | Yes | How many rows of the primary to take. A value of `0` or less disables the sample (the run is a full run). |
| `keyColumn` | string | Yes | The column that ties a row to its parent, **present on the primary and (ideally) on every other source**. |

#### Remarks

How it works, per output:

1. A pre-pass reads the first `primaryRows` rows of the primary and collects the distinct non-empty values of
   `keyColumn` (compared as raw ordinal strings, exactly as joins are).
2. The primary is then capped to those same rows.
3. Every other source (children at any depth, lookup sources) is filtered **while it is read** to rows whose
   `keyColumn` value is in that set.

Consequences:

* Records are complete: a customer with five orders keeps all five, at any depth.
* A source that has **no column named `keyColumn`** cannot be scoped: it is read in full and the console logs
  `[warn] sample: source '<alias>' has no '<keyColumn>' column; reading it in full.` The join still produces the
  right records, but you pay full read cost for that source. Typical case: a line-items file that links to its order
  by `order_id` and has no `customer_id`.
* The filter is by **column name, not relationship**: every source is filtered on the *same* `keyColumn`. If a
  source has a column with that name that means something different, rows are dropped wrongly.
* Each referenced source is still **scanned end to end** (matching rows can be anywhere in the file); the saving
  is in sorting, indexing and rendering. Sampling does not make small files faster.
* Each output is sampled against **its own** primary. With several outputs the sample key sets differ.
* While sampling, identical lookups across outputs are not shared (each output builds its own scoped index).
* The console shows `Sample : first N primary row(s), scoped by <keyColumn> (SAMPLE RUN)` and, per output,
  `[SAMPLE RUN] ...` and `sample: N primary row(s) from '<alias>' -> M distinct <keyColumn> value(s); ...`.
* `keyColumn` missing from the primary source fails with `run.sample: keyColumn '<col>' not found in primary
  source '<alias>'`; an empty `keyColumn` fails with `run.sample requires 'keyColumn'`.
* `sample` and [`keyFilter`](#keyfilter) are **mutually exclusive**: `run.sample and run.keyFilter are mutually
  exclusive.`
* Do not confuse it with [`load.sampling`](Load.md#sampling), which writes a random handful of *output* records
  as pretty JSON files.

#### Example

```jsonc
"run": { "sample": { "primaryRows": 2, "keyColumn": "customer_id" } }
```

[samples/09-run-limits/sample.etlconfig](samples/09-run-limits/sample.etlconfig) writes customers 1001 and 1002,
each with all their orders and emails.

#### See also

[keyFilter](#keyfilter), [maxRecords](#maxrecords), [Load.md#sampling](Load.md#sampling)

---

### keyFilter

Runs the config on an **explicit list of key values**, wherever they sit in the primary file. Same scoping as
[`sample`](#sample), but you name the parents instead of taking the first N. Use it to reproduce one customer's
record while debugging.

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `keyColumn` | string | Yes | Same meaning as in `sample`. Must exist on the primary source. |
| `values` | array of string | Yes | The exact key values to keep. An **empty list disables the filter** (full run). |

#### Remarks

* Values are compared as **raw ordinal strings**: leading zeros and formatting must match the data exactly.
  Write numbers as strings (`"1003"`). (A JSON number is not accepted here: `values` is a list of strings.)
* The primary is filtered by value (not capped by count), as is every other source that has the column; sources
  without the column are read in full with the same `[warn]` as `sample`.
* The console shows `[KEY FILTER RUN] ...` and `keyFilter: scoping to N explicit <col> value(s): ...`.
* Errors: `run.keyFilter requires 'keyColumn' ...`, `run.keyFilter: keyColumn '<col>' not found in primary source
  '<alias>'`, and the mutual-exclusion error shown under [`sample`](#sample).
* A key that matches nothing silently produces no record for it; check the record count.

#### Example

```jsonc
"run": { "keyFilter": { "keyColumn": "customer_id", "values": [ "1003", "1006" ] } }
```

[samples/09-run-limits/key-filter.etlconfig](samples/09-run-limits/key-filter.etlconfig)

#### See also

[sample](#sample), [Errors.md](Errors.md#compile-time-errors)

---

### maxRecords

Stop **each output** after N records have been written.

| | |
| --- | --- |
| **Type** | integer (64-bit) |
| **Required** | No |
| **Default** | none (no cap); `0` or negative also means no cap |

#### Remarks

* It counts records **written**, so records dropped by a `filter`, `requireRelations`, `distinctBy` or a `skip`
  do not count against it.
* It is per output, not for the whole run: with two outputs and `maxRecords: 5` you get up to ten records.
* When the cap stops a feed early the engine logs `stopped at run.maxRecords = N record(s); the feed is
  incomplete.` If the source had exactly N eligible records the loop ends naturally and no such message appears.
* It limits what is *written*, not what is *read*: under `hashInMemory` the child indexes are already built, and
  under `externalMerge` the sorts are already done. For a cheap trial run on a large input prefer
  [`sample`](#sample) or [`keyFilter`](#keyfilter), which also reduce the work.
* For a single output file, combine it with [`load.splitEvery`](Load.md#splitevery) set to the same value.
* The `Max records` line in the console reads `(no cap)` or the formatted number.
* Random [`sampling`](Load.md#sampling) sees only the records that were written.

#### Example

```jsonc
"run": { "maxRecords": 5 }
```

With `"splitEvery": 2` in the output's `load`, [samples/09-run-limits/max-records.etlconfig](samples/09-run-limits/max-records.etlconfig)
writes three files holding 2 + 2 + 1 records.

#### See also

[Load.md](Load.md#splitevery), [sample](#sample)

---

## Memory and temp space

| Resource | Behavior |
| --- | --- |
| Primary source | Never held in memory (streamed), **except** a `keyedMap` output under `hashInMemory`, which buffers the whole primary to group it |
| Child sources | `hashInMemory`: fully indexed in memory before the first record. `externalMerge`: direct children external-sorted; deeper levels in memory |
| Lookup sources | Always fully indexed in memory (`key -> list of values`); a lookup index is shared between lookups and outputs that index the identical thing (same file, join column, projection, filter, mode), except in sampled or key-filtered runs. Every row's join key and projected value is stored |
| Sort chunk | 200,000 rows per run file (`externalMerge`); fixed |
| Temp folder | `Path.GetTempPath()` (`extsort_<guid>`); fixed; removed after use |
| Decrypted/decompressed sources | Written next to the source in `<file>_decrypted/`; kept ([Sources.md](Sources.md#encrypted-and-compressed-sources)) |
| Samples | `sampling` keeps up to `count` rendered records in memory; `richSampling` likewise |
| Parallelism | None. One output at a time, one thread |

If a run dies with an `OutOfMemoryException`, find which source is large and what indexes it: a child relation
(try `externalMerge`), a lookup (no engine fix: reduce the lookup source, or turn it into a relation under
`externalMerge`), or a `keyedMap` over a huge primary (use `externalMerge`).
