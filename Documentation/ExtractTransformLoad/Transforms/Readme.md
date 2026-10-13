# Transform pipelines and value conversion

A **pipeline** is an ordered list of **steps** attached to an output field of an
[Extract / Transform / Load](../Readme.md) config. Each step applies one **op** (a named
transformation such as `lower`, `toDecimal` or `map`) to a running value and hands the result to the next
step. After the last step the engine coerces the result to the field's declared `type` and writes it to the
JSONL output.

Everything on these pages is verified against the engine source
(`bleak.Data.Transformer/src/bleak.Data.Transformer/Transforms/*.cs`, `Engine/RowFilter.cs`) and against real runs of the sample set in
[`../samples/transforms/`](../samples/transforms/README.md). Where the engine behaves in a surprising way the
page says so under **Remarks**.

## Contents

| Page | Covers |
| --- | --- |
| [ValueTypes.md](ValueTypes.md) | Field types (`string`, `integer`, `decimal`, `double`, `bool`, `date`, `dateTime`, `object`, `array`): input typing, parse rules, culture, rounding, output formatting, null/empty handling |
| [ConversionOps.md](ConversionOps.md) | `toString`, `toInteger` (`toInt`), `toDecimal`, `toDouble`, `toBool` |
| [DateOps.md](DateOps.md) | `toDate`, `toDateTime`, `dateFormat` |
| [NumberOps.md](NumberOps.md) | `numberFormat`, `add`, `subtract`, `multiply`, `divide` |
| [StringOps.md](StringOps.md) | `lower`, `upper`, `snakeCase`, `concat` |
| [CollectionOps.md](CollectionOps.md) | `split` |
| [ConditionalOps.md](ConditionalOps.md) | `map`, `literal`, and the field-level `coalesce` |
| [Filters.md](Filters.md) | Every operator usable in a `filter` clause (`eq`, `in`, `onOrAfter`, `empty`, ...) |
| [../samples/transforms/](../samples/transforms/README.md) | Runnable CSV + configs + real output demonstrating every op and filter operator |

## Step shape

```jsonc
{ "op": "<name>", /* op parameters */ "onError": "throw" | "null" | { "default": <value> } }
```

A pipeline is a JSON array of such objects:

```jsonc
{ "name": "price_text", "from": "price", "type": "string",
  "pipeline": [
    { "op": "multiply", "rhs": { "literal": 0.9 } },
    { "op": "numberFormat", "format": "0.00" }
  ] }
```

### Properties of a step (`StepConfig`)

Only the properties an op reads are used; others are silently ignored (for example `format` on `lower`).

