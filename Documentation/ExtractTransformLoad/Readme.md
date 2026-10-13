# Extract / Transform / Load (config-driven)

The **Extract / Transform / Load** command reads one or more delimited files (CSV, TSV, pipe, ...), joins them
according to a JSON config, reshapes every record (typed fields, transform pipelines, nested children, lookups,
keyed maps) and writes **JSONL**: one JSON document per line, split into numbered files. Nothing is hardcoded:
the sources, relationships, field maps, error policy, join strategy and output folder all come from a single
`.etlconfig` file. The command itself asks only a few things: where your config folder is (once), which config to
run and, if the config does not say, where to write.

It is an offline command: it needs no API, no network and no credentials. It is a thin console wrapper around
the `bleak.Data.Transformer` engine, which can also be called from code (`EtlEngine.Run`).

## Contents

| Page | What it covers |
| --- | --- |
| **Readme.md** (this page) | Running the command, prompts, the config folder, output folder, run timestamp, side files |
| [EtlConfig.md](EtlConfig.md) | The whole `.etlconfig` schema at a glance: `version`, `run`, `sources`, `outputs`; JSON-with-comments rules; how the engine validates a config |
| [RunOptions.md](RunOptions.md) | Every `run` setting: `joinStrategy` (`hashInMemory` vs `externalMerge`), `onError`, `emitEmpty`, `sample`, `keyFilter`, `maxRecords`; memory and temp-file behavior |
| [Sources.md](Sources.md) | Delimited sources: path rules, delimiter, quote, header, typed `fields`, gzip and PGP-encrypted files and the decrypted-file cache |
| [Outputs.md](Outputs.md) | Output `mode` (`object`, `keyedMap`), fields, types, `object` groups, lookups, `coalesce`, `keysOf`, null/empty handling, filters |
| [Relationships.md](Relationships.md) | `children`: cardinality, `on` keys (single and composite), nesting, shared `as`, inline relationships, `distinctBy`, `limit`, `filter`, `keyBy`/`keyedSingle`/`keyedSingleBestOf`, `mergeBy`/`mergePrefix`, `requireRelations` |
| [Load.md](Load.md) | `load`: format, folder, `{run_timestamp}`, `fileBaseName`, `splitEvery`, random `sampling` and `richSampling`; exact file names |
| [Errors.md](Errors.md) | Error policies, per-step and per-field overrides, rejects / filtered / warnings files, what is a compile-time failure, every known message |
| [Transforms/Readme.md](Transforms/Readme.md) | The per-operation reference for transform pipelines (`lower`, `concat`, `map`, `toDecimal`, ...) and the filter operators |
| [samples/README.md](samples/README.md) | Runnable inputs, configs and real expected output for every feature |

## Syntax / how to run

Start DataMonger (`dotnet run --project DataMonger`, or `dotnet DataMonger.dll` from a build folder) and pick the
menu entry:

```
  8) Extract / Transform / Load (config-driven)
```

The menu is sorted alphabetically, so the number depends on which commands are installed; choose by name. The
command is **site independent**: it runs once, not once per configured site, and the app exits when it finishes.
(The app does insist on a valid `appsettings.json` and a `LocalData/products.csv` at startup for the sake of its
other commands; see [Configuration](../Configuration.md).)

### Prompts, in order

