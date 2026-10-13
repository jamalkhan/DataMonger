# The `.etlconfig` file

An `.etlconfig` file describes one ETL run: a pool of aliased **sources** (the files to read) feeding any number
of **outputs** (the feeds to write), plus a few **run**-wide settings. This page is the schema map. Each area has
its own reference page with the details.

* [Command usage](Readme.md)
* [RunOptions.md](RunOptions.md), [Sources.md](Sources.md), [Outputs.md](Outputs.md),
  [Relationships.md](Relationships.md), [Load.md](Load.md), [Errors.md](Errors.md)
* [Transforms/Readme.md](Transforms/Readme.md) for the `pipeline` steps used by fields

## File format

An `.etlconfig` is **JSON with comments**. The loader (`EtlConfigLoader`) is `System.Text.Json` configured with:

| Rule | Effect |
| --- | --- |
| Comments allowed | `// line` and `/* block */` comments are skipped, anywhere whitespace is allowed |
| Trailing commas allowed | `[ 1, 2, ]` and `{ "a": 1, }` are accepted |
| Property names are case-insensitive | `"fileBaseName"`, `"FileBaseName"` and `"filebasename"` are the same key |
| Enum values are **strings**, camelCase | `"hashInMemory"`, `"externalMerge"`, `"skip"`, `"keyedMap"`, `"dateTime"`. Matching ignores case. A number in place of an enum string is rejected |
| Unknown properties are **ignored silently** | A misspelled key (`"splitEvry"`) does not error; it just has no effect. Check spelling against this documentation |
| Strings must be valid JSON strings | Backslashes in Windows paths must be doubled (`"C:\\data\\x.csv"`) or written with forward slashes |

A syntax error, an unknown enum value or a value of the wrong JSON type stops the command with
`[FAIL] Could not parse config: <reason>` (the reason includes the JSON path, line and byte position).

