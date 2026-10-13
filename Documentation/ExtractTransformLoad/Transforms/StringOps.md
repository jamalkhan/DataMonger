# String ops

[`lower`](#lower), [`upper`](#upper), [`snakeCase`](#snakecase) change the case/shape of text;
[`concat`](#concat) builds a string from several pieces. For splitting text into a list see
[`split`](CollectionOps.md#split); for lookup-table rewriting see [`map`](ConditionalOps.md#map). There is no
`trim`, `replace` or `substring` op.

Sample data: [`items.csv`](../samples/transforms/items.csv), [`transforms.etlconfig`](../samples/transforms/transforms.etlconfig),
output group `string` ([`string_*.jsonl`](../samples/transforms/expected-output/transforms/)).

Common to `lower`, `upper` and `snakeCase`: a non-string running value is first converted to text with the rules
of [To string](ValueTypes.md#to-string) (so `12` becomes `"12"`); `null` returns `null`; the step takes no
parameters other than `onError` (which can only matter for exotic failures, since these ops do not normally throw).

---

### lower

Converts text to lowercase using the **invariant** culture.

**Syntax**

```jsonc
{ "op": "lower" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- Invariant casing: no Turkish-I style culture rules. Whitespace is preserved (there is no trim).
- Typical use: normalize email addresses before they are used as keys.
- A blank cell is `null`, so the field is omitted rather than written as `""`.

**Example**

| Input | Output |
| --- | --- |
| `ADA@Example.com` | `"ada@example.com"` |
| `Grace.Hopper@EXAMPLE.com` | `"grace.hopper@example.com"` |
| `` (blank) | *(omitted)* |

Sample fields: `lower_email`, `lower_blank_middle`.

**See also** [`upper`](#upper), [`snakeCase`](#snakecase)

---

### upper

Converts text to uppercase using the **invariant** culture.

**Syntax**

```jsonc
{ "op": "upper" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks** Same as [`lower`](#lower). Do not use it after [`split`](CollectionOps.md#split): `upper` would receive
the list object and uppercase its .NET type name.

**Example**

| Input | Output |
| --- | --- |
| `Outerwear Jackets` | `"OUTERWEAR JACKETS"` |
| `Hats & Caps` | `"HATS & CAPS"` |

Sample field: `upper_category`.

**See also** [`lower`](#lower)

---

### snakeCase

Converts text to `snake_case`: lowercase letters/digits separated by single underscores.

**Syntax**

```jsonc
{ "op": "snakeCase" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- Every letter or digit (Unicode aware, so accented letters stay) is lowercased; every **run** of other
  characters (spaces, punctuation, symbols) collapses into one underscore; leading and trailing separators are
  dropped.
- It does **not** split camelCase: `HelloWorld` becomes `helloworld`.
- Text with no letters/digits at all becomes `""` (omitted from the output).

**Example**

| Input | Output |
| --- | --- |
| `Outerwear Jackets` | `"outerwear_jackets"` |
| `Hats & Caps` | `"hats_caps"` |
| `HelloWorld  v2.0-beta` | `"helloworld_v2_0_beta"` |
| `Café Ñandú` | `"café_ñandú"` |

Sample field: `snakeCase_category`.

**See also** [`lower`](#lower)

---

### concat

Joins fields, literals and/or the running value into one string.

**Syntax**

```jsonc
{ "op": "concat", "separator": "-", "values": [ { "field": "brand" }, { "field": "customer_key" } ] }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `values` | array of [operands](Readme.md#operands) | **yes** | none | `{field}`, `{literal}`, `{value:true}`. Omitting `values` stops the run (`concat requires 'values'`). An empty array is accepted and yields `""`. |
| `separator` | string | no | `""` (none) | any string, inserted between operands |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- The **running value** is only included when an operand is `{ "value": true }`; otherwise it is ignored, so a
  field can omit `from` entirely and build its value purely from other columns.
- Each operand is converted with [To string](ValueTypes.md#to-string) using the **invariant** culture (there is no
  `culture`/`format` here): decimals keep their scale and use `.`, `date` values become `2020-01-15T00:00:00Z`,
  booleans `True`/`False`. Pre-format with [`numberFormat`](NumberOps.md#numberformat)/[`dateFormat`](DateOps.md#dateformat)
  in an earlier field or a separate step if you need other text.
- A `null` operand (blank cell, missing column) contributes nothing, but its **separator is still written**:
  `name|middle|brand` with a blank middle gives `Alan Turing||K`. There is no "skip blanks" option.
- The result is always a string and never `null`; if everything is blank the result is `""` and the field is
  omitted unless run `emitEmpty` is on.
- Output `type` should be `string`.

**Example**

| Config | Row | Output |
| --- | --- | --- |
| `values: [brand, customer_key]`, `separator: "-"` | brand `A`, key `1001` | `"A-1001"` |
| `from: name`, `values: [{value:true}, {literal:" <"}, {field:email}, {literal:">"}]` | Ada Lovelace | `"Ada Lovelace <ADA@Example.com>"` |
| `values: [name, middle, brand]`, `separator: "\|"` | middle blank | `"Alan Turing\|\|K"` |
| `values: [price, signup]`, `separator: " @ "` | price `19.99`, signup date `2020-01-15` | `"19.99 @ 2020-01-15T00:00:00Z"` |

Sample fields: `concat_label`, `concat_with_value`, `concat_null_operand`, `concat_typed_operands`.

**See also** [Operands](Readme.md#operands), [`split`](CollectionOps.md#split)
