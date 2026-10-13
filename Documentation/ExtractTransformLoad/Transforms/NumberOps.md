# Number ops

[`numberFormat`](#numberformat) renders a number as text. [`add`](#add), [`subtract`](#subtract),
[`multiply`](#multiply) and [`divide`](#divide) combine the running value with an [operand](Readme.md#operands).

Sample data: [`items.csv`](../samples/transforms/items.csv), [`transforms.etlconfig`](../samples/transforms/transforms.etlconfig),
output groups `format` and `math` in [expected-output/transforms](../samples/transforms/expected-output/transforms/).

---

### numberFormat

Formats a numeric running value as text using a .NET numeric format string.

**Syntax**

```jsonc
{ "op": "numberFormat", "format": "N2", "culture": "en-US" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `format` | string | **yes** | none | any .NET standard (`N2`, `F1`, `C2`, `P1`, `E3`, `G`) or custom (`0.00`, `#,##0`, `00000000`) numeric format string. Missing/empty stops the run: `numberFormat requires 'format'.` |
| `culture` | string | no | invariant | .NET culture name: decimal/group separators, currency symbol, percent pattern |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- Output type should be `string`; the result is text such as `"1,234.50"`.
- Integers and decimals are formatted as decimals; doubles as doubles. Decimals round half away from zero
  (`N2` of `0.125` is `0.13`); doubles format from their binary value (`N2` of `0.125` is `0.12`).
- A **string** input is parsed first, using the step's `culture` (so `"2.5"` with `de-DE` is read as `25`: the
  `.` is a thousands separator there and the result is `"25,0"` for `N1`). Booleans become 1/0; dates throw.
  Non-numeric text throws.
- Not every specifier suits every type: `D4` is for integers only and throws `Format specifier was invalid` for
  decimals (use a custom format such as `0000`). The invariant culture's `C` format uses the generic currency sign
  rather than `$`; give a `culture` such as `en-US`.
- `P` multiplies by 100 (`0.125` gives `12.5%` with `P1`).
- `null` gives `null`. To round and then do arithmetic again, follow with [`toDecimal`](ConversionOps.md#todecimal)
  (see sample `chain_discounted_price`).

**Example**

| Input | Config | Output |
| --- | --- | --- |
| decimal `1234.50` | `N2`, `en-US` | `"1,234.50"` |
| decimal `1234.50` | `N2`, `de-DE` | `"1.234,50"` |
| decimal `1234.50` | `C2`, `en-US` | `"$1,234.50"` |
| integer `1001` | `00000000` | `"00001001"` |
| double `0.125` | `P1`, `en-US` | `"12.5%"` |
| text `"3"` | `0.0` | `"3.0"` |

Sample fields (group `format`): `numberFormat_n2`, `numberFormat_n2_de`, `numberFormat_currency`,
`numberFormat_pad`, `numberFormat_percent`, `numberFormat_string_input`.

**See also** [`dateFormat`](DateOps.md#dateformat), [`toDecimal`](ConversionOps.md#todecimal), [`toString`](ConversionOps.md#tostring)

---

## Remarks common to the arithmetic ops

`add`, `subtract`, `multiply` and `divide` all have the same shape: `left <op> rhs` where **left** is the running
value and **rhs** is the operand.

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `rhs` | operand | **yes** | none | `{ "literal": n }`, `{ "field": "col" }`, `{ "value": true }`. Missing `rhs` stops the run (`add requires 'rhs'.`). |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

- **Operand types.** Left and right may be integers, decimals, doubles, booleans (as 1/0) or numeric text.
  Text is parsed with the **invariant** culture and thousands separators allowed (`"1,234.50"` is `1234.50`);
  there is no `culture` parameter. Dates and non-numeric text throw (`Invalid cast from 'DateTime' to 'Decimal'`,
  `The input string 'abc' was not in a correct format.`).
- **Exact vs floating.** If either side is a `double` (a typed `double` column, or a literal with a fraction or
  exponent such as `0.9`), the math is done in **double** precision; otherwise it is done in exact **decimal**.
  A `double` result written to a `decimal` field is rounded to 15 significant digits. Write money literals
  that you need exact as quoted strings (`"literal": "0.90"`) to stay in decimal math.
- **Null operand.** If left or right is `null` (a blank cell, a missing column name) the step throws
  `arithmetic operand is null`. Use `onError: "null"` to propagate the blank, or `{ "default": 0 }` to treat it as zero.
- **Integer fields.** Decimal results go through the usual rounding (half away from zero) when the field `type`
  is `integer`: `7 / 2` is `3.5` in the pipeline and `4` in an integer field.
- **Division by zero.** Decimal division throws (`Attempted to divide by zero.`), so `onError` can handle it.
  Double division by zero produces infinity/NaN *without* throwing, and writing it to a `double` field **stops the
  run** (`.NET number values such as positive and negative infinity cannot be written as valid JSON`). Convert the
  left side to decimal with `toDecimal` first if zero divisors are possible.
- Decimal results keep a scale: `19.99 * 3` is `59.97`, `1234.50 * 2` is `2469.00`, `2.51 / 2` is `1.255`,
  and `19.99 / 3` yields all 28 digits (`6.6633333333333333333333333333`). Format or round afterwards if needed.
- `{ "field": ... }` reads the **typed** value of the column on the current row (a `decimal`-typed column
  contributes an exact decimal; an untyped column contributes text that is parsed as above).
- Operand evaluation uses the current *node's* row: in a child relationship, `field` refers to the child row.

### add

Running value + operand.

```jsonc
{ "op": "add", "rhs": { "field": "discount" }, "onError": "null" }
```

| Input (left) | rhs | Output |
| --- | --- | --- |
| qty `"3"` | literal `10` | `13` |
| price `19.99` | field `discount` = `"12.5"` | `32.49` |
| price `5` | field `discount` = blank, `onError: "null"` | *(omitted)* |
| discount blank | literal `1`, `onError: {default: 0}` | `0` |
| discount `"12.5"` | literal `1`, `onError: {default: 0}` | `13.5` |

Sample fields: `add_literal`, `add_field`, `add_null_defaulted`. Note that `add` never concatenates text: use
[`concat`](StringOps.md#concat).

**See also** [`subtract`](#subtract), [`concat`](StringOps.md#concat)

### subtract

Running value - operand.

```jsonc
{ "op": "subtract", "rhs": { "literal": 0.99 } }
```

| Input (left) | rhs | Output |
| --- | --- | --- |
| price `19.99` | literal `0.99` | `19` |
| price `1234.50` | literal `0.99` | `1233.51` |
| price `0.1` | literal `0.99` | `-0.89` |

Sample field: `subtract_literal`. The `0.99` literal is a double, so these are computed in floating point and
then rounded to 15 digits into the decimal field.

**See also** [`add`](#add)

### multiply

Running value x operand.

```jsonc
{ "op": "multiply", "rhs": { "field": "qty" } }
{ "op": "multiply", "rhs": { "value": true } }   // square the running value
```

| Input (left) | rhs | Output |
| --- | --- | --- |
| price `19.99` | field `qty` = `"3"` | `59.97` |
| price `1234.50` | field `qty` = `"2"` | `2469.00` |
| price `100` | field `qty` = `"0"` | `0` |
| qty `12` (after `toInteger`) | `value: true` | `144` |

Sample fields: `multiply_field` (line total), `multiply_value_square`, and `chain_discounted_price` (a
`multiply` by `0.9`, `numberFormat` `0.00`, `toDecimal` chain: `19.99` becomes `17.99`).

**See also** [`divide`](#divide), [`numberFormat`](#numberformat)

### divide

Running value / operand.

```jsonc
{ "op": "divide", "rhs": { "field": "qty" }, "onError": "null" }
```

| Input (left) | rhs | Output |
| --- | --- | --- |
| score `"2.5"` | literal `2` | `1.25` |
| price `19.99` | field `qty` = `"3"` | `6.6633333333333333333333333333` |
| price `100` | field `qty` = `"0"`, `onError: "null"` | *(omitted; the divide-by-zero was nulled)* |
| ratio (double) `0.125` | literal `3` | `0.041666666666666664` |

Sample fields: `divide_literal`, `divide_field_by_zero_nulled`, `divide_double`.

**See also** [`multiply`](#multiply), [Remarks: division by zero](#remarks-common-to-the-arithmetic-ops)