| Property | Type | Used by | Meaning |
| --- | --- | --- | --- |
| `op` | string, required | all | Op name. Matched case-insensitively (`TOSTRING` works). A missing/blank `op` or an unknown name fails the run at config-compile time. |
| `onError` | `"throw"` \| `"null"` \| `{ "default": v }` | all | What to do when the step throws. See [Error behavior](#error-behavior). Default `"throw"`. The strings are matched case-insensitively. |
| `format` | string | `toDate`, `toDateTime`, `dateFormat`, `numberFormat` | Parse format (`toDate`/`toDateTime`) or output format (`dateFormat`/`numberFormat`). |
| `culture` | string | conversions, `dateFormat`, `numberFormat` | .NET culture name such as `en-US`, `de-DE`. Invariant culture when omitted. |
| `rhs` | operand | `add`, `subtract`, `multiply`, `divide` | Right-hand operand. |
| `values` | array of operands | `concat` | Operands to join. |
| `separator` | string | `concat`, `split` | Join / split string. |
| `cases` | object | `map` | Lookup table: input (as text) to literal. |
| `default` | JSON scalar | `map`, `literal` | `map`: fallback value. `literal`: the constant to emit. |

### Operands

`add`, `subtract`, `multiply`, `divide` and `concat` take **operands**. An operand is an object with exactly
one of these properties (checked in this order; the first present one wins):

| Operand | Resolves to |
| --- | --- |
| `{ "value": true }` | the running pipeline value (the previous step's output) |
| `{ "field": "col" }` | the **typed** value of column `col` on the current source row (typed per the source's `fields` declaration, default string). A blank cell is `null`. A column name that does not exist is **not** a config error; it resolves to `null` at run time. Column names are case-insensitive. |
| `{ "literal": v }` | a constant. JSON strings stay strings; integers become 64-bit integers; numbers with a fraction/exponent become `double`; `true`/`false` become booleans. |

An operand with none of the three (including `{ "literal": null }` or `{ "value": false }`) fails the run
with `an operand must specify one of: field, literal, or value:true.`

> A fractional JSON literal such as `0.9` is a `double`. Arithmetic that touches a `double` is done in
> floating point; see [NumberOps](NumberOps.md#remarks-common-to-the-arithmetic-ops).

## Where pipelines run

| Location | Starts from | Notes |
| --- | --- | --- |
| `fields[].pipeline` (any output, child or object-group field) | the typed value of `from` (or `null` when `from` is omitted) | The common case. |
| `fields[].pipeline` on a **coalesce** field | the winning candidate's raw text | [Details](ConditionalOps.md#coalesce-field-level). |
| `fields[].pipeline` on a **lookup** field | each looked-up value | Applied per element for array lookups. Per-element results are coerced to string. |
| `lookup.expr` (with optional `lookup.from`) | the lookup-source row's typed `from` column | Evaluated once per row of the lookup source while building its index. A row whose expression throws is skipped (the reason is logged), not fatal. |
| `keyBy.pipeline` on a keyed child relationship | the child row's typed `keyBy.from` column | A failure drops that child from the keyed object and records a warning. |
| `mergePrefix.pipeline` | the child row's typed `mergePrefix.from` column | Failure records a warning and drops the child. |
| `keyExpr.pipeline` (keyedMap outputs) | `null` (there is no `from`) | Must produce the dynamic property name from `field`/`literal` operands, e.g. via `concat`. A null/empty result skips that row. |

Pipelines are **not** applied to filter conditions: [`filter`](Filters.md) compares the raw CSV text.

## Execution order

For one field of one row the engine does the following ([`RecordWriter.WriteField`](https://github.com/jamalkhan/bleak.Data.Transformer/blob/main/src/bleak.Data.Transformer/Engine/RecordWriter.cs)):

1. Read the starting value: the typed value of `from` (raw cell parsed per the source's `fields` type; an
   empty cell becomes `null`). With no `from`, the start is `null`.
2. Run each step in array order. Each step receives the previous step's output as its **running value**.
3. Coerce the final value to the field `type` ([ValueTypes](ValueTypes.md)). Failures here are *not* step
   errors; they follow the field/run policy (below).
4. Emit. `null` is omitted (unless `emitNull`), an empty string is omitted (unless run `emitEmpty`), an
   empty array is omitted.

Steps run strictly in sequence; there is no branching. If you need conditional logic use
[`map`](ConditionalOps.md#map), `coalesce`, or a [filter](Filters.md).

## Null and empty semantics

| Situation | Behavior |
| --- | --- |
| Empty CSV cell (`""`) in a column | Becomes `null` when the column is read (typed or untyped). Whitespace-only cells (`" "`) are **not** empty: they stay as a one-space string (and fail numeric/date parses). |
| `null` running value into `toString`, `toInteger`, `toDecimal`, `toDouble`, `toBool`, `toDate`, `toDateTime`, `dateFormat`, `numberFormat`, `lower`, `upper`, `snakeCase`, `split` | Returns `null` (no error). |
| `null` into `add`/`subtract`/`multiply`/`divide` (as left **or** right operand) | Throws `arithmetic operand is null`. Use `onError` to substitute. |
| `null` into `map` | Looks the key up as a missing key: returns `default` when one is configured, otherwise throws `map: no case for ''...`. |
| `null` into `concat` | Never fails: null operands contribute nothing (but separators are still inserted). Result is always a string. |
| `null` into `literal` | Ignored; the constant is returned. |
| Final `null` | Field omitted from the record, unless the field sets `"emitNull": true` (writes `null`). |
| Final `""` (string type) | Omitted unless the run sets `"emitEmpty": true`. With `emitEmpty`, a **plain pass-through** string field (no steps) keeps its empty cell as `""`; any field with at least one step does not (the blank cell is already `null` by then). |

## Error behavior

There are three layers, from the inside out:

1. **Step `onError`**: governs a failure inside one op (parse failure, divide by zero, unmapped `map` key, ...).
   - `"throw"` (default): the failure becomes an `EtlRowException` (`transform 'toInteger' failed: <reason>`) and
     moves to layer 2.
   - `"null"`: the step yields `null` and the pipeline continues (later steps see `null`).
   - `{ "default": v }`: the step yields `v` (converted from the JSON literal) and the pipeline continues. The
     default is still subject to the final type coercion, e.g. a default of `"n/a"` on an `integer` field fails
     in layer 2.
2. **Field `onError`**: `"onError": "warnAndNull"` on the *field* (not the step). When the pipeline or the final
   coercion throws, only that field is omitted, the record is kept, and a row is added to
   `<fileBaseName>_<timestamp>_warnings.csv` (tab-separated `record_key`, `field`, `reason`, `raw_value`; `record_key` is
   derived from the join key of the relationship the row belongs to and is blank for a config without
   relationships, see [Errors.md](../Errors.md#how-record_key-is-derived)). Any other value is a config
   error (`expected 'warnAndNull'`).
3. **Run `run.onError`**: applies when neither of the above handled it.
   - `skip` (default): the whole record is dropped and written to `<fileBaseName>_<timestamp>_rejects.csv`
     (a `# reason<TAB>row` header, then a `# field '<name>': <reason>` comment line followed by the original CSV row).
   - `nullify`: just that field is omitted; the record is kept. No warning is written.
   - `abort`: the run stops immediately: the console shows `[FAIL] EtlRowException: ...`.

Samples: [errors-skip, errors-nullify, errors-abort](../samples/transforms/README.md#what-to-look-at).

A scientific-notation cell (e.g. `1.5e-05`) read into a `decimal`/`double` field *succeeds* but adds a warning row
(`scientific notation in a numeric column`).

Config mistakes (unknown op, missing required parameter such as `rhs`, `format`, `values` or `cases`, a bad
`onError` value, an operand with no `field`/`literal`/`value`) abort the run at compile time with
`EtlConfigException`, before the offending output writes anything (earlier outputs in the same config may already
have been written). Two compile-time failures are not wrapped as `EtlConfigException`: duplicate `map` case keys
differing only by case surface as `ArgumentException`, and an unparseable `type` value is reported as
`Could not parse config`.

## Op reference

| Op | Aliases | Summary | Page |
| --- | --- | --- | --- |
| [`toString`](ConversionOps.md#tostring) | | Convert the running value to text. | ConversionOps |
| [`toInteger`](ConversionOps.md#tointeger) | `toInt` | Convert to a 64-bit integer (decimals round half away from zero). | ConversionOps |
| [`toInt`](ConversionOps.md#toint) | alias of `toInteger` | Identical to `toInteger`. | ConversionOps |
| [`toDecimal`](ConversionOps.md#todecimal) | | Convert to an exact decimal (culture-aware parse). | ConversionOps |
| [`toDouble`](ConversionOps.md#todouble) | | Convert to a double-precision float. | ConversionOps |
| [`toBool`](ConversionOps.md#tobool) | | Convert to boolean (`1`/`0`/`true`/`false`). | ConversionOps |
| [`toDate`](DateOps.md#todate) | | Parse a date (optional exact `format`); output renders as `yyyy-MM-dd`. | DateOps |
| [`toDateTime`](DateOps.md#todatetime) | | Parse a date and time; output renders as ISO-8601 `...Z`. | DateOps |
| [`dateFormat`](DateOps.md#dateformat) | | Format a date as text with a .NET format string. | DateOps |
| [`numberFormat`](NumberOps.md#numberformat) | | Format a number as text with a .NET numeric format string. | NumberOps |
| [`add`](NumberOps.md#add) | | Running value + operand. | NumberOps |
| [`subtract`](NumberOps.md#subtract) | | Running value - operand. | NumberOps |
| [`multiply`](NumberOps.md#multiply) | | Running value x operand. | NumberOps |
| [`divide`](NumberOps.md#divide) | | Running value / operand. | NumberOps |
| [`lower`](StringOps.md#lower) | | Lowercase (invariant culture). | StringOps |
| [`upper`](StringOps.md#upper) | | Uppercase (invariant culture). | StringOps |
| [`snakeCase`](StringOps.md#snakecase) | | `"Hats & Caps"` becomes `"hats_caps"`. | StringOps |
| [`concat`](StringOps.md#concat) | | Join fields, literals and/or the running value into one string. | StringOps |
| [`split`](CollectionOps.md#split) | | Split text into an array of trimmed, non-empty strings. | CollectionOps |
| [`map`](ConditionalOps.md#map) | | Case-insensitive lookup table with optional default. | ConditionalOps |
| [`literal`](ConditionalOps.md#literal) | | Ignore the input and emit a constant. | ConditionalOps |

There is no `trim`, `replace`, `substring`, `round`, `if` or regex op. The registry is the complete list (it is
built in `TransformRegistry.CreateDefault`); a new op is added by registering a factory in code.

## See also

- [Extract / Transform / Load command](../Readme.md)
- [Samples](../samples/transforms/README.md) and its [op to sample-field cross-reference](../samples/transforms/README.md#op-to-sample-field-cross-reference)
- Config schema reference for the rest of the config (sources, relationships, load settings): see the parent folder.
