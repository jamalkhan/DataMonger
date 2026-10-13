# Outputs

An entry in the `outputs` array defines one **feed**: which source is its primary, what each record looks like
and where it is written. A config may declare any number of outputs; they run one after another and may share
sources.

```jsonc
{
  "name": "customers",
  "mode": "object",
  "source": "customers",
  "fields":   [ /* what each record contains */ ],
  "children": [ /* nested records: see Relationships.md */ ],
  "filter": [ /* optional */ ],
  "load": { /* see Load.md */ }
}
```

| Property | Type | Required | Default | Section |
| --- | --- | --- | --- | --- |
| [`name`](#name) | string | Yes | none | Label of the feed |
| [`mode`](#mode) | `object` \| `keyedMap` | No | `object` | Record shape |
| [`source`](#source) | string | Yes | none | Primary source alias |
| [`fields`](#fields) | array | No | `[]` | The record's fields |
| `children` | array | No | `[]` | [Relationships.md](Relationships.md) |
| [`filter`](#filter) | array | No | none | Drop records |
| [`filterExcludedFile`](#filterexcludedfile) | bool | No | `false` | Keep what was dropped |
| [`distinctBy`](#distinctby) | string | No | none | One record per distinct column value (object mode) |
| `requireRelations` | array | No | none | [Relationships.md](Relationships.md#requirerelations) (object mode) |
| [`groupBy`, `keyExpr`, `dedup`](#keyedmap-mode) | | | | keyedMap mode only |
| `load` | object | No | defaults | [Load.md](Load.md) |

Contents: [name](#name) · [mode](#mode) · [source](#source) · [fields](#fields) ·
[object groups](#object-groups) · [lookup fields](#lookup-fields) · [coalesce fields](#coalesce-fields) ·
[keysOf](#keysof) · [null and empty handling](#null-and-empty-handling) · [filter](#filter) ·
[distinctBy](#distinctby) · [keyedMap mode](#keyedmap-mode)

---

### name

The feed's name. Shown in console messages (`--- customers (Object, HashInMemory) ---`, the report) and used in
error messages.

| | |
| --- | --- |
| **Type** | string |
| **Required** | Yes (empty fails: `an output is missing 'name'.`) |

The `name` does **not** determine the output file name; that is [`load.fileBaseName`](Load.md#filebasename). It
does not need to be unique, but give each output a distinct `fileBaseName`/folder: two outputs resolving to the same
file silently overwrite each other ([EtlConfig.md](EtlConfig.md#pitfalls)).

---

### mode

The shape of one output record.

| | |
| --- | --- |
| **Type** | enum string |
| **Default** | `object` |
| **Allowed values** | `object`, `keyedMap` |

| Mode | One record per | Shape |
| --- | --- | --- |
| `object` | primary-source row (after filters) | A JSON object with the declared fields and nested children |
| `keyedMap` | group of primary rows sharing a `groupBy` value | A JSON object whose **property names are computed** from each row, e.g. `{"email::ada@x": {...}, "email::alan@x": {...}}` |

Object mode is by far the more common; see [keyedMap mode](#keyedmap-mode) below for the other.

---

### source

The alias (a key of [`sources`](Sources.md)) of the **primary source**: the file whose rows drive the output.
In object mode each of its rows becomes (at most) one record. Aliases are case-insensitive; an unknown alias
fails with `unknown source alias '<alias>'.`

Remarks: all `from` columns of the output's own `fields` read this source's columns; relationships and lookups
join *to* columns of this source.

---

### fields

The ordered list of the record's fields. **Output order follows the config order**; node-level
[`children`](Relationships.md) are written after all of a node's fields. To put a nested array between scalar
fields, use an [inline relationship field](Relationships.md#inline-relationship-fields).

Each field object:

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| [`name`](#field-name) | string | required | The JSON property name (case-sensitive) |
| [`from`](#from) | string | none | Source column the field starts from |
| [`type`](#type) | enum | `string` | The emitted JSON type |
| `pipeline` | array of steps | `[]` | Transforms: [Transforms/Readme.md](Transforms/Readme.md) |
| [`emitNull`](#emitnull) | bool | `false` | Write `null` instead of omitting |
| `onError` | `"warnAndNull"` | none | [Errors.md](Errors.md#field-level-onerror) |

A field is one of several **kinds**, chosen by which properties are present (checked in this order; the first
match wins):

| If the field has | It is a | Section |
| --- | --- | --- |
| `coalesce` | coalesce field | [Coalesce fields](#coalesce-fields) |
| `lookup` | lookup field | [Lookup fields](#lookup-fields) |
| `source` | inline relationship | [Relationships.md](Relationships.md#inline-relationship-fields) |
| `type: "object"` or `fields` | object group | [Object groups](#object-groups) |
| `keysOf` | key-list field | [keysOf](#keysof) |
| none of the above | plain field | below |

Properties that belong to a different kind are not rejected, they are just unused (a `from` on a coalesce field is
ignored, for instance).

#### field name

Required (`a field is missing 'name'.`). It is the JSON property name. If two fields use the same name the second
is written as a second property of that name (invalid for strict consumers); the exception is *inline
relationship* fields, which merge ([Relationships.md](Relationships.md#sharing-one-array-between-several-sources)).

#### from

The source column whose **typed value** starts the field's pipeline. The match ignores case. If `from` is omitted
the pipeline starts with `null`, which suits fields built only from literals or other columns
(`concat`, `literal`).

**A `from` that names a column which does not exist is not an error:** the value is simply null and the field is
omitted, for every record. If a field is unexpectedly missing everywhere, check the spelling of `from` against the
header first. (Columns used for joins, filters, `groupBy`, `distinctBy`, `keyBy`, `mergeBy` and lookups *are*
validated.)

#### type

The emitted JSON type. After the pipeline runs, the engine coerces the result to this type.

| `type` | JSON written | Notes |
| --- | --- | --- |
| `string` (default) | `"text"` | Numbers, dates and booleans are converted to text |
| `integer` | `123` | 64-bit; decimals/doubles round half away from zero |
| `decimal` | `12.50` | Exact; keeps the scale of its source text (`12.50` stays `12.50`) |
| `double` | `1.5` | Floating point |
| `bool` | `true` / `false` | Accepts `true`/`false` (any case) and `1`/`0` |
| `date` | `"2024-01-15"` | Always `yyyy-MM-dd` |
| `dateTime` | `"2024-01-15T09:30:00Z"` | Always `yyyy-MM-dd'T'HH:mm:ss'Z'`, whole seconds |
| `object` | `{...}` | An [object group](#object-groups) |
| `array` | `[...]` | From a `split` step, an array lookup, an inline relationship or `keysOf` |

Remarks on dates:

* The `Z` is a **label**, not a conversion: the engine formats the parsed local clock time and appends `Z`.
  A source value with an explicit offset (`2025-03-01T10:00:00-05:00`) is first converted to the **machine's local
  time zone** by the parser, so the written time depends on where the engine runs (on a UTC machine it becomes
  `15:00:00Z`; on a US-Eastern machine `10:00:00Z`, which is wrong as UTC). Values without an offset
  (`2025-03-01 10:00:00`) and values ending in `Z` are written unchanged. If your data carries offsets, run on
  a UTC machine or normalize the data first.
* `date` drops the time of day. A bare month-day without a year (`12-25`) is rejected rather than being given the
  current year.

The full parsing and formatting rules are in [Transforms/ValueTypes.md](Transforms/ValueTypes.md).

#### emitNull

Write an explicit JSON `null` when the field's value is null, instead of omitting it.

| | |
| --- | --- |
| **Type** | bool |
| **Default** | `false` |

The one exception to "nulls are never written". Useful to deliberately blank a property in an update feed:

```jsonc
{ "name": "nickname", "type": "string", "emitNull": true, "pipeline": [ { "op": "literal" } ] }
// -> "nickname": null
```

A *failing* field (error policy) is not "null": see [Errors.md](Errors.md). Empty strings are governed by
[`run.emitEmpty`](RunOptions.md#emitempty), not by `emitNull`. Sample:
[01-basic-object/empty-and-null.etlconfig](samples/01-basic-object/empty-and-null.etlconfig).

---

### Object groups

A field with `"type": "object"` (or with a `fields` array) writes a nested JSON object built from the **same
source row**.

| Property | Type | Meaning |
| --- | --- | --- |
| `fields` | array of field | The group's fields; may include further groups, lookups, ... |
| `children` | array of child | Relationships *inside* the group, joined against the current row |

```jsonc
{ "name": "profile", "type": "object",
  "fields": [
    { "name": "tier",      "from": "tier",      "type": "string" },
    { "name": "is_member", "from": "is_member", "type": "bool" }
  ] }
// -> "profile": { "tier": "gold", "is_member": true }
```

Remarks:

* A group is **always written**, even if every inner field is empty (`"profile": {}`).
* Groups nest to any depth.
* A group's `children` join against the row of the node the group sits in, so a relationship declared in a group
  can use that node's columns as its parent keys. This matters most for [`mergeBy`](Relationships.md#mergeby),
  where a group's contents from several rows are merged.
* Groups do not change which source a field reads: fields inside the group read the node's columns.

Sample: [02-nested-children](samples/02-nested-children/nested.etlconfig) (`profile`) and
[06-merge-by](samples/06-merge-by/merge-by.etlconfig) (`properties` with a nested array).

---

### Lookup fields

A **lookup** pulls a value from a *different* source by joining on a key, for data that is not on the current
row. The lookup source is indexed once in memory (`key -> values`), then each record looks its key up.

```jsonc
{ "name": "email_addresses", "type": "array",
  "lookup": {
    "source": "emails",
    "on": { "parent": "customer_id", "child": "customer_id" },
    "column": "email_address",
    "distinct": true, "limit": 2
  } }
```

`lookup` properties:

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `source` | string | required | Alias of the lookup source |
| `on.parent` | string | required | Column on the **current row** holding the key |
| `on.child` | string | required | Column on the **lookup source** holding the key |
| `column` | string | required unless `expr` or `mode: "exists"` | The lookup-source column to project |
| `single` | bool | `false` | `true`: one scalar (the first match); `false`: an array of values |
| `distinct` | bool | `false` | Drop duplicate values (exact match, first-seen order) |
| `limit` | integer | none | Keep at most N values (applied after `distinct`; with `single`, the first of those) |
| `mode` | `"values"` \| `"exists"` | `"values"` | `exists` returns a bool: is there a matching row? |
| `filter` | array of conditions | none | Only lookup rows passing these (AND) are indexed/counted |
| `expr` | array of steps | none | Replaces `column`: a pipeline run **per lookup row** to produce the indexed string |
| `from` | string | none | With `expr`: the lookup-source column the `expr` pipeline starts from |

Remarks:

* **Result type.** `exists` always gives a bool, `single` gives the field's `type` (the first value converted),
  anything else gives an **array of strings** (the field's `type` is ignored; use `"type": "array"` for clarity).
  Values are the raw cell text of the lookup column, not typed values.
* **No match / empty values.** Empty cells are never indexed. A key with no values gives null, so the field is
  omitted (use `exists` mode if you need a `false`). An array lookup with no values is omitted, not written
  as `[]`.
* **Key matching** is exact (ordinal, case-sensitive) on the raw text, with a single column on each side. Composite
  keys and `caseInsensitiveJoin` are **not** available for lookups (use a [relationship](Relationships.md) for
  those). A row whose key cell is blank is looked up under the empty key `""` and so matches lookup rows that also
  have a blank key; guard with a `filter` if that matters.
* **`expr`.** Use it when the projected value is not a single raw column, for example `"order_id:status"` built with
  `concat`, or a `map` of codes. Fields of the lookup row resolve through `{"field": ...}` operands; `from` makes
  one column the starting value. A lookup row whose `expr` throws (an unmapped code with no `map` default, say)
  contributes nothing and the console logs one
  `[warn] lookup on '<column>': skipped rows the projection couldn't compute - <reasons>` line; it never fails
  the run.
* **`mode`.** Anything other than `exists` (case-insensitive) behaves as `values`. In `exists` mode `column`,
  `single`, `distinct`, `limit` and `expr` are ignored; `filter` is the useful part:
  `"mode": "exists", "filter": [ { "column": "status", "op": "eq", "value": "shipped" } ]`.
* **Pipelines on lookups.** A field-level `pipeline` post-processes the looked-up value: once for a single value,
  once **per element** (results become strings) for an array.
* **`lookups` (plural)** lists extra lookups whose results are unioned into the field's array alongside the
  primary `lookup`. Duplicates are removed (by text) keeping first-seen order, so the result is always an array.
  Typical use: brand names from one file plus brand names from another.
  ```jsonc
  { "name": "brands", "type": "array",
    "lookup":  { "source": "emails", "on": { "parent": "customer_id", "child": "customer_id" }, "column": "brand", "distinct": true },
    "lookups": [ { "source": "phones", "on": { "parent": "customer_id", "child": "customer_id" }, "column": "brand", "distinct": true } ] }
  ```
  `lookups` is only read when `lookup` is also set.
* **Reuse.** Two lookups (in one output or different outputs of the same run) that index the identical thing
  (same file, join column, projected column/`expr`/`from`, `filter` and mode) share one index; the console logs
  `lookup reused from an earlier output in this run (identical source/join/projection) ...`. Sharing is turned off
  during a [`sample`](RunOptions.md#sample) or [`keyFilter`](RunOptions.md#keyfilter) run.
* **Memory.** The whole indexed lookup source is held in memory regardless of
  [`joinStrategy`](RunOptions.md#joinstrategy).
* **Validation.** Missing `source`, `column`/`expr`, `on.parent` or `on.child`, and columns not found
  (`lookup parent column`, `lookup child column`, `lookup column`, `lookup 'from' column`) are compile errors.
* When the lookup should return a *whole nested object* or several columns, use a
  [relationship](Relationships.md) instead.

Sample: [04-lookup](samples/04-lookup/lookup.etlconfig).

---

### Coalesce fields

A **coalesce** field emits the first non-empty value from an ordered list of candidates ("email if present, else
phone").

```jsonc
{ "name": "contact_value", "type": "string",
  "coalesce": [
    { "from": "email", "label": "email" },
    { "from": "phone", "label": "phone" },
    { "lookup": { "source": "phones", "on": { "parent": "customer_id", "child": "customer_id" }, "column": "phone_number" },
      "label": "phone_on_file" } ] },
{ "name": "contact_type", "type": "string", "coalesceEmit": "label",
  "coalesce": [ /* the same candidates */ ] }
```

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `coalesce` | array of candidate | none | The ordered candidates |
| `coalesceEmit` | `"value"` \| `"label"` | `"value"` | Emit the winning candidate's value, or its `label` |
| `type`, `pipeline` | | `string`, none | Apply to the winning *value* (in `value` mode) |

A **candidate** has exactly one of:

| Property | Meaning |
| --- | --- |
| `from` | A column on the current row (must exist) |
| `lookup` | A [lookup](#lookup-fields) on another source, forced to a single value (the first match) |
| `label` | The text emitted when `coalesceEmit` is `label`. Required on **every** candidate in that case |

Remarks:

* "Non-empty" means a non-zero-length string. A cell of spaces counts as a value.
* Candidates are read as **raw text** (not typed). The winning raw text starts the field's pipeline.
* To report *which* candidate won, declare a second field with the same candidates and `"coalesceEmit": "label"`
  (use `type: "string"`). `coalesceEmit` other than `value`/`label` is a compile error.
* No candidate has a value: the field is omitted.
* Errors (`each coalesce candidate needs exactly one of 'from' or 'lookup'`, `coalesce column '<c>' not found on the
  current source`, `coalesceEmit 'label' requires every candidate to have a 'label'`) are compile errors.

Sample: [04-lookup](samples/04-lookup/lookup.etlconfig) (customer 1005 has no email, so the phone wins).

---

### keysOf

An **array field that lists the keys actually produced by keyed relationships on the same node.**

```jsonc
{ "name": "brand_codes", "type": "array", "keysOf": [ "emails_by_brand", "phones_by_brand" ] }
```

`keysOf` names [`keyBy` relationships](Relationships.md#keyby) (by their `as`). The value is the union, in
first-seen order and without duplicates, of every distinct key each of them writes for the current record. Because
it is derived from the relationships themselves, it cannot drift out of step with them, which a separate lookup of
"the same" values can. The array is omitted when there are no keys.

* Each name must be a **keyed** relation on the same node (including inside its object groups); otherwise the
  config fails: `field '<f>': keysOf references '<name>', which is not a keyed relation on this node.`
* A `from`/`pipeline` on a keysOf field is ignored.

Sample: [05-keyed-relations](samples/05-keyed-relations/keyed-relations.etlconfig).

---

### Null and empty handling

| Value | Result |
| --- | --- |
| Null (empty cell, missing column, failed `onError: null` step, no lookup match) | Field **omitted**; `emitNull: true` writes `null` |
| Empty string `""` | Field **omitted**; kept as `""` for plain string pass-through fields when [`run.emitEmpty`](RunOptions.md#emitempty) is `true` |
| Empty array | **Omitted** (an array relationship with no matching children, an array lookup with no values, a `split` of nothing) |
| Empty object group | Written as `{}` |
| Keyed relationship (`keyBy`) with no keys | Written as `{}` |
| Cardinality `one` relationship with no match | Omitted |
| Number or boolean | Never "empty": `0` and `false` are written |

The record itself is written even when it ends up as `{}`.

---

### filter

Conditions the **primary row** must satisfy (all of them, AND) for the record to be emitted. Rows that fail are
counted as "filtered out".

```jsonc
"filter": [
  { "column": "status",     "op": "in",        "value": [ "shipped", "pending" ] },
  { "column": "order_date", "op": "onOrAfter", "value": "2024-01-01" },
  { "column": "store_id",   "op": "notEmpty" }
]
```

| Property | Type | Meaning |
| --- | --- | --- |
| `column` | string | A column of the primary source (checked at compile time: `filter column '<c>' not found in the primary source.`) |
| `op` | string | The operator |
| `value` | JSON value | Comparison value (not used by `empty` / `notEmpty`; an array for `in`) |

Operators (details and edge cases in [Transforms/Filters.md](Transforms/Filters.md)):

| Operators | Value |
| --- | --- |
| `eq` `ne` `gt` `ge` `lt` `le` | A number or text. If both sides parse as numbers they compare numerically, otherwise as ordinal strings |
| `in` | An array of values (exact string match) |
| `empty` `notEmpty` | None |
| `onOrAfter` `after` `onOrBefore` `before` | A date: an ISO date/datetime, or `now`, `now-2years`, `now+30days`, `now-90days` (units: `year`, `month`, `week`, `day`, `hour`, `minute`, optionally plural) |

Remarks:

* Filters compare the **raw cell text**; typed source `fields` and pipelines do not apply.
* A missing or unparseable date **fails** a date condition (the row is excluded).
* Relative tokens such as `now-2years` are resolved once when the output is compiled (run start).
* The same condition shape is used for the `filter` of a [relationship](Relationships.md#filter), an
  [inline relationship](Relationships.md#inline-relationship-fields) and a [lookup](#lookup-fields); in those places the
  column belongs to the *child* or *lookup* source.
* Order of checks for an object-mode record: `filter` -> [`requireRelations`](Relationships.md#requirerelations)
  -> [`distinctBy`](#distinctby) -> render.
* In keyedMap mode the filter applies to each **row** before dedup and key computation; a group with no remaining
  rows produces no record.

Sample: [10-filter-distinct-require](samples/10-filter-distinct-require/filters.etlconfig),
[transforms samples](samples/transforms/README.md).

#### filterExcludedFile

When `true`, every primary row excluded by `filter` or by `requireRelations` is also appended to
`<fileBaseName>_<timestamp>_filtered.csv` in the run folder.

| | |
| --- | --- |
| **Type** | bool |
| **Default** | `false` |

* The file has **no header row**; each line is the raw source row re-written as comma-separated CSV (quoted where
  needed), whatever the source's own delimiter was.
* Rows dropped by `distinctBy` (duplicates) are *not* written; they are not "excluded", just merged.
* The file is created only if something was excluded.
* The console reports `N record(s) filtered out -> <path>` and `, N filtered out` in the summary. The count is
  kept even when `filterExcludedFile` is `false`.

---

### distinctBy

Object mode only. Keeps only the **first primary row per distinct value** of a column.

| | |
| --- | --- |
| **Type** | string (a column of the primary source) |
| **Default** | none |

Typical use: the primary source has many rows per entity (one per line item) but you want one record per entity
(one per order).

```jsonc
{ "name": "order_events", "source": "order_lines", "distinctBy": "order_id",
  "fields": [ { "name": "order_id", "from": "order_id", "type": "integer" } ],
  "children": [ { "source": "order_lines", "as": "lines", "on": { "parent": "order_id", "child": "order_id" },
                  "fields": [ /* ... */ ] } ] }
```

* The first-seen row supplies the record's own field values. Children still join against **all** rows that share
  the key (here, every line of the order), not only the kept row.
* Values compare as exact text; a blank value counts as one distinct value (`""`).
* The set of seen values is held in memory for the whole output: very high cardinality costs memory.
* Under [`externalMerge`](RunOptions.md#joinstrategy) "first" means first in the sorted order.
* Unknown column: `distinctBy column '<c>' not found in source '<alias>'.`
* **Ignored in keyedMap mode** (no error). Use [`dedup`](#dedup) there.
* Do not confuse it with the relationship-level [`distinctBy`](Relationships.md#distinctby), which dedupes
  *children* inside a record.

Sample: `order_events` in [10-filter-distinct-require](samples/10-filter-distinct-require/filters.etlconfig).

---

## keyedMap mode

In `keyedMap` mode one record is written per **group** of primary rows, and its property names are *computed
from the data*:

```json
{"email::ada.lovelace@example.com":{"status":"active","status_date":"2025-02-01","brand":"Acme Kids"}}
```

```jsonc
{
  "name": "email_map",
  "mode": "keyedMap",
  "source": "emails",
  "groupBy": "customer_id",
  "keyExpr": { "pipeline": [ { "op": "concat", "values": [ { "literal": "email::" }, { "field": "email_address" } ] } ] },
  "dedup":   { "within": "email_address", "keepMaxOf": "status_date" },
  "fields": [
    { "name": "status",      "from": "status",      "type": "string" },
    { "name": "status_date", "from": "status_date", "type": "date" }
  ]
}
```

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `groupBy` | string | Yes | Primary-source column; rows with equal values form one record (`keyedMap mode requires 'groupBy'`; unknown column: `groupBy column '<c>' not found in source '<alias>'.`) |
| `keyExpr` | object `{ "pipeline": [...] }` | Yes | Pipeline that yields each row's property name (`keyedMap mode requires a 'keyExpr' pipeline.`) |
| `dedup` | object | No | Drop duplicate rows within a group, see below |
| `fields` | array | | Fields of the value object written under each key |

#### keyExpr

The pipeline runs once per row with **no starting value**, so it must build the key from `field` and `literal`
operands (typically `concat`) or `literal`. Operands read typed values of the row. The result is converted to a
string; a **null or empty key silently drops that row**. A pipeline that *throws* (for example a `map` with no
match and no default) stops the whole run, whatever `onError` says ([Errors.md](Errors.md#keyedmap-key-failures)).

#### dedup

| Property | Type | Meaning |
| --- | --- | --- |
| `within` | string | Column whose value identifies duplicates inside a group |
| `keepMaxOf` | string | Column to maximize when choosing which duplicate to keep |

Per group, rows with the same `within` value collapse to the single row with the largest `keepMaxOf`. The
comparison is an **ordinal string comparison**, so it is only right for values that sort as text (ISO dates, equal
width numbers). Ties keep the first row. Groups keep their rows' first-seen order. Without `dedup` no row is
removed.

#### Remarks

* **Duplicate keys.** If two rows of a group produce the same key and `dedup` does not remove one, the record
  contains that property name **twice** (the writer does not detect it, and most JSON parsers keep only the
  last). Make the key unique or dedup on the column the key is built from.
* **Record order.** `hashInMemory`: groups in first-seen order of the `groupBy` value, but the *whole primary is
  buffered in memory* first. `externalMerge`: groups in ordinal order of the `groupBy` value with bounded memory;
  prefer it for large primaries ([RunOptions.md](RunOptions.md#joinstrategy)).
* **What is not supported.** `children` are accepted and validated but **never rendered** in keyedMap mode;
  `requireRelations` is rejected (`requireRelations is only supported in object mode.`); `distinctBy` and the
  random/rich [sampling](Load.md#sampling) are ignored. Lookups, coalesce, groups and `keysOf` inside `fields`
  work only to the extent they need the row itself (lookups and coalesce do; `keysOf` has no relations to read).
* `filter` and `filterExcludedFile` work per row; [`maxRecords`](RunOptions.md#maxrecords) counts groups.
* With `onError: skip`, a failing field rejects the **whole group** (the rejects file receives the group's first
  row only).

Sample: [03-keyed-map](samples/03-keyed-map/keyed-map.etlconfig).

## See also

[Relationships.md](Relationships.md), [Load.md](Load.md), [Errors.md](Errors.md),
[Transforms/Readme.md](Transforms/Readme.md), [EtlConfig.md](EtlConfig.md)