1. **Config folder** (first run only)

   The first time you run this command, it asks where your `.etlconfig` files live and remembers the answer
   (see [The config folder](#the-config-folder)):

   ```
   First run: choose the folder where your ETL config files (.etlconfig) live.
   Config folder [~/Documents/DataMonger/Config/EtlConfig]:
   ```

   Press Enter to accept the default, or type a folder. The folder is created, filled with the bundled example
   config when it holds no `.etlconfig` yet, and saved in `settings.json`. A complete transcript is in
   [samples/first-run-console.txt](samples/first-run-console.txt). Later runs skip this question unless the saved
   folder no longer exists.

2. **Config selection**

   ```
   Available ETL configs (~/Documents/DataMonger/Config/EtlConfig):
     1) example.etlconfig
   Select a number, enter a path to your own .etlconfig, 'c' to change the config folder, or 'q' to cancel:
   ```

   | Answer | Result |
   | --- | --- |
   | A number `1`..`N` | Runs that config from the config folder. |
   | A path | Runs that file. Surrounding double quotes are removed and a leading `~` expands to your home folder. A relative path is relative to the **current working directory**. |
   | `c` (any case) | Asks for a different config folder (`Choose the folder for your ETL config files (.etlconfig).` / `Config folder [<current>]:`), saves it and lists it. |
   | `q` (any case) | Cancels: `[FAIL] No config file provided or it does not exist. Aborting.` |
   | An empty line | Asks again. |
   | Anything else (bad number, missing file) | Prints `[warn] enter 1-N, a valid .etlconfig path, 'c', or 'q'.` (or `[warn] file not found: <input>` when the folder holds no configs) and asks again. |
   | End of input (stdin closed) | Cancels, so a piped run that runs out of answers does not loop forever. |

   When the folder holds no `.etlconfig` the list is replaced by `No .etlconfig files found in <folder>.` and
   only a path, `c` or `q` is useful.

   A config that cannot be parsed (invalid JSON, an unknown enum value, a wrong value type) stops the run with
   `[FAIL] Could not parse config: <reason>`. A config that parses but is structurally wrong stops with
   `[FAIL] <ExceptionType>: <reason>` once the engine compiles the output ([Errors.md](Errors.md)).

3. **Output folder** (only if at least one output has no `load.folder`)

   ```
   Output folder for the .jsonl files [~/Desktop]:
   ```

   | Answer | Result |
   | --- | --- |
   | Empty line | Uses the default shown: your Desktop folder, or the current directory when the OS reports none. |
   | A path | Used as the **fallback folder** for every output that has no `load.folder` of its own. Quotes are removed; `~` expands; a relative path resolves against the **config file's folder** (not the working directory). |

   If every output sets `load.folder` this prompt never appears and the console shows
   `Output folder : (from config)`.

There is no confirmation step. After the config is chosen the run starts immediately.

To reproduce the samples without touching your real Documents folder, point `DATAMONGER_HOME` at a scratch folder
(see [samples/README.md](samples/README.md#how-to-run-a-sample)).

### Example session

```
Choice: 8

=== Extract / Transform / Load (config-driven) ===

Available ETL configs (~/Documents/DataMonger/Config/EtlConfig):
  1) example.etlconfig
Select a number, enter a path to your own .etlconfig, 'c' to change the config folder, or 'q' to cancel: samples/01-basic-object/basic.etlconfig

Config        : samples/01-basic-object/basic.etlconfig
Sources       : 1
Outputs       : 1
Join strategy : HashInMemory
On error      : Skip
Sample        : (full run)
Max records   : (no cap)
Output folder : (from config)
Run timestamp : 20260101_000000

    output 'customers': 0 lookup(s) (0 exists-mode), 0 keyed relation group(s) compiled.
--- customers (Object, HashInMemory) ---
    output folder: samples/01-basic-object/output/run_20260101_000000

=== Done ===
customers: 6 records across 1 file(s):
    samples/01-basic-object/output/run_20260101_000000/customers_20260101_000000.jsonl
Total elapsed: 00:00:00.000
```

The summary block before the run echoes the settings the engine will use: the number of sources and outputs, the
`joinStrategy` and `onError` (printed as the enum names, `HashInMemory`, `Skip`, ...), whether this is a
[sample run](RunOptions.md#sample), the [`maxRecords`](RunOptions.md#maxrecords) cap, and the output folder.

## Inputs

* One `.etlconfig` file ([EtlConfig.md](EtlConfig.md)).
* The delimited files its `sources` point at ([Sources.md](Sources.md)). Source files are only read, never
  modified. (Gzip or PGP sources are additionally unpacked next to the original; see
  [Sources.md](Sources.md#encrypted-and-compressed-sources).)

## Outputs

For each output in the config, in the order they are declared, the command creates (inside the
[per-run folder](#output-folder-and-run-timestamp)):

| File | When | Contents |
| --- | --- | --- |
| `<fileBaseName>_<timestamp>.jsonl` | Always (when at least one record is written); `_<n>` ordinal appended when the feed is split into several files | One JSON document per line ([Load.md](Load.md#file-names)) |
| `<fileBaseName>_<timestamp>_rejects.csv` | Only when a record was skipped by `onError: skip` | Each skipped source row, preceded by a `# <reason>` line ([Errors.md](Errors.md#the-rejects-file)) |
| `<fileBaseName>_<timestamp>_filtered.csv` | Only when `filterExcludedFile: true` and a record was filtered out | The excluded source rows as CSV lines, without a header ([Outputs.md](Outputs.md#filter)) |
| `<fileBaseName>_<timestamp>_warnings.csv` | Only when a field warning was raised | Tab-separated `record_key`, `field`, `reason`, `raw_value` ([Errors.md](Errors.md#the-warnings-file)) |
| `<sampling.folder>/*.json` (default `sampling/`) | Object-mode outputs when `sampling` is enabled (the default) | A random sample of records, one pretty-printed JSON file each ([Load.md](Load.md#sampling)) |
| `<richSampling.folder>/*.json` (default `sampling/full/`) | Only when `richSampling.enabled` | The richest records ([Load.md](Load.md#richsampling)) |

The closing `=== Done ===` report prints one block per output:

```
<name>: <records> records across <n> file(s)[, <k> skipped][, <k> filtered out][, <k> warnings]:
    <path of every .jsonl file>
```

and a final `Total elapsed: hh:mm:ss.fff`.

## The config folder

The numbered list is the content of your **ETL config folder**, not of a folder bundled with the build. Where it is
and how it is created is described in [Configuration](../Configuration.md#config-folders); in short:

* **Default:** `~/Documents/DataMonger/Config/EtlConfig` (the `DataMonger` home folder is `$DATAMONGER_HOME` when
  that variable is set).
* **First run:** the command asks for the folder (Enter accepts the default), creates it, **copies the bundled
  `example.etlconfig` into it** when it holds no `.etlconfig`, and saves the choice in
  `<home>/settings.json` under `ConfigFolders.Etl`.
* **Afterwards:** the saved folder is used without asking. Type `c` at the config prompt to change it. If the saved
  folder has been deleted, the command asks again (and re-seeds it).
* Only files directly inside the folder with the extension `.etlconfig` are listed (no subfolders), sorted by file
  name ignoring case.
* Anything outside the folder is run by typing its path at the prompt; that does not change the saved folder.

The seeded `example.etlconfig` reads `~/Documents/DataMonger/Data/csvs/customers.csv` and `.../orders.csv` and writes
to `~/Desktop/{run_timestamp}`, so put your input files in that `Data/csvs` folder (or edit your copy of the
example) before running it. It is a template; the runnable samples in this documentation use paths relative to
their own folders instead.

## Output folder and run timestamp

* The **run timestamp** is the local time at the moment the command starts, formatted `yyyyMMdd_HHmmss`, for
  example `20260101_000000`. One timestamp is used for the whole run, so every output of one run carries the same
  one.
* Every output's files go into a **per-run subfolder** `run_<timestamp>`, so successive runs never overwrite or
  mix with each other. By default the subfolder is appended to the output's folder:
  `<folder>/run_<timestamp>/`. A `load.folder` containing the token `{run_timestamp}` instead puts the
  `run_<timestamp>` folder exactly where the token is; see [Load.md](Load.md#folder).
* The folder for an output is its own `load.folder` if it has one, else the folder you typed at the prompt.
  Relative folders are relative to the **config file's folder**, and `~` is the home folder.

## Remarks

* **Sequential.** Outputs run one after another, in declaration order, on a single thread. Within an output the
  primary source is streamed row by row, so memory use is bounded by the child and lookup sources, not by the
  primary (see [RunOptions.md](RunOptions.md#joinstrategy)).
* **Fail-fast configuration.** Every structural problem (unknown alias, missing column in a join, misspelled
  op) is detected when an output is compiled, before that output writes anything. But outputs compile and run
  one at a time, so earlier outputs of a multi-output config may already have written their files when a later one
  fails. Mistakes in `from:` column names of plain output fields are not detected at all; the field is simply
  absent from the output ([Errors.md](Errors.md#things-that-are-not-validated)).
* **Not idempotent by design.** Each run writes to a new timestamped folder; nothing is overwritten or deleted.
  Delete old `run_*` folders yourself.
* **Gzip/PGP side effect.** A source named `*.gz` or `*.pgp` is unpacked once into a sibling
  `<file name>_decrypted/` folder that is kept between runs ([Sources.md](Sources.md#encrypted-and-compressed-sources)).
* **Temp space.** `externalMerge` spills sorted runs to the system temp directory and deletes them afterwards.
* **Logging.** The command writes only to the console; it does not use the app's log file.
* **Return value.** The command reports the total record count as its success count; a configuration or runtime
  failure reports one failure and prints `[FAIL] ...` (with a stack trace for unexpected exceptions).
* **Once per session.** A static guard prevents the command from running twice in one process
  (`[skip] ExtractTransformLoad already ran this session`); in practice the app exits after one command anyway.
* **Aborted runs leave partial output.** When `onError: abort` (or any unhandled exception) stops a run, the files
  written so far are closed and kept; the feed is incomplete ([Errors.md](Errors.md#abort)).

## Examples

All examples live under [`samples/`](samples/README.md) with real expected output.

| Sample | Shows |
| --- | --- |
| [01-basic-object](samples/01-basic-object/basic.etlconfig) | One record per row, typed input/output fields, a few pipelines; [blank vs null handling](samples/01-basic-object/empty-and-null.etlconfig) |
| [02-nested-children](samples/02-nested-children/nested.etlconfig) | Children, cardinality, three-level nesting, shared `as`, object groups, inline relationships |
| [03-keyed-map](samples/03-keyed-map/keyed-map.etlconfig) | `keyedMap` output with dynamic property names and `dedup` |
| [04-lookup](samples/04-lookup/lookup.etlconfig) | Lookups, `exists`, `expr`, `lookups`, `coalesce` |
| [05-keyed-relations](samples/05-keyed-relations/keyed-relations.etlconfig) | `keyBy`, `keyedSingle`, `keyedSingleBestOf`, `keysOf` |
| [06-merge-by](samples/06-merge-by/merge-by.etlconfig) | `mergeBy` / `mergePrefix` |
| [07-external-merge](samples/07-external-merge/external-merge.etlconfig) | `externalMerge` and a composite join key |
| [08-load-sampling](samples/08-load-sampling/load-sampling.etlconfig) | `sampling` and `richSampling` |
| [09-run-limits](samples/09-run-limits/) | `run.sample`, `run.keyFilter`, `run.maxRecords`, `splitEvery` |
| [10-filter-distinct-require](samples/10-filter-distinct-require/filters.etlconfig) | `filter`, `filterExcludedFile`, `distinctBy`, `requireRelations` |
| [11-errors](samples/11-errors/) | `skip` / `nullify` / `abort`, field and step overrides, rejects and warnings files |
| [12-delimited-sources](samples/12-delimited-sources/delimited.etlconfig) | Delimiters, quoting, BOM/CRLF, gzip |
| [13-joins-advanced](samples/13-joins-advanced/joins-advanced.etlconfig) | `caseInsensitiveJoin`, composite keys, `shareDistinct`, lookup reuse |
| [14-load-folders](samples/14-load-folders/load-folders.etlconfig) | `{run_timestamp}`, per-output folders, the fallback-folder prompt |
| [transforms](samples/transforms/README.md) | Every transform op and filter operator |

## Related

* [Transforms](Transforms/Readme.md): pipeline ops, value types, filter operators.
* [Anonymize PII](../AnonymizePii/Readme.md): a sibling config-driven command.
* [Split CSV](../SplitCsv/Readme.md): splitting a file before feeding it to the engine.
* [Architecture](../Architecture.md) and [Configuration](../Configuration.md).
