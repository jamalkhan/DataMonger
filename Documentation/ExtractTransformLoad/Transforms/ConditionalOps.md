# Conditional and constant ops

[`map`](#map) translates values through a lookup table, [`literal`](#literal) emits a constant, and the
field-level [`coalesce`](#coalesce-field-level) feature picks the first non-empty of several columns. None of
these is a general "if": for row selection use a [filter](Filters.md).

Sample data: [`items.csv`](../samples/transforms/items.csv), [`transforms.etlconfig`](../samples/transforms/transforms.etlconfig),
output group `conditional` ([`conditional_*.jsonl`](../samples/transforms/expected-output/transforms/)).

---

### map

Looks the running value up in a fixed table of `cases` and returns the matching literal, or a `default`.

**Syntax**

```jsonc
{ "op": "map",
  "cases":   { "A": "acme", "K": "acme-kids" },
  "default": "other" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `cases` | object | **yes** | none | keys: the input as text; values: any JSON scalar (string, number, `true`/`false`, `null`). Omitting `cases` stops the run (`map requires 'cases'.`); `{}` is allowed. |
| `default` | JSON scalar | no | none (error on miss) | returned when no case matches or the input is `null` |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- **Matching** is on the *text form* of the running value, case-**insensitively** (ordinal). Text form: strings
  unchanged (no trimming), integers `1001`, decimals with their scale (`12.0` is `"12.0"`, not `"12"`),
  booleans `True`/`False`, dates `yyyy-MM-ddTHH:mm:ssZ`. A numeric key therefore needs the exact text; run
  `toString`/`toInteger` first to normalize.
- **No match and no `default`** throws `map: no case for 'X' and no default was configured.` Choose: a
  `default`, `onError: "null"` (field omitted), or let the row be rejected (default policy) so unexpected codes
  surface.
- **`"default": null` is the same as no default** (JSON null is read as "absent"), so a null default cannot be
  expressed; use `onError: "null"` instead. A **case value** of `null` is fine and yields `null` (field omitted).
- A `null` running value (blank cell) is a miss: it returns `default` if configured, else throws
  (`no case for ''`).
- Case values keep their JSON type: `3` is an integer, `1.5` a double, `true` a boolean, `"x"` a string; the
  field's final `type` then coerces them. Objects/arrays are not meaningful (stored as raw JSON text).
- Two cases whose keys differ only by letter case (`"A"` and `"a"`) stop the run at compile time with
  `ArgumentException: An item with the same key has already been added.`
- Common uses: brand/status code translation, yes/no vocabularies (`map_flag`), and as a `keyBy`/`lookup.expr` step
  that turns a code into a dynamic property name (see [Readme](Readme.md#where-pipelines-run)).

**Example**

| Input | Config | Output |
| --- | --- | --- |
| `A` | cases A, K; default `other` | `"acme"` |
| `Z` | same | `"other"` |
| `Z` | cases A, K; no default; `onError: "null"` | *(omitted)* |
| `gold` | cases `GOLD`:3, `Silver`:2; default 1 (type integer) | `3` |
| `silver` | same | `2` |
| `bronze` | same | `1` |
| `yes` | cases `1`:true, `0`:false, `true`:true, `false`:false; default false (type bool) | `false` |

Sample fields: `map_brand`, `map_tier_rank`, `map_no_default_nulled`, `map_flag`.

**See also** [`toBool`](ConversionOps.md#tobool), [`literal`](#literal), [coalesce](#coalesce-field-level)

---

### literal

Ignores the running value and returns a constant.

**Syntax**

```jsonc
{ "name": "source", "type": "string", "pipeline": [ { "op": "literal", "default": "ACME-FEED" } ] }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `default` | JSON scalar | no | `null` | the constant. (The parameter is named `default`, not `value`.) |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | never triggers |

**Remarks**

- Because it ignores its input, `literal` is typically the only step of a field with **no `from`**: a
  hard-coded column (a feed name, a flag, a schema version).
- With no `default` (or `"default": null`) the result is `null`, so the field is **omitted**. Add
  `"emitNull": true` to the *field* to write an explicit JSON `null` instead (the one exception to the engine's
  never-emit-null rule).
- JSON typing: `"ACME-FEED"` string, `1` integer, `1.5` double, `false` boolean; the field's `type` coerces
  further (`"default": "1"` with `type: "integer"` gives `1`). An object/array default is kept as its raw JSON text.
- A `literal` in the middle of a pipeline discards everything before it.
- `literal` is also how you give a step-level default a value of your own type: see `onError: {default}`.

**Example**

| Config | Output |
| --- | --- |
| `default: "ACME-FEED"`, `type: string` | `"ACME-FEED"` |
| `default: 1`, `type: integer` | `1` |
| `default: false`, `type: bool` | `false` |
| `from: name`, `default: "constant"` | `"constant"` |
| no `default` | *(omitted)* |
| no `default`, field `emitNull: true` | `null` |

Sample fields: `literal_text`, `literal_int`, `literal_bool`, `literal_ignores_input`, `literal_null_omitted`,
`literal_null_emitted`.

**See also** [`map`](#map), [Null and empty semantics](Readme.md#null-and-empty-semantics)

---

### coalesce (field-level)

Not an op: a **field** property that emits the first non-empty candidate. It is documented here because it is the
usual companion of `map`/`literal` for fallbacks.

```jsonc
{ "name": "preferred_name", "type": "string",
  "coalesce": [ { "from": "middle" }, { "from": "name" } ] }

{ "name": "name_source", "type": "string", "coalesceEmit": "label",
  "coalesce": [ { "from": "middle", "label": "middle" }, { "from": "name", "label": "name" } ] }
```

| Property | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `coalesce` | array of candidates | yes (to use the feature) | none | each candidate has exactly one of `from` (a column on this row) or `lookup` (a scalar lookup on a related source), plus an optional `label` |
| `coalesceEmit` | string | no | `"value"` | `"value"` emits the winning candidate's text; `"label"` emits its `label` (every candidate then needs a `label`) |

**Remarks**

- A candidate wins if its **raw text** is non-empty (empty string/blank cell loses; a whitespace-only value wins).
  Candidates are tried in order; if none wins the value is `null` (field omitted).
- If the field has a `pipeline`, it runs on the winner's raw text (not typed per the source) and the result is coerced
  to the field's `type`. With `coalesceEmit: "label"` the pipeline is not applied: the label is emitted.
- Config errors: a candidate with both/neither of `from` and `lookup`; a `from` column that does not exist;
  an unknown `coalesceEmit`; `"label"` with a candidate lacking a `label`.
- Pair a `value` field and a `label` field over the same candidate list to emit both "the contact" and "which
  kind of contact it is".

**Example (sample rows)**

| middle | name | `coalesce_middle_or_name` | `coalesce_source` |
| --- | --- | --- | --- |
| `Augusta` | Ada Lovelace | `"Augusta"` | `"middle"` |
| *(blank)* | Alan Turing | `"Alan Turing"` | `"name"` |

Sample fields: `coalesce_middle_or_name`, `coalesce_source`.

**See also** [`map`](#map), [Null and empty semantics](Readme.md#null-and-empty-semantics)
