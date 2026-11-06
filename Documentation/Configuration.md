# Configuration

## `appsettings.json`

The app reads `appsettings.json` from its output folder at startup. It is **required**: startup fails if the file
is missing or the `Sites` array is empty.

Configuration is loaded by the .NET Generic Host (`Host.CreateApplicationBuilder`), so the standard sources apply, each
overriding the one before it:

1. `appsettings.json` in the output folder;
2. `appsettings.{Environment}.json` in the output folder, when present (the environment comes from `DOTNET_ENVIRONMENT`
   and defaults to `Production`);
3. environment variables, with `__` as the section separator (for example `Logging__LogLevel__bleak=Debug`);
4. command-line arguments (for example `dotnet DataMonger.dll --Logging:LogLevel:DataMonger=Debug`).

The host is used only for configuration, logging and dependency injection; it is never started, so it prints nothing.

```json
{
  "Sites": [
    {
      "BaseUrl": "http://localhost:5280",
      "SiteId": "demo",
      "Username": "demo",
      "Password": "REPLACE_WITH_PASSWORD"
    }
  ]
}
```

| Property | Type | Required | Description |
|---|---|---|---|
| `Sites` | array | Yes | One entry per target site. API commands run once per entry (or ask you to pick one). Must contain at least one entry, even for offline commands. |
| `Sites[].BaseUrl` | string | Yes | Base URL of the API, without a trailing slash (a trailing slash is trimmed). |
| `Sites[].SiteId` | string | Yes | Substituted into request paths: `{BaseUrl}/ver2/{SiteId}/…`. |
| `Sites[].Username` | string | Yes | User name for the site (sent with each request; the REST client library handles the auth scheme). |
| `Sites[].Password` | string | Yes | Password for the site. |

### Which file is used

| File | Committed? | Purpose |
|---|---|---|
| `DataMonger/appsettings.example.json` | Yes | Placeholder values. |
| `DataMonger/appsettings.json` | No | Optional local copy. |
| `DataMonger/appsettings.user.json` | No | Real credentials. Copied over the build output's `appsettings.json` after every build. |

If neither `appsettings.json` nor `appsettings.user.json` exists, the build copies the placeholder example, so
the app starts but any API call fails against the fake site.

## Config folders

The config-driven commands read user-editable config files: `.etlconfig` ([Extract / Transform / Load](ExtractTransformLoad/Readme.md)),
`.anonconfig` ([Anonymize PII](AnonymizePii/Readme.md)) and `.synthconfig` ([Synthesize User Props](SynthesizeUserProps/Readme.md)).
They no longer list configs from the folders bundled with the build (`data/etlconfig` and friends). Instead each kind
of config has a **config folder** that you choose once and that the app remembers.

### Default locations

| Kind | Extension | Default folder | Used by |
|---|---|---|---|
| ETL | `.etlconfig` | `~/Documents/DataMonger/EtlConfig` | Extract / Transform / Load (config-driven) |
| Anonymization | `.anonconfig` | `~/Documents/DataMonger/AnonConfig` | Anonymize PII (config-driven) |
| Synthetic data | `.synthconfig` | `~/Documents/DataMonger/SynthConfig` | Synthesize User Props (fake data) |
| Anonymous purchases | `.purchaseanonymousconfig` | `~/Documents/DataMonger/PurchaseAnonymousConfig` | Anonymous purchases |
| Known purchases | `.purchaseknownconfig` | `~/Documents/DataMonger/PurchaseKnownConfig` | Known (identified) purchases |
| Email conversions | `.emailconversionconfig` | `~/Documents/DataMonger/EmailConversionConfig` | Generate Email Conversion data |

`~/Documents/DataMonger` is the **DataMonger home folder**. It is `$DATAMONGER_HOME` when that environment variable is
set (a leading `~` is expanded), otherwise the `DataMonger` folder inside your operating system's Documents folder.
The three kinds are remembered independently.

### First run