Everything the loader needs is in the file: there are no includes, variables or environment expansion. The only
substitutions are a leading `~` in file and folder paths and the `{run_timestamp}` token in
[`load.folder`](Load.md#folder).

## Shape at a glance

```jsonc
{
  "version": 1,

  "run": {                                   // RunOptions.md
    "joinStrategy": "hashInMemory",          //   hashInMemory | externalMerge
    "onError": "skip",                       //   skip | abort | nullify
    "emitEmpty": false,
    "sample":    { "primaryRows": 100, "keyColumn": "customer_id" },
    "keyFilter": { "keyColumn": "customer_id", "values": ["1001"] },
    "maxRecords": 100000
  },

  "sources": {                               // Sources.md   (alias -> source)
    "customers": {
      "file": "customers.csv", "format": "delimited", "delimiter": "csv", "quote": "\"", "hasHeader": true,
      "fields": { "customer_id": { "type": "integer" } },
      "decrypt": { "pgpKeyFile": "key.asc", "pgpPassphrase": "..." }
    }
  },

  "outputs": [                               // Outputs.md   (one object per feed)
    {
      "name": "customers",
      "mode": "object",                      //   object | keyedMap
      "source": "customers",
      "fields": [ { "name": "customer_id", "from": "customer_id", "type": "integer" } ],
      "children": [ /* Relationships.md */ ],
      "filter": [ /* conditions */ ], "filterExcludedFile": false,
      "distinctBy": "...", "requireRelations": [ "..." ],
      "load": { "format": "jsonl", "folder": "output", "fileBaseName": "customers", "splitEvery": 1000000 }
    }
  ]
}
```

## Top level

| Property | Type | Required | Default | Notes |
| --- | --- | --- | --- | --- |
| `version` | integer | No | `1` | Read but not interpreted by the engine; there is only one schema version. Keep it `1`. |
| `run` | object | No | all defaults | [RunOptions.md](RunOptions.md) |
| `sources` | object (alias to source) | Yes (in practice) | none | [Sources.md](Sources.md). Aliases are case-insensitive. An output that references an alias that is not here fails with `unknown source alias '<alias>'`. |
| `outputs` | array of output | Yes (in practice) | none | [Outputs.md](Outputs.md). An empty array runs nothing and reports nothing. |

## Property reference by object

The `name` and `from` values in the tables below are the JSON keys. "Required" means the engine fails (or the
property has no useful default) without it.

### `run`

| Property | Type | Default | Page |
| --- | --- | --- | --- |
| `joinStrategy` | `hashInMemory` \| `externalMerge` | `hashInMemory` | [RunOptions.md#joinstrategy](RunOptions.md#joinstrategy) |
| `onError` | `skip` \| `abort` \| `nullify` | `skip` | [RunOptions.md#onerror](RunOptions.md#onerror), [Errors.md](Errors.md) |
| `emitEmpty` | bool | `false` | [RunOptions.md#emitempty](RunOptions.md#emitempty) |
| `sample` | object (`primaryRows`, `keyColumn`) | none | [RunOptions.md#sample](RunOptions.md#sample) |
| `keyFilter` | object (`keyColumn`, `values`) | none | [RunOptions.md#keyfilter](RunOptions.md#keyfilter) |
| `maxRecords` | integer | none (no cap) | [RunOptions.md#maxrecords](RunOptions.md#maxrecords) |

### `sources.<alias>`

| Property | Type | Default | Page |
| --- | --- | --- | --- |
| `file` | string | `""` (must be set) | [Sources.md#file](Sources.md#file) |
| `format` | string | `"delimited"` | [Sources.md#format](Sources.md#format) |
| `delimiter` | string | `","` | [Sources.md#delimiter](Sources.md#delimiter) |
| `quote` | string | `"\""` | [Sources.md#quote](Sources.md#quote) |
| `hasHeader` | bool | `true` | [Sources.md#hasheader](Sources.md#hasheader) (accepted but currently not used) |
| `fields` | object (column to typed field) | `{}` | [Sources.md#fields](Sources.md#fields) |
| `decrypt` | object (`pgpKeyFile`, `pgpPassphrase`) | none | [Sources.md#decrypt](Sources.md#decrypt) |

### `outputs[]`

| Property | Type | Default | Page |
| --- | --- | --- | --- |
| `name` | string | required | [Outputs.md#name](Outputs.md#name) |
| `mode` | `object` \| `keyedMap` | `object` | [Outputs.md#mode](Outputs.md#mode) |
| `source` | string (source alias) | required | [Outputs.md#source](Outputs.md#source) |
| `fields` | array of field | `[]` | [Outputs.md#fields](Outputs.md#fields) |
| `children` | array of child | `[]` | [Relationships.md](Relationships.md) |
| `filter` | array of condition | none | [Outputs.md#filter](Outputs.md#filter) |
| `filterExcludedFile` | bool | `false` | [Outputs.md#filterexcludedfile](Outputs.md#filterexcludedfile) |
| `distinctBy` | string (column) | none | [Outputs.md#distinctby](Outputs.md#distinctby) (object mode) |
| `requireRelations` | array of string | none | [Relationships.md#requirerelations](Relationships.md#requirerelations) (object mode) |
| `groupBy`, `keyExpr`, `dedup` | | none | [Outputs.md#keyedmap-mode](Outputs.md#keyedmap-mode) (keyedMap mode only) |
| `load` | object | defaults | [Load.md](Load.md) |

### `fields[]` (output field, child field, or group field)

| Property | Type | Default | Page |
| --- | --- | --- | --- |
| `name` | string | required | [Outputs.md#field-name](Outputs.md#field-name) |
| `from` | string (column) | none | [Outputs.md#from](Outputs.md#from) |
| `type` | `string` \| `integer` \| `decimal` \| `double` \| `bool` \| `date` \| `dateTime` \| `object` \| `array` | `string` | [Outputs.md#type](Outputs.md#type), [Transforms/ValueTypes.md](Transforms/ValueTypes.md) |
| `pipeline` | array of step | `[]` | [Transforms/Readme.md](Transforms/Readme.md) |
| `fields`, `children` | | none | object groups: [Outputs.md#object-groups](Outputs.md#object-groups) |
| `lookup`, `lookups` | object, array | none | [Outputs.md#lookup-fields](Outputs.md#lookup-fields) |
| `coalesce`, `coalesceEmit` | array, string | none | [Outputs.md#coalesce-fields](Outputs.md#coalesce-fields) |
| `keysOf` | array of string | none | [Outputs.md#keysof](Outputs.md#keysof) |
| `source`, `on`, `caseInsensitiveJoin`, `distinctBy`, `limit`, `shareDistinct`, `filter` | | none | inline relationship: [Relationships.md#inline-relationship-fields](Relationships.md#inline-relationship-fields) |
| `onError` | `"warnAndNull"` | none | [Errors.md#field-level-onerror](Errors.md#field-level-onerror) |
| `emitNull` | bool | `false` | [Outputs.md#emitnull](Outputs.md#emitnull) |

### `children[]` (relationship)

`source`, `as`, `cardinality`, `on`, `caseInsensitiveJoin`, `fields`, `children`, `filter`, `distinctBy`, `limit`,
`shareDistinct`, `inline`, `keyBy`, `keyedSingle`, `keyedSingleBestOf`, `mergeBy`, `mergePrefix`. All of them are
documented in [Relationships.md](Relationships.md).

### `load`

`format`, `folder`, `fileBaseName`, `splitEvery`, `sampling`, `richSampling`. See [Load.md](Load.md).

## How the engine reads a config

For each output, in order, the engine:

1. **Compiles** the output: opens every referenced source (reading its header row), resolves every column name
   used in joins, filters, `groupBy`, `distinctBy`, `keyBy`, `mergeBy`, lookups and coalesce candidates, compiles
   every pipeline, and checks the structural rules (for example that `mergeBy` and `distinctBy` are not both set).
   Any violation stops the run with an `EtlConfigException` before the output writes anything
   ([Errors.md](Errors.md#compile-time-errors)).
2. **Builds lookup indexes** by reading each lookup source once (or reuses an identical one built by an earlier
   field or output in the same run).
3. **Streams** the primary source through the configured [join strategy](RunOptions.md#joinstrategy), renders
   each record to JSON and appends it to the output file.
4. Writes the side files (rejects, filtered, warnings, samples) and moves to the next output.

Column names (in `from`, `on`, filters, ...) are matched against the source's header **ignoring case**.
Source aliases are also case-insensitive. Output field names and relationship `as` names are case-**sensitive**
(they become JSON property names).

## Minimal complete example

```jsonc
{
  "sources": { "customers": { "file": "customers.csv" } },
  "outputs": [
    {
      "name": "customers",
      "source": "customers",
      "fields": [ { "name": "customer_id", "from": "customer_id" } ],
      "load": { "folder": "output", "fileBaseName": "customers" }
    }
  ]
}
```

Everything not shown takes its default: object mode, `hashInMemory`, `onError: skip`, a random-sample folder
(`sampling`, see [Load.md](Load.md#sampling)), one file per million records. Note that fields default to
`type: "string"`, so `customer_id` is written as the string `"1001"`, not the number `1001`; declare
`"type": "integer"` for a JSON number.

## Complete worked configs

| Config | Demonstrates |
| --- | --- |
| [samples/01-basic-object/basic.etlconfig](samples/01-basic-object/basic.etlconfig) | The smallest realistic config |
| [samples/02-nested-children/nested.etlconfig](samples/02-nested-children/nested.etlconfig) | Two outputs, seven sources, three nesting levels |
| [samples/13-joins-advanced/joins-advanced.etlconfig](samples/13-joins-advanced/joins-advanced.etlconfig) | Three outputs sharing sources and lookups |

## Pitfalls

* **Two outputs with the same `load.folder` and `fileBaseName`** (including the default `output`) write to the
  **same file name** inside the run folder. The second silently overwrites the first, although the console report
  still lists the first output's record count. Give each output a distinct `fileBaseName` (or folder).
* **A misspelled property is ignored**, not reported (see the table above).
* **Quoting of numbers in JSON**: `"value": 50` and `"value": "50"` are both accepted in filters; the engine
  reads the raw text of numeric literals.
