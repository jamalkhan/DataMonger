# Filters

A **filter** keeps only the rows (records) that satisfy every condition in a list. It is evaluated on the **raw
CSV text** of a source row, before any transform pipeline runs, so it is the right tool for "only emit customers
who ..." and the wrong tool for conditions on transformed values.

```jsonc
"filter": [
  { "column": "tier",       "op": "eq",  "value": "gold" },
  { "column": "qty",        "op": "ge",  "value": 5 },
  { "column": "last_login", "op": "onOrAfter", "value": "now-2years" }
]
```

Sample: [`filters.etlconfig`](../samples/transforms/filters.etlconfig) (one output per operator) with real
results in [expected-output/filters](../samples/transforms/expected-output/filters/). Input is
[`items.csv`](../samples/transforms/items.csv).

## Where filters apply

| Location | Effect |
| --- | --- |
| `outputs[].filter` | A **primary** row that fails is dropped from the feed. With `"filterExcludedFile": true` the dropped rows are written verbatim (no header) to `<fileBaseName>_<timestamp>_filtered.csv`. The console summary reports `N filtered out`. In `keyedMap` outputs the filter applies to each row of a group; a group with no surviving rows is skipped. |
| `children[].filter` | Only matching **child** rows are joined/nested. |
| inline relationship field `filter` | Only matching rows become array elements. |
| `lookup.filter` | Only matching lookup-source rows are indexed (for `"mode": "exists"`, only they count as existing). |

All four use the same condition shape and operator set. (The statistics in the console and `filtered.csv` exist only
for the output-level filter.)

## Condition properties (`FilterConditionConfig`)

| Property | Type | Required | Default | Meaning |
| --- | --- | --- | --- | --- |
| `column` | string | yes | none | Column of the source being filtered. Case-insensitive. A column that does not exist stops the run: `filter column 'x' not found in the primary source.` |
| `op` | string | yes | none | Operator (table below). Trimmed and matched case-insensitively (`EQ`, `onOrAfter`, ` in ` all work). Unknown: `unknown filter op 'like'.` |
| `value` | JSON | per operator | none | Comparison value; omitted for `empty`/`notEmpty`. A string or a number for most operators (a number is used as its raw JSON text); an array for `in`; an ISO date or relative token for the date operators. |

Conditions are combined with **AND**; there is no OR and no NOT (use `ne` / `notEmpty`, or `in` for "any of").
An empty or absent `filter` keeps every row.

## Operators at a glance