The first time you run a command of a given kind, it asks where that kind's configs live. Press Enter to accept the
default, or type a folder (surrounding double quotes are removed, `~` expands to your user profile):

```text
First run: choose the folder where your ETL config files (.etlconfig) live.
Config folder [~/Documents/DataMonger/EtlConfig]:
  Copied 1 example .etlconfig file(s) into ~/Documents/DataMonger/EtlConfig
  Saved. Using ~/Documents/DataMonger/EtlConfig (settings: ~/Documents/DataMonger/settings.json)
```

The app then:

1. creates the folder (and any missing parents);
2. **seeds** it: when the folder holds no file with that kind's extension, the example configs bundled with the build
   (`data/etlconfig`, `data/anonconfig`, `data/synthconfig`, looked up in the working directory first and then next
   to `DataMonger.dll`) are copied in. Existing files are never overwritten, and a folder that already contains at
   least one config of the kind is left alone. No synthetic-data examples are bundled in this build, so a new
   `SynthConfig` folder starts empty;
3. saves the choice in `settings.json` (below).

From then on the saved folder is used silently, and the config prompt lists its contents:

```text
Available ETL configs (~/Documents/DataMonger/EtlConfig):
  1) example.etlconfig
Select a number, enter a path to your own .etlconfig, 'c' to change the config folder, or 'q' to cancel:
```

| Answer | Result |
|---|---|
| A number | Runs that config from the config folder (sorted by name, ignoring case; subfolders are not searched). |
| A path | Runs that file, wherever it is (`~/` expands). The config folder is not changed. |
| `c` | Asks for a different config folder (same `Config folder [<current>]:` prompt, default = the current folder), creates and seeds it if needed, saves it, and lists it. |
| `q` | Cancels (the command prints `[FAIL] No config file provided or it does not exist. Aborting.` and does nothing else). |

If the folder holds no config of the kind, the list is replaced by `No <ext> files found in <folder>.`; you can still
type a path or press `c`.

If the saved folder has since been deleted, the command asks again (this time without the "First run:" line, with the
saved path as the default); accepting recreates it and re-seeds it with the bundled examples.

### `settings.json`

The choices live in `<home>/settings.json` (`~/Documents/DataMonger/settings.json` by default):

```json
{
  "ConfigFolders": {
    "Etl": "/home/you/Documents/DataMonger/EtlConfig",
    "Anon": "/home/you/Documents/DataMonger/AnonConfig",
    "Synth": "/home/you/Documents/DataMonger/SynthConfig"
  }
}
```

| Key | Meaning |
|---|---|
| `ConfigFolders.Etl` / `.Anon` / `.Synth` | The saved config folder for that kind, stored as a **full path** (a typed `~` is expanded before saving). A kind appears only after its first run. |

You can edit the file by hand (valid JSON, exactly this shape) or delete a key to be asked again. A missing,
unreadable or malformed file is treated as "nothing saved yet": the app asks again and writes a fresh file.

### Relocating everything: `DATAMONGER_HOME`

Set `DATAMONGER_HOME` before starting the app to use a different home folder, for example to keep a separate set of
configs per project or to run the documentation samples without touching your real Documents folder:

```sh
export DATAMONGER_HOME=./scratch-home
dotnet DataMonger.dll
```

The home folder determines both the default config folders and the location of `settings.json`. It does **not**
relocate the data folder below, and a `~` inside a config file always means your user profile, not
`DATAMONGER_HOME`.

### Relation to the data folder

The bundled example configs (`example.etlconfig`, `example.anonconfig`) read their inputs from
`~/Documents/DataMonger/Data/csvs/...` (for example `~/Documents/DataMonger/Data/csvs/customers.csv`) and the anonymizer
example writes to `~/Documents/DataMonger/Data/csvs_anonymized`. `Config/` (your configs) and `Data/` (your input and
output files) are siblings under the same home folder by convention only: nothing forces your own configs to
use the `Data` folder, and nothing checks that it exists. Create it and put the files the example expects
there, or edit the seeded copy of the example in your config folder to point elsewhere (paths in a config may be
absolute, `~/...` or relative to the config file itself).

