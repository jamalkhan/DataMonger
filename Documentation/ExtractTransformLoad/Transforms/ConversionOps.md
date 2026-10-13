# Conversion ops

Ops that change the **type** of the running value: [`toString`](#tostring), [`toInteger`](#tointeger) /
[`toInt`](#toint), [`toDecimal`](#todecimal), [`toDouble`](#todouble), [`toBool`](#tobool). Dates have their own
page: [`toDate`, `toDateTime`](DateOps.md). All of them share the conversion rules in
[ValueTypes](ValueTypes.md#conversion-rules).

Why convert mid-pipeline when the field already has a `type`? The final `type` coercion happens *after* the
last step, with no `culture` or `format`. A conversion step lets you parse with a specific culture, round before
doing arithmetic, or change the type that the next op sees (for example `toDecimal` then `toInteger` to round a
decimal-looking string).

Common to all conversion ops:

- `null` in gives `null` out (no error, no `onError` needed).
- Parse/overflow failures throw and are handled by the step's [`onError`](Readme.md#error-behavior).
- Sample data: [`items.csv`](../samples/transforms/items.csv), config
  [`transforms.etlconfig`](../samples/transforms/transforms.etlconfig), output group `convert`
  ([`convert_*.jsonl`](../samples/transforms/expected-output/transforms/)).

---

### toString

Converts the running value to text.

**Syntax**

```jsonc
{ "op": "toString", "culture": "en-US", "onError": "null" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `culture` | string | no | invariant | any .NET culture name; affects decimal/double text only |
| `format` | string | no | n/a | accepted but **ignored** (use [`numberFormat`](NumberOps.md#numberformat) / [`dateFormat`](DateOps.md#dateformat)) |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- Strings are returned unchanged. Dates/datetimes always become `yyyy-MM-ddTHH:mm:ssZ` (a `date` gets
  `T00:00:00Z`), regardless of culture. Integers/decimals/doubles use the culture; decimals keep their scale
  (`1234.50`). Booleans become `True`/`False` with a capital letter.
- Never throws for scalar input. A list (the output of [`split`](CollectionOps.md#split)) is turned into the
  .NET type name; do not `toString` a list.
- Useful before `map` (whose lookup key is the value's text) or when a typed input column must be emitted as text.

**Example**

| Input (typed source column) | Output |
| --- | --- |
| integer `1001` | `"1001"` |
| decimal `1234.50` | `"1234.50"` |
| decimal `5` | `"5"` |
| date `2020-01-15` | `"2020-01-15T00:00:00Z"` |
| `toBool` of `"1"`, then `toString` | `"True"` |
| `null` | *(field omitted)* |

Sample fields: `toString_id`, `toString_price`, `toString_signup`, `toString_bool`.

**See also** [`numberFormat`](NumberOps.md#numberformat), [`dateFormat`](DateOps.md#dateformat), [ValueTypes: To string](ValueTypes.md#to-string)

---

### toInteger

Converts the running value to a 64-bit integer. Alias: [`toInt`](#toint).

**Syntax**

```jsonc
{ "op": "toInteger", "culture": "en-US", "onError": { "default": -1 } }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `culture` | string | no | invariant | .NET culture name; used only when parsing a string |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- From a **string** the text must be a whole number: optional surrounding whitespace and a leading sign, nothing
  else. `"2.5"`, `"12.0"`, `"1,234"` and `"1e3"` all throw (`The input string '2.5' was not in a correct format.`).
  Values beyond 64 bits throw an overflow error.
- From a **decimal or double** the value is rounded half **away from zero** (`2.5` becomes `3`, `-2.5` becomes
  `-3`). So to convert a numeric-looking string with a fraction, chain `toDecimal` first.
- From a boolean: `1` / `0`. From a date: throws.
- Final output `type: "integer"` applies the same rules, so an explicit `toInteger` is only needed to round
  *before* later steps (such as `multiply` with `{ "value": true }`) or to attach an `onError`.

**Example**

| Input | Steps | Output |
| --- | --- | --- |
| `"3"` | `toInteger` | `3` |
| `"2.5"` | `toInteger` with `onError: {default: -1}` | `-1` |
| `"2.5"` | `toDecimal`, `toInteger` | `3` |
| `"-2.5"` | `toDecimal`, `toInteger` | `-3` |
| `"3.49"` | `toDecimal`, `toInteger` | `3` |
| `"+7"` / `" 7 "` | `toInteger` | `7` |
| `""` (blank cell) | `toInteger` | *(omitted)* |

Sample fields: `toInteger_qty`, `toInteger_score_rounded`, `toInteger_score_direct`; error handling variants in
`step_errors` (`err_null`, `err_default`, `err_default_text`, `err_chain`).

**See also** [`toInt`](#toint), [`toDecimal`](#todecimal), [ValueTypes: To integer](ValueTypes.md#to-integer)

---

### toInt

Alias of [`toInteger`](#tointeger): registered separately but constructs the identical transform.

**Syntax**

```jsonc
{ "op": "toInt" }
```

Parameters, remarks and behavior are exactly those of [`toInteger`](#tointeger).

Sample field: `toInt_qty` (output equals `toInteger_qty` on every row).

**See also** [`toInteger`](#tointeger)

---

### toDecimal

Converts the running value to an exact decimal; the right choice for money.

**Syntax**

```jsonc
{ "op": "toDecimal", "culture": "de-DE", "onError": "null" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `culture` | string | no | invariant | .NET culture name; governs the decimal and thousands separators when parsing text |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- Strings are parsed with sign, decimal point, exponent and **thousands separators** allowed; currency symbols,
  percent signs and parentheses are not (`$5.00` throws). `1e3` parses as `1000` (and a `decimal`/`double`
  *output field* reading such a raw cell adds a "scientific notation" warning).
- **Culture trap:** with the wrong culture a string can parse to a wrong number instead of failing. Invariant
  `"9,99"` becomes `999` and invariant `"1.234,50"` fails. With `culture: "de-DE"` they are `9.99` and `1234.50`.
- Doubles are converted with 15 significant digits; integers exactly; booleans to `1`/`0`; dates throw.
- Decimals keep their scale when written: `"12,00"` with `de-DE` is written `12.00`.

**Example**

| Input | Config | Output |
| --- | --- | --- |
| `"1.234,50"` | `culture: de-DE` | `1234.50` |
| `"9,99"` | `culture: de-DE` | `9.99` |
| `"9,99"` | invariant (no culture), `onError: "null"` | `999` (silently wrong) |
| `"1.234,50"` | invariant, `onError: "null"` | *(omitted: fails, becomes null)* |
| `"1,234.50"` | invariant | `1234.50` |
| `"$5.00"` | any | throws |

Sample fields: `toDecimal_eu`, `toDecimal_invariant_misparse` (source column `price_eu`).

**See also** [`toDouble`](#todouble), [`numberFormat`](NumberOps.md#numberformat), [ValueTypes: To decimal / double](ValueTypes.md#to-decimal--double)

---

### toDouble

Converts the running value to a double-precision floating-point number.

**Syntax**

```jsonc
{ "op": "toDouble", "culture": "en-US" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `culture` | string | no | invariant | .NET culture name |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- Same parsing rules as [`toDecimal`](#todecimal) (sign, point, exponent, thousands separators allowed).
- Prefer `decimal` for money: doubles cannot represent most decimal fractions exactly (a division such as
  `0.6 / 3` writes `0.19999999999999998`).
- A result of `NaN` or infinity (for example from dividing a double by zero) **cannot be written to JSON and
  stops the entire run** with an `ArgumentException`; guard such divisions.

**Example**

| Input | Output (`type: "double"`) |
| --- | --- |
| `"12.5"` | `12.5` |
| `"0"` | `0` |
| `"abc"` | throws (`The input string 'abc' was not in a correct format.`) |
| `""` | *(omitted)* |

Sample field: `toDouble_discount` (source column `discount`).

**See also** [`toDecimal`](#todecimal)

---

### toBool

Converts the running value to a boolean.

**Syntax**

```jsonc
{ "op": "toBool", "onError": { "default": false } }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

`format` and `culture` are accepted but have no effect.

**Remarks**

- Strings (trimmed): `1` is true, `0` is false, `true`/`false` in any letter case. Everything else throws,
  including `yes`, `y`, `t`, `12`, `maybe`. Use [`map`](ConditionalOps.md#map) for vocabularies like yes/no.
- Numbers: non-zero is true. Booleans unchanged. Dates throw.

**Example**

| Input | Output |
| --- | --- |
| `"1"`, `"true"`, `"TRUE"`, `" True "` | `true` |
| `"0"`, `"False"` | `false` |
| `"yes"`, `"maybe"` | throws; with `onError: {default: false}` gives `false` |
| `""` | *(omitted)* |

Sample fields: `toBool_flag`, `toString_bool`; the `map`-based alternative is `map_flag` in
[ConditionalOps](ConditionalOps.md#map).

**See also** [`map`](ConditionalOps.md#map), [ValueTypes: To bool](ValueTypes.md#to-bool)
