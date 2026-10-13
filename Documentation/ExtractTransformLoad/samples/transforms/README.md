# Transform samples

One small input file and five configs that between them run **every transform op and every filter operator**
at least once, with the real output captured under [`expected-output/`](expected-output/). All data is invented.

Reference pages: [Transforms/Readme.md](../../Transforms/Readme.md) (ops index) and
[Filters](../../Transforms/Filters.md).

## Files

| File | Purpose |
| --- | --- |
| [`items.csv`](items.csv) | 10 rows, 20 columns of invented customer/order data. Columns are deliberately messy (mixed case, European decimals `"1.234,50"`, US dates `12/10/1985`, `;`-separated tags, a month-day with no year, `n/a`, blanks). |
| [`transforms.etlconfig`](transforms.etlconfig) | Six outputs (`convert`, `format`, `math`, `string`, `conditional`, `step_errors`) covering all 21 ops plus input typing, `onError` (step and field) and `emitNull`. |
| [`filters.etlconfig`](filters.etlconfig) | Nineteen outputs, one per filter operator/variant, plus an AND combination. |
| [`errors-skip.etlconfig`](errors-skip.etlconfig), [`errors-nullify.etlconfig`](errors-nullify.etlconfig), [`errors-abort.etlconfig`](errors-abort.etlconfig) | The same failing pipeline under each run-level `onError` policy. |
| [`expected-output/<config>/`](expected-output/) | The `.jsonl` (plus `_warnings.csv`, `_rejects.csv`, `_filtered.csv`) a real run produced. |
| [`expected-output/<config>.console.txt`](expected-output/) | The console transcript of that run: paths made relative to the `ExtractTransformLoad` folder, the elapsed time and run timestamp normalised, the config folder shown as `~/Documents/DataMonger/Config/EtlConfig`. |

