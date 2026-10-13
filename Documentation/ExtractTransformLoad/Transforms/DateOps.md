# Date ops

[`toDate`](#todate) and [`toDateTime`](#todatetime) parse text into a date value; [`dateFormat`](#dateformat)
renders a date value as custom text. General rules (how dates are written, the no-year guard, time zones) are in
[ValueTypes](ValueTypes.md#to-date--datetime).

Sample data: [`items.csv`](../samples/transforms/items.csv), [`transforms.etlconfig`](../samples/transforms/transforms.etlconfig),
outputs `convert` and `format` in [expected-output/transforms](../samples/transforms/expected-output/transforms/).

Choosing between the three: if your final field `type` is `date`/`dateTime` and the source text is ISO-8601, you
need **no op at all** (or declare the column `type: "date"` in the source). Use `toDate`/`toDateTime` when the
text is in another layout (`MM/dd/yyyy`, `yyyyMMdd`), and `dateFormat` when you need the *output* as custom text
(`"January 15, 2020"`, `"20200115"`) rather than the standard ISO form.

---

### toDate

Parses the running value into a date. The output field `type` should be `date` to be written as `yyyy-MM-dd`.

**Syntax**

```jsonc
{ "op": "toDate", "format": "MM/dd/yyyy", "culture": "en-US", "onError": "null" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `format` | string | no | none (free-form parse) | a .NET custom or standard date/time format string, matched **exactly** (`MM/dd/yyyy`, `yyyyMMdd`, `d-MMM-yyyy`, `o`) |
| `culture` | string | no | invariant | .NET culture name; affects free-form parsing and month/day names in `format` |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- **Without `format`**: `DateTime.Parse(text, culture, RoundtripKind)`: tolerant, culture-dependent. `05/06/2024`
  is May 6 with the invariant/`en-US` culture and June 5 with `de-DE` (both verified). A bare month-day such as
  `9-3` or `02-29` is **rejected** with `'9-3' has no year (month-day only) - refusing to guess a year.` (The
  framework would otherwise silently use the current year.)
- **With `format`**: `DateTime.ParseExact`; any deviation throws (`String '...' was not recognized as a valid
  DateTime`). The no-year guard does not apply here: `format: "M-d"` parses `9-3` and fills in the *current* year,
  so the result changes every January.
- `toDate` does **not** truncate the time-of-day inside the pipeline; only the final `date` output type hides it
  (`toDate` then `dateFormat` with `HH:mm` still sees `14:30`).
- A value that is already a date (typed source column) passes through unchanged.
- Numbers are parsed via their invariant text, so `toInteger`/typed `20240101` plus `format: "yyyyMMdd"` works.
- Time zones: `...Z` text and zone-less text are kept as written. Text with a numeric UTC offset
  (`+02:00`) is converted to the **machine's local time** and then labelled `Z`; the same input produced
  `12:30Z` with `TZ=UTC` and `21:30Z` with `TZ=Asia/Tokyo`. Avoid offsets in input or strip them first.
- Pair with field-level `"onError": "warnAndNull"` to keep the record when a few dates are malformed (see
  [Readme](Readme.md#error-behavior)).

**Example**

| Input | Config | Output (`type: "date"`) |
| --- | --- | --- |
| `12/10/1985` | `format: "MM/dd/yyyy"` | `"1985-12-10"` |
| `1985-12-10` | none | `"1985-12-10"` |
| `09-3` | none | throws (no year); with field `onError: "warnAndNull"` the field is omitted and a warning row is written |
| `""` | any | *(omitted)* |

Sample fields: `toDate_dob` (format), `toDate_birth_md` (no-year guard plus `warnAndNull`; see
`convert_*_warnings.csv`), `err_field_warn` in `step_errors`.

**See also** [`toDateTime`](#todatetime), [`dateFormat`](#dateformat), [ValueTypes](ValueTypes.md#to-date--datetime)

---

### toDateTime

Parses the running value into a date-time. With output `type: "dateTime"` it is written as
`yyyy-MM-ddTHH:mm:ssZ`.

**Syntax**

```jsonc
{ "op": "toDateTime", "format": "yyyy-MM-dd HH:mm", "culture": "en-US", "onError": "null" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `format` | string | no | none (free-form parse) | exact .NET format string |
| `culture` | string | no | invariant | .NET culture name |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- Identical to [`toDate`](#todate): the two ops share one converter. They differ only in the field `type` you
  choose for the output (`date` drops the time, `dateTime` keeps it).
- A date-only text (`2024-05-01`) becomes midnight: `"2024-05-01T00:00:00Z"`. Fractional seconds are dropped on
  output. The trailing `Z` is cosmetic: no zone conversion is done.
- Text such as `2024-03-05 14:30:00` (space instead of `T`) parses fine free-form.

**Example**

| Input | Config | Output (`type: "dateTime"`) |
| --- | --- | --- |
| `2024-05-01` | none | `"2024-05-01T00:00:00Z"` |
| `12/10/1985` | `format: "MM/dd/yyyy"` | `"1985-12-10T00:00:00Z"` |
| `n/a` | `onError: "null"` | *(omitted)* |
| `2024-03-05 14:30:00` | none | `"2024-03-05T14:30:00Z"` |

Sample fields: `toDateTime_login` (column `last_login`, contains a bad value), `toDateTime_dob`.

**See also** [`toDate`](#todate), [`dateFormat`](#dateformat)

---

### dateFormat

Formats a date/datetime as text with a .NET format string.

**Syntax**

```jsonc
{ "op": "dateFormat", "format": "MMMM d, yyyy", "culture": "en-US" }
```

| Parameter | Type | Required | Default | Allowed values |
| --- | --- | --- | --- | --- |
| `format` | string | **yes** | none | any .NET standard or custom date/time format string. A missing/empty `format` stops the run (`dateFormat requires 'format'.`). |
| `culture` | string | no | invariant | .NET culture name; selects month/day names and separators |
| `onError` | `"throw"` / `"null"` / `{default}` | no | `"throw"` | |

**Remarks**

- The input is first converted to a date with the rules of [`toDateTime`](#todatetime) *without* a parse
  format and with the **invariant** culture; `culture` here affects only the *output*. For input such as
  `05/06/2024` use an explicit [`toDate`](#todate) with `format` first.
- Output type should be `string`. (A `date`/`dateTime` field type would re-parse your text.)
- Month and day names come from the culture's data (ICU/NLS on the host); `de-DE` gives
  `Mittwoch, 15. Januar 2020` on a normal install but may fall back to invariant names on hosts that run
  with invariant globalization.
- `null` input gives `null`. Non-date text throws (`The string 'abc' was not recognized as a valid DateTime`).
- An invalid format string throws a `FormatException` per row.

**Example**

| Input | Config | Output |
| --- | --- | --- |
| date `2020-01-15` | `MMMM d, yyyy`, `en-US` | `"January 15, 2020"` |
| date `2020-01-15` | `dddd, d. MMMM yyyy`, `de-DE` | `"Mittwoch, 15. Januar 2020"` |
| date `2020-01-15` | `yyyyMMdd` | `"20200115"` |
| dateTime `2024-03-05T14:30:00Z` | `HH:mm` | `"14:30"` |
| text `2024-05-01` | `MMM yyyy`, `en-US` | `"May 2024"` |
| text `n/a` | `onError: "null"` | *(omitted)* |

Sample fields: `dateFormat_long`, `dateFormat_de`, `dateFormat_compact`, `dateFormat_time`,
`dateFormat_string_input` in output group `format`.

**See also** [`numberFormat`](NumberOps.md#numberformat), [`toDate`](#todate), [`toString`](ConversionOps.md#tostring)
