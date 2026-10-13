# Value types and conversion

Values in the engine are typed in three places:

1. **Input typing**: a source column may declare a type in the source's `fields` map; the raw CSV cell is parsed
   when it is read.
2. **Step conversions**: the `to*` ops convert the running value mid-pipeline ([ConversionOps](ConversionOps.md),
   [DateOps](DateOps.md)).
3. **Output coercion**: after the last step the value is converted to the field's `type` and written as JSON.

All three use one converter (`ValueConvert.To`), so the parse rules below apply everywhere.

## Field types

`type` accepts these names (case-insensitive: `"integer"`, `"dateTime"` and `"DATETIME"` all work). Anything
else fails with `Could not parse config`.

| Type | CLR value | JSON output | Notes |
| --- | --- | --- | --- |
| `string` (default) | text | JSON string | Empty strings are omitted unless run `emitEmpty`. |
| `integer` | 64-bit signed | JSON number | Decimals/doubles are rounded half away from zero. |
| `decimal` | exact decimal | JSON number | Keeps its scale (`1234.50` stays `1234.50`, `0.0` stays `0.0`). Use for money. |
| `double` | 64-bit float | JSON number | Written in shortest round-trip form; very small/large values use exponent form (`1.5E-05`). |
| `bool` | boolean | `true` / `false` | |
| `date` | date and time (kind kept) | `"yyyy-MM-dd"` | The time-of-day is dropped **only when written**; inside the pipeline a `date` value still carries its time. |
| `dateTime` | date and time | `"yyyy-MM-ddTHH:mm:ssZ"` | Fractional seconds are dropped. The `Z` is a literal: no zone conversion is applied. |
| `object` | n/a | nested object | An object-group field: has its own `fields`/`children`. Not a scalar type. |
| `array` | list | JSON array | The value produced by [`split`](CollectionOps.md#split) or an array lookup. Empty arrays are omitted. |

Inside a JSON array, `dateTime`/`date` elements are always written in the long `...Z` form and decimals/doubles
as numbers.

## Input typing

```jsonc
"sources": {
  "items": {
    "file": "items.csv",
    "fields": {
      "price":  { "type": "decimal" },
      "signup": { "type": "date" },
      "dob":    { "type": "date", "format": "MM/dd/yyyy" },
      "amount": { "type": "decimal", "culture": "de-DE" }
    }
  }
}
```

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `type` | one of the types above | `string` | Target type for the column. |
| `format` | string | none | `date`/`dateTime` only: exact parse format (`DateTime.ParseExact`). ISO-style parsing when omitted. |
| `culture` | string | invariant | Culture for number and (format-less) date parsing. |

Remarks:

- Columns not listed are strings. A column that is listed but absent from the file is ignored.
- An **empty cell** becomes `null` for every type. A whitespace-only cell is not empty: for a typed column it is
  parsed and fails (`The input string ' ' was not in a correct format.`); for an untyped column it stays `" "`.
- A cell that does not parse raises an error **when a field reads it**, so it follows the usual
  [error layers](Readme.md#error-behavior) (reject the record by default). Columns that no field reads cause
  no error even if malformed.
- Input types affect pipelines and `{ "field": ... }` operands only. Joins, `filter` conditions, `distinctBy`,
  `groupBy` and lookups compare the **raw text**, so `00123` stays `00123` there.

## Conversion rules

`To(value, targetType, format, culture)` is used for step conversions, input typing and output coercion. A `null`
input always yields `null`. "Throws" means a `FormatException`, `OverflowException` or `InvalidCastException`,
reported as `transform '<op>' failed: <message>` (or `field '<name>': <message>` for output coercion).

### To string

| From | Result |
| --- | --- |
| string | unchanged (no trimming) |
| date/dateTime | always `yyyy-MM-ddTHH:mm:ssZ`, invariant culture, no zone conversion. A `date` becomes `2020-01-15T00:00:00Z`. |
| integer, decimal, double | `ToString(null, culture)`: decimals keep their scale (`1234.50`); the culture picks the decimal separator (`de-DE`: `19,99`) |
| bool | `True` / `False` (capitalized!) |
| a list (from `split`) | **not meaningful**: yields the CLR type name. Use `"type": "array"` for list values. |

### To integer

| From | Result |
| --- | --- |
| integer | unchanged |
| string | `long.Parse` with *integer* number style: optional leading/trailing whitespace and a leading sign only. `"+7"` and `" 7 "` work; `"2.5"`, `"1,234"`, `"1e3"`, `"abc"`, `" "` throw. Larger than 64 bits throws `Value was either too large or too small for an Int64`. |
| decimal, double | rounded to the nearest integer, **half away from zero**: `2.5` becomes `3`, `-2.5` becomes `-3`, `-0.5` becomes `-1`, `3.49` becomes `3`. NaN/Infinity/out-of-range throw. |
| bool | `true` is `1`, `false` is `0` |
| date | throws (`Invalid cast from 'DateTime' to 'Int64'`) |

To go from a decimal-looking string to an integer, chain `toDecimal` then `toInteger`.

### To decimal / double

| From | Result |
| --- | --- |
| decimal / double (respectively) | unchanged |
| string | `decimal.Parse` / `double.Parse` with *float + thousands* style, in the step's `culture`: optional whitespace, sign, a decimal point, an exponent (`1e3` is `1000`) and **thousands separators** (`1,234.50`). No currency symbols: `$5.00` throws. |
| integer | exact |
| double to decimal | rounded to 15 significant digits |
| bool | `1` / `0` |
| date | throws |

> **Culture trap.** The thousands separator is accepted anywhere, so parsing the wrong culture can *succeed with
> a wrong value*: `"9,99"` with the invariant culture is `999`, not `9.99`. Declare the right `culture` (`de-DE`
> reads `9,99` as 9.99 and `1.234,50` as 1234.50). See sample fields `toDecimal_eu` and
> `toDecimal_invariant_misparse`.

### To bool

| From | Result |
| --- | --- |
| bool | unchanged |
| string | after trimming: `1` is true, `0` is false; otherwise `true`/`false` in any letter case. Anything else (`yes`, `y`, `12`, `maybe`) throws. |
| integer / decimal / double | non-zero is true, zero is false |
| date | throws |

### To date / dateTime

`date` and `dateTime` use the same conversion; they differ only in how the value is written.

| From | Result |
| --- | --- |
| date/dateTime | unchanged |
| string, no `format` | A bare month-day (`^\d{1,2}-\d{1,2}$`, e.g. `9-3`, `02-29`) is **rejected**: `'9-3' has no year (month-day only) - refusing to guess a year.` Otherwise `DateTime.Parse(text, culture, RoundtripKind)`. |
| string, with `format` | `DateTime.ParseExact(text, format, culture)`: the text must match the format exactly. The no-year guard does **not** apply: `format: "M-d"` accepts `9-3` and fills in the *current* year. |
| any other value (e.g. an integer) | its invariant text is parsed as above (so `20240101` with `format: "yyyyMMdd"` works) |

Format-less parsing is culture-dependent: `05/06/2024` is May 6 for the invariant/`en-US` culture and June 5 for
`de-DE` (verified by running both cultures; see [DateOps](DateOps.md#todatetime)). Prefer ISO-8601 or an explicit
`format`.

Time zones: text ending in `Z` is read as UTC and text with no zone is read as-is; both are then written with a
literal `Z` and **no conversion**. Text with a numeric offset (`2024-03-05T14:30:00+02:00`) is converted to the
**machine's local time zone** before being labelled `Z`, so the result depends on where the engine runs. Strip
or normalize offsets upstream, or parse with an explicit `format` that has no offset (see
[DateOps](DateOps.md#todatetime)).

## Output coercion (the field `type`)

After the pipeline, `ValueConvert.To(result, type)` runs (no `format`/`culture`: use a conversion step first if
you need them). Then the value is written:

| `type` | Written as |
| --- | --- |
| `integer` | JSON number |
| `decimal` | JSON number, scale preserved (`59.97`, `273.0`, `2469.00`) |
| `double` | JSON number (`0.041666666666666664`, `5E-06`). `NaN`/`Infinity` cannot be written as JSON: the run fails with `ArgumentException` rather than skipping the row. |
| `bool` | `true`/`false` |
| `date` | `"2020-01-15"` |
| `dateTime` | `"2020-01-15T00:00:00Z"` |
| `string` | JSON string; `""` omitted unless `emitEmpty` |
| `array` | JSON array of strings/numbers/bools; empty array omitted |

`null` is never written unless the field sets `"emitNull": true`.

## Rounding summary

| Where | Rule |
| --- | --- |
| decimal/double to integer (conversion or output) | round half **away from zero** |
| double to decimal | 15 significant digits |
| `numberFormat` | .NET format-string rules: decimals round half away from zero (`N2` of decimal `0.125` is `0.13`, `N0` of `2.5` is `3`); doubles are formatted from their exact binary value (`N2` of double `0.125` is `0.12`, of `2.675` is `2.67`) |
| decimal arithmetic | exact; `divide` yields up to 28-29 significant digits (`6.6633333333333333333333333333`). Add a `numberFormat` + `toDecimal` pair to round. |

## Samples

Fields in [`convert`](../samples/transforms/expected-output/transforms/) outputs: `typed_*` (input typing),
`toString_*`, `toInteger_*`, `toDecimal_*`, `toBool_flag`, `toDate_*`, `toDateTime_*`.

## See also

[Readme](Readme.md) | [ConversionOps](ConversionOps.md) | [DateOps](DateOps.md) | [Filters](Filters.md)