File names carry the run timestamp (`convert_20261007_213026.jsonl`); yours will differ, the content will not. The
`_warnings.csv` files have a blank `record_key` column even though `items.csv` has a column called `customer_key`: the
column is only an ordinary input here, and a config without relationships has no join key to report
([Errors](../../Errors.md#how-record_key-is-derived)). The
only run-dependent content is in the `filter_*_relative` outputs (they use `now-100years` / `now+50years`, which give
the same rows until about 2050) and the warning/reject timestamps in file names.

## How to run

Run the app's menu entry **`Extract / Transform / Load (config-driven)`** (it is item 8 in the current menu; the
number can change as commands are added, so read the menu). The command reads its configs from your ETL config
folder (default `~/Documents/DataMonger/Config/EtlConfig`, asked for once on the first run and remembered in
`settings.json`; see [Configuration](../../../Configuration.md#config-folders)), but you run these samples by typing the
path to one of the configs at the prompt *Select a number, enter a path to your own .etlconfig, 'c' to change the
config folder, or 'q' to cancel*. No output-folder prompt appears because each config sets `load.folder` (relative to
the config file).

From the `Documentation/ExtractTransformLoad` folder, with a built app and the `appsettings.json` /
`LocalData/products.csv` the app needs at start-up (see the [command Readme](../../Readme.md)). `DATAMONGER_HOME`
points the settings and config folder at a scratch location instead of `~/Documents/DataMonger`; on a brand-new
home the first run asks for the config folder, so send an empty line to accept the default:

```sh
cd Documentation/ExtractTransformLoad
export DATAMONGER_HOME=./scratch-home
printf '8\n\nsamples/transforms/transforms.etlconfig\n' | dotnet path/to/DataMonger.dll   # first run: empty line = default folder
printf '8\nsamples/transforms/filters.etlconfig\n'    | dotnet path/to/DataMonger.dll   # later runs: no folder question
```

Repeat with `errors-skip.etlconfig`, `errors-nullify.etlconfig`, `errors-abort.etlconfig`.
Each run writes to `output/run_<timestamp>/` next to the config; delete that folder between runs if you want to
diff against `expected-output/`. The ETL command runs only once per app start (a second invocation in the same session prints `[skip]`).

## What to look at

| Config | Look for |
| --- | --- |
| `transforms` | `convert_*.jsonl`: typed input and conversions. `format_*.jsonl`: formatting. `math_*.jsonl`, `string_*.jsonl`, `conditional_*.jsonl`, `step_errors_*.jsonl`. Three `_warnings.csv` files: a no-year date (`09-3`, row 1004) and a scientific-notation cell (`1.5e-05`, row 1006). All 10 records are written in every output. |
| `filters` | One file per operator; record counts in the console (`filter_in: 7 records ..., 3 filtered out`), plus `filter_eq_*_filtered.csv` and `filter_onOrAfter_*_filtered.csv` holding the dropped rows. |
| `errors-skip` | 8 records written, 2 skipped; the `_rejects.csv` holds rows 1001 and 1008 with the reason. |
| `errors-nullify` | All 10 records; `discount_int` simply missing on 1001, 1003, 1006, 1008. |
| `errors-abort` | No output file. The console ends with `[FAIL] EtlRowException: transform 'toInteger' failed: The input string '12.5' was not in a correct format.` and the command reports failure. |

Note for `errors-*`: `1003` and `1006` have a blank `discount`, which is simply omitted (blank cells never fail a
`toInteger`); `1001` (`12.5`) and `1008` (`2.5`) fail.

## Op to sample-field cross-reference

Field names are in `transforms.etlconfig`; look at the same key in the matching `expected-output/transforms/<group>_*.jsonl`.

| Op | Group | Field(s) demonstrating it |
| --- | --- | --- |
| [`toString`](../../Transforms/ConversionOps.md#tostring) | convert | `toString_id`, `toString_price`, `toString_signup`, `toString_bool` |
| [`toInteger`](../../Transforms/ConversionOps.md#tointeger) | convert | `toInteger_qty`, `toInteger_score_rounded`, `toInteger_score_direct` |
| [`toInt`](../../Transforms/ConversionOps.md#toint) | convert | `toInt_qty` |
| [`toDecimal`](../../Transforms/ConversionOps.md#todecimal) | convert | `toDecimal_eu` (culture), `toDecimal_invariant_misparse` (the culture trap) |
| [`toDouble`](../../Transforms/ConversionOps.md#todouble) | convert | `toDouble_discount` |
| [`toBool`](../../Transforms/ConversionOps.md#tobool) | convert | `toBool_flag`, `toString_bool` |
| [`toDate`](../../Transforms/DateOps.md#todate) | convert | `toDate_dob` (format), `toDate_birth_md` (no-year guard + `warnAndNull`) |
| [`toDateTime`](../../Transforms/DateOps.md#todatetime) | convert | `toDateTime_login`, `toDateTime_dob` |
| [`dateFormat`](../../Transforms/DateOps.md#dateformat) | format | `dateFormat_long`, `dateFormat_de`, `dateFormat_compact`, `dateFormat_time`, `dateFormat_string_input` |
| [`numberFormat`](../../Transforms/NumberOps.md#numberformat) | format | `numberFormat_n2`, `_n2_de`, `_currency`, `_pad`, `_percent`, `_string_input` |
| [`add`](../../Transforms/NumberOps.md#add) | math | `add_literal`, `add_field`, `add_null_defaulted` |
| [`subtract`](../../Transforms/NumberOps.md#subtract) | math | `subtract_literal` |
| [`multiply`](../../Transforms/NumberOps.md#multiply) | math | `multiply_field`, `multiply_value_square`, `chain_discounted_price` |
| [`divide`](../../Transforms/NumberOps.md#divide) | math | `divide_literal`, `divide_field_by_zero_nulled`, `divide_double` |
| [`lower`](../../Transforms/StringOps.md#lower) | string | `lower_email`, `lower_blank_middle` |
| [`upper`](../../Transforms/StringOps.md#upper) | string | `upper_category` |
| [`snakeCase`](../../Transforms/StringOps.md#snakecase) | string | `snakeCase_category` |
| [`concat`](../../Transforms/StringOps.md#concat) | string | `concat_label`, `concat_with_value`, `concat_null_operand`, `concat_typed_operands` |
| [`split`](../../Transforms/CollectionOps.md#split) | string | `split_tags`, `split_category_words` |
| [`map`](../../Transforms/ConditionalOps.md#map) | conditional | `map_brand`, `map_tier_rank`, `map_no_default_nulled`, `map_flag` |
| [`literal`](../../Transforms/ConditionalOps.md#literal) | conditional | `literal_text`, `literal_int`, `literal_bool`, `literal_ignores_input`, `literal_null_omitted`, `literal_null_emitted` |
| [`coalesce`](../../Transforms/ConditionalOps.md#coalesce-field-level) (field-level) | conditional | `coalesce_middle_or_name`, `coalesce_source` |
| step `onError` (`null` / `{default}`) | step_errors | `err_null`, `err_default`, `err_default_text`, `err_chain` |
| field `onError: warnAndNull` | convert, step_errors | `toDate_birth_md`, `err_field_warn` (see `*_warnings.csv`) |
| input typing | convert | `typed_price`, `typed_ratio`, `typed_signup`, `typed_seen` (source `fields` in the config) |

## Filter operator to sample output

All in `filters.etlconfig`; each output file is named `filter_<name>_<timestamp>.jsonl` and carries `id` plus the
tested column.

| Operator | Output name | Condition | Kept ids |
| --- | --- | --- | --- |
| [`eq`](../../Transforms/Filters.md#eq) | `filter_eq` | `tier eq gold` | 1001, 1004, 1007, 1010 |
| `eq` (numeric) | `filter_eq_numeric` | `qty eq "3.0"` | 1001 |
| [`ne`](../../Transforms/Filters.md#ne) | `filter_ne` | `tier ne gold` | 1002, 1003, 1005, 1006, 1008, 1009 |
| [`gt`](../../Transforms/Filters.md#gt) | `filter_gt` | `qty gt 5` | 1003, 1007, 1008, 1010 |
| `gt` (text) | `filter_gt_string` | `name gt "Grace"` | 1003, 1004, 1008, 1009 |
| [`ge`](../../Transforms/Filters.md#ge) | `filter_ge` | `qty ge 5` | 1003, 1006, 1007, 1008, 1010 |
| [`lt`](../../Transforms/Filters.md#lt) | `filter_lt` | `qty lt 3` | 1002, 1004, 1005 |
| [`le`](../../Transforms/Filters.md#le) | `filter_le` | `qty le 3` | 1001, 1002, 1004, 1005 |
| [`in`](../../Transforms/Filters.md#in) | `filter_in` | `brand in [A, K]` | 1001, 1002, 1003, 1005, 1007, 1008, 1009 |
| `in` (numbers) | `filter_in_numeric` | `customer_key in [1001, 1003]` | 1001, 1003 |
| [`empty`](../../Transforms/Filters.md#empty) | `filter_empty` | `middle empty` | 1002, 1004, 1008 |
| [`notEmpty`](../../Transforms/Filters.md#notempty) | `filter_notEmpty` | `middle notEmpty` | 1001, 1003, 1005, 1006, 1007, 1009, 1010 |
| [`onOrAfter`](../../Transforms/Filters.md#date-operators) | `filter_onOrAfter` | `last_login >= 2024-01-01` | 1001, 1006, 1008, 1009 |
| `onOrAfter` (relative) | `filter_onOrAfter_relative` | `last_login >= now-100years` | 1001, 1002, 1003, 1006, 1008, 1009, 1010 |
| [`after`](../../Transforms/Filters.md#date-operators) | `filter_after` | `last_login > 2024-02-29` | 1001, 1008, 1009 |
| [`onOrBefore`](../../Transforms/Filters.md#date-operators) | `filter_onOrBefore` | `last_login <= 2022-07-04` | 1003, 1007, 1010 |
| [`before`](../../Transforms/Filters.md#date-operators) | `filter_before` | `last_login < 2022-07-04` | 1007, 1010 |
| `before` (relative) | `filter_before_relative` | `last_login < now+50years` | 1001, 1002, 1003, 1006, 1007, 1009, 1010 |
| AND of two | `filter_and_combined` | `tier eq gold` and `qty ge 5` | 1007, 1010 |

The `last_login` column holds a blank (1005) and `n/a` (1004) on purpose: every date operator drops them.

## See also

[Transforms index](../../Transforms/Readme.md) | [Value types](../../Transforms/ValueTypes.md) | [Command Readme](../../Readme.md)