| Operator | Meaning | `value` | Sample output |
| --- | --- | --- | --- |
| [`eq`](#eq) | equal | string/number | `filter_eq`, `filter_eq_numeric` |
| [`ne`](#ne) | not equal | string/number | `filter_ne` |
| [`gt`](#gt) | greater than | string/number | `filter_gt`, `filter_gt_string` |
| [`ge`](#ge) | greater than or equal | string/number | `filter_ge` |
| [`lt`](#lt) | less than | string/number | `filter_lt` |
| [`le`](#le) | less than or equal | string/number | `filter_le` |
| [`in`](#in) | equals any listed value | array | `filter_in`, `filter_in_numeric` |
| [`empty`](#empty) | cell is empty | none | `filter_empty` |
| [`notEmpty`](#notempty) | cell is not empty | none | `filter_notEmpty` |
| [`onOrAfter`](#date-operators) | date `>=` value | date/relative | `filter_onOrAfter`, `filter_onOrAfter_relative` |
| [`after`](#date-operators) | date `>` value | date/relative | `filter_after` |
| [`onOrBefore`](#date-operators) | date `<=` value | date/relative | `filter_onOrBefore` |
| [`before`](#date-operators) | date `<` value | date/relative | `filter_before`, `filter_before_relative` |

AND-combination: `filter_and_combined` (`tier eq gold` and `qty ge 5`).

## Comparison rules for eq, ne, gt, ge, lt, le

The raw cell text and the `value` are compared like this:

1. If **both** parse as numbers (`decimal.TryParse`, invariant culture, `NumberStyles.Any`: sign, decimal point,
   thousands separators, exponent, surrounding whitespace) they are compared **numerically**: `"3"` equals `3.0`;
   `"10"` is greater than `9`.
2. Otherwise they are compared as **text**, ordinally (code-unit order, case-sensitive, culture-free): uppercase
   sorts before lowercase, so `"Ada" < "alan"`, and a shorter prefix sorts first.

Consequences worth knowing:

- Text comparison is case-**sensitive**: `eq` `"Gold"` does not match `gold`.
- An **empty** cell is not a number, so it is compared as text: `""` sorts before every non-empty string. Hence
  `lt 3` and `le 3` **include blank cells** (sample: `discount lt 3` keeps rows with a blank `discount`) and `ne`
  includes them too. Combine with `notEmpty` to exclude blanks.
- Currency text only parses with the matching culture; with the invariant culture `"$10"` is *not* a number, so
  `price gt "$10"` degrades to a text comparison (and matches everything starting with a digit). Use plain `10`.
- Leading zeros: numeric comparison ignores them (`"007"` equals `7`) but only when both sides parse.
- Values come from the CSV text: the source's `fields` typing and any pipeline are not involved.

### eq

Equal.

```jsonc
{ "column": "tier", "op": "eq", "value": "gold" }
{ "column": "qty",  "op": "eq", "value": "3.0" }     // numeric: matches qty 3
```

| Cell | Value | Matches |
| --- | --- | --- |
| `gold` | `"gold"` | yes |
| `Gold` | `"gold"` | no (case-sensitive) |
| `3` | `"3.0"` | yes (numeric) |

Sample: `filter_eq` keeps 1001, 1004, 1007, 1010; `filter_eq_numeric` keeps 1001 (qty 3).

### ne

Not equal. Includes blank cells (a blank is not equal to anything non-empty).

```jsonc
{ "column": "tier", "op": "ne", "value": "gold" }
```

Sample: `filter_ne` keeps 1002, 1003, 1005, 1006, 1008, 1009.

### gt

Greater than (numeric when both sides are numbers, otherwise ordinal text).

```jsonc
{ "column": "qty", "op": "gt", "value": 5 }
```

Sample: `filter_gt` (numeric `qty > 5`) keeps 1003 (10), 1007 (7), 1008 (12), 1010 (6). Note `10 > 5` numerically, which
a text comparison would get wrong. `filter_gt_string` (`name gt "Grace"`, text order) keeps 1003, 1004, 1008, 1009.

### ge

Greater than or equal.

Sample: `filter_ge` (`qty >= 5`) keeps 1003, 1006, 1007, 1008, 1010.

### lt

Less than. Beware blanks (see rules).

Sample: `filter_lt` (`qty < 3`) keeps 1002, 1004, 1005.

### le

Less than or equal.

Sample: `filter_le` (`qty <= 3`) keeps 1001, 1002, 1004, 1005.

### in

True when the raw cell **exactly equals** any element of the `value` array.

```jsonc
{ "column": "brand",        "op": "in", "value": ["A", "K"] }
{ "column": "customer_key", "op": "in", "value": [1001, 1003] }
```

| Remarks | |
| --- | --- |
| Comparison | Exact, ordinal, case-sensitive text; **no numeric normalization**: cell `1001` is not in `["01001"]`. |
| Elements | Strings are used as-is; numbers/booleans use their raw JSON text. |
| Empty cells | An empty cell matches only if `""` is in the list. An empty array matches nothing. |
| Errors | `value` that is not an array stops the run: `filter 'in' on 'tier' requires an array value.` |

Sample: `filter_in` (`brand in [A,K]`) keeps 1001, 1002, 1003, 1005, 1007, 1008, 1009; `filter_in_numeric` keeps 1001, 1003.

### empty

True when the cell is the empty string (or the row is shorter than the header). Takes no `value`. A cell with only
spaces is **not** empty.

```jsonc
{ "column": "middle", "op": "empty" }
```

Sample: `filter_empty` keeps 1002, 1004, 1008.

### notEmpty

True when the cell is not empty. Takes no `value`.

Sample: `filter_notEmpty` keeps 1001, 1003, 1005, 1006, 1007, 1009, 1010.

### Date operators

`onOrAfter`, `after`, `onOrBefore`, `before` compare a date column with a cutoff.

```jsonc
{ "column": "last_login", "op": "onOrAfter", "value": "2024-01-01" }
{ "column": "last_login", "op": "onOrAfter", "value": "now-2years" }
```

| Property | Detail |
| --- | --- |
| `value` | **Required** string. Either an ISO date/date-time (`2024-01-01`, `2024-01-01T08:00:00Z`) or a relative token `now`, `now-2years`, `now+50years`, `now-90days`. Grammar: `now`, then optionally a signed whole number immediately after the sign and one unit `year`, `month`, `week`, `day`, `hour`, `minute`, plural `s` optional; case-insensitive; blanks before the sign/after the number are tolerated but **not between the sign and the digits** (`now -1 years` works, `now - 1 years` is rejected). |
| Resolution | Relative tokens are resolved **once, when the output starts**, against the current UTC time. Each run therefore has a moving window; the sample uses `now-100years` / `now+50years` so its results are stable for decades. |
| Cell parsing | `DateTime.TryParse` with the invariant culture. A cell that is **empty or not a date is excluded**, for every one of the four operators (including `before`/`onOrBefore`): "cannot confirm" means "drop". In the sample `last_login` contains a blank and `n/a`, and both are dropped by all date filters. |
| Time zone | Both sides are converted to UTC for the comparison; a zone-less cell is treated as local time of the machine. For date-only data this only matters at the exact cutoff day. |
| Errors | Invalid cutoff stops the run: `filter value 'yesterday' is not a valid date or relative expression (e.g. "now-2years").` |

Sample outputs (`last_login` values: 2024-05-01, 2023-12-15, 2022-07-04, `n/a`, blank, 2024-02-29, 1900-01-01, 2100-01-01, 2024-04-04, 2020-01-01):

| Output | Condition | Kept ids |
| --- | --- | --- |
| `filter_onOrAfter` | `>= 2024-01-01` | 1001, 1006, 1008, 1009 |
| `filter_after` | `> 2024-02-29` | 1001, 1008, 1009 (1006 is exactly that date and drops out) |
| `filter_onOrBefore` | `<= 2022-07-04` | 1003, 1007, 1010 |
| `filter_before` | `< 2022-07-04` | 1007, 1010 |
| `filter_onOrAfter_relative` | `>= now-100years` | every parseable date except 1900-01-01 (1007) |
| `filter_before_relative` | `< now+50years` | every parseable date except 2100-01-01 (1008) |

The `filter_onOrAfter` and `filter_eq` outputs also set `filterExcludedFile`, producing
`*_filtered.csv` files with the dropped rows.

## See also

[Readme](Readme.md) | [ValueTypes](ValueTypes.md) | [Samples](../samples/transforms/README.md)
