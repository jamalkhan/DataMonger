# Collection ops

### split

Splits the running text into an array of trimmed, non-empty strings.

**Syntax**

```jsonc
{ "name": "tags", "from": "tags", "type": "array", "pipeline": [ { "op": "split", "separator": ";" } ] }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `separator` | string | no | `";"` | any non-empty string (multi-character allowed, compared ordinally, e.g. `"; "`, `" "`, `"::"`). A missing or empty value falls back to `";"`. |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- Each piece is trimmed of whitespace and empty pieces are dropped: `"red;;green;"` gives `["red","green"]` and
  `"red;blue; green"` gives `["red","blue","green"]`.
- The result is a list of **strings** (numbers stay text: `"1;2"` gives `["1","2"]`). It is written as a JSON array.
- An input that has no non-empty pieces yields an empty list, which is **omitted** (as is a `null` input / blank cell).
- **The field `type` must be `"array"`.** With `"type": "string"` the engine stringifies the list object and
  writes `System.Collections.Generic.List`1[System.Object]`: no error, wrong output.
- **Make `split` the last step.** The other ops do not understand lists (`upper` after `split` uppercases the
  list's type name). The only place steps run once *per element* is a lookup field's pipeline
  ([Readme](Readme.md#where-pipelines-run)).
- Non-string input is converted to text first (number culture is invariant).

**Example**

| Input | Config | Output |
| --- | --- | --- |
| `red;blue; green` | default separator | `["red","blue","green"]` |
| `red;;green;` | default | `["red","green"]` |
| `blue` | default | `["blue"]` |
| `Outerwear Jackets` | `separator: " "` | `["Outerwear","Jackets"]` |
| `Hats & Caps` | `separator: " "` | `["Hats","&","Caps"]` |
| `` (blank) | default | *(omitted)* |

Sample fields: `split_tags`, `split_category_words` (output group `string`).

**See also** [`concat`](StringOps.md#concat), [ValueTypes: array](ValueTypes.md#field-types)