### Changing the folder later

* From the command: type `c` at the config prompt.
* By hand: edit or delete the key in `settings.json`.
* Per run, without changing the saved folder: type the full path of any config at the prompt.

## `LocalData/`

| Path | Used by |
|---|---|
| `LocalData/products.csv` | Read at **startup** by every command (the app fails to start without it). |
| `LocalData/identified.csv`, `anonymous.csv` | [PurchaseKnown](PurchaseKnown/Readme.md), [PurchaseAnonymous](PurchaseAnonymous/Readme.md), [GenerateEmailConversions](GenerateEmailConversions/Readme.md) |
| `LocalData/jsonl/…` | Default input folders of the upload and synthesize commands (see each command page). |
| `LocalData/logs/datamonger_<yyyyMMdd_HHmmss>.jsonl` | Application log, one file per run (see [Logging](#logging)). |

## Logging

Each run writes one log file, `LocalData/logs/datamonger_<yyyyMMdd_HHmmss>.jsonl` (local start time; if that name is
already taken, the run id is appended). The file is **JSON lines**: one JSON object per line, for example:

```json
{"Timestamp":"2026-10-19T14:02:11.5170000Z","Level":"Information","Category":"DataMonger.Commands.SplitCsv","EventId":1702,"EventName":"SplitCsvCompleted","Message":"Split CSV completed: ...","FileName":"input.csv","RowCount":250000,"FileCount":3,"ByteCount":41000000,"BatchSize":100000,"DurationMs":812.4,"RecordsPerSecond":307730.9,"Command":"Split CSV into N-row files","SiteId":"(local)","RunId":"3f9c1a2b"}
```

| Field | Meaning |
|---|---|
| `Timestamp`, `Level`, `Category`, `EventId`, `EventName`, `Message` | Always present (`EventName` when the event has one). `Timestamp` is ISO-8601 UTC. |
| Structured properties | The event's own values, e.g. `DurationMs`, `RecordCount`, `FileName`, `StatusCode`. |
| `RunId` | Short id of this run; on every line. |
| `Command`, `SiteId` | On every line written while a command runs. `SiteId` is `(local)` for file-only commands and the picked site for commands with their own site picker. |
| `Exception` | Full exception text, on Error lines that have one. |

Lines are written by a background writer, so logging does not slow the commands down; the file is flushed whenever
the queue runs empty and completely at exit.

### Levels

Levels come from the optional `Logging` section of the configuration (the standard .NET format, normally in
`appsettings.json`; any source above can set or override it). Without the section everything at `Information` and
above is written. The log file is the only log output: the host's default console, debug and event-source loggers are
removed, so logging never writes to the console.

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "bleak": "Information",
    "DataMonger": "Information"
  }
}
```

| Key | Covers |
|---|---|
| `Default` | Everything not matched by a more specific key. |
| `DataMonger` | The app: `DataMonger.Commands.*` (one category per command), `DataMonger.Synth.*`, `DataMonger.Common.*` (config folders, CSV/config loaders), `DataMonger.Program`. A longer prefix such as `DataMonger.Commands.UploadJsonlEvents` targets one command. |
| `bleak` | The bleak libraries the commands call, which log through the same logging setup (they get their loggers from the app's container): `bleak.Data.Transformer.*` (ETL engine, compiler, joins, source readers, `JsonlSplitWriter`), `bleak.Data.Anonymizer.Engine.AnonymizeEngine`, `bleak.Data.InfiniteDataLoader.*` (`CsvSplitter`, `CsvReader`, `ExternalSorter`) and `bleak.Api.Rest.RestClient` (one Debug line per HTTP call: method, URL path, status, duration). Set e.g. `"bleak.Data.Transformer": "Debug"` for per-phase ETL timings, which also turns on the per-step transform costs from `bleak.Data.Transformer.Engine.RecordWriter` (invocations, errors and time per transform/filter op, per output). Set `"bleak.Data.Transformer.Engine.RecordWriter": "Trace"` to split those costs by field. Measuring step costs adds roughly 20% to the transform phase, so turn it on only for profiling runs. |

| Level | What you get |
|---|---|
| `Information` | Run started (app version, OS, processor count, .NET version) and run finished (total `DurationMs`); each command's start (config/job name, input file names) and completion (totals, `DurationMs`, `RecordsPerSecond`). |
| `Debug` | Per file and per phase: load/parse/generate/write timings, per-file upload results, upload progress every 1,000 lines, HTTP fan-out stats (requests, peak in-flight parallelism, successes, failures, 429 throttles, average/max latency), config folder decisions. |
| `Trace` | Per chunk: progress ticks of the bulk/email-event uploaders, generation chunks of Synthesize User Props, plus whatever the bleak libraries log at Trace. |
| `Warning` / `Error` | Aborts, skipped/malformed lines (as counts), throttling and failed requests (as counts), exceptions. |

To investigate a slow run, set `"DataMonger": "Debug"` and `"bleak": "Debug"`. Changing only `Default` is not
enough while those two keys are present, because the more specific key wins. Use `Trace` only for short runs, as it
is much more verbose (and the bleak libraries may include record values at Trace). `appsettings.json` is read from the build output folder, so edit `appsettings.user.json`
(copied over it on every build) or the output copy, or override a level for one run with an environment variable such
as `Logging__LogLevel__bleak=Debug`.

The app never logs record contents: no field values, e-mails or names at any level, only counts, sizes, durations,
file names, column names, HTTP method, status code and URL path. Credentials are never logged.

## Concurrency

`MaxParallelism` is fixed at 50 in `Program.cs`; it is the ceiling on concurrent requests for commands that
use it. It is not currently configurable.

## ETL run options (`Etl`)

The optional `Etl` section tunes *Extract / Transform / Load* for the machine it runs on. It is bound to
`bleak.Data.Transformer`'s `EtlRunOptions` and injected into the command as `IOptions<EtlRunOptions>`; the engine
receives it as a plain parameter. Every property is optional:

```json
"Etl": {
  "MaxDegreeOfParallelism": 0,
  "SortMemoryBudgetBytes": 4294967296,
  "TempDirectory": "/Volumes/Scratch/etl-tmp"
}
```

| Property | Default | Description |
|---|---|---|
| `MaxDegreeOfParallelism` | `0` (all cores) | Threads for preparing sorts, indexes and lookups and for rendering records. `1` runs everything on one thread. |
| `SortMemoryBudgetBytes` | 4 GiB | Memory the external sorts running at once may hold in their chunks, split evenly between them. In-memory indexes and lookups are not counted. |
| `SortChunkSize` | 200000 | Maximum records per sort chunk. |
| `TempDirectory` | system temp folder | Where external sorts spill. Point it at a disk with room for the largest sorted inputs. |
| `ShareScans` | `true` | Read and sort each file once per key, and produce outputs that share a primary file and join tree in one pass. |
| `PruneColumns` | `true` | Blank columns a relation's config never names before sorting and indexing. |
| `PushDownFilters` | `true` | Apply `many`-relation filters before sorting. |
| `StreamNestedJoins` | `true` | Merge-join grandchildren whose key starts with the parent's key instead of loading them into memory. |
| `CleanupStaleTempDirectories` | `true` | Delete sort folders left behind by killed runs when a run starts. |

The switches exist to rule an optimization out while debugging; they do not change the output (the library's tests
check this byte for byte). Like any setting, they can be given as environment variables, for example
`Etl__MaxDegreeOfParallelism=1`.

The app uses the .NET server garbage collector (set in `DataMonger.csproj`), which collects on all cores while the
ETL renders on all cores. `DOTNET_gcServer=0` switches back to the workstation collector for one run.
