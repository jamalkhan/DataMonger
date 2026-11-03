# Architecture

## Projects

| Project | Role |
|---|---|
| `DataMonger` | Console app. Discovers every `IConsoleCommand` by reflection, shows a menu, runs the chosen command. Contains the commands in `Commands/`, the synthetic-data builders in `Synth/`, and JSONL helpers in `Jsonl/`. |
| `DataMonger.Common` | Shared types: `IConsoleCommand`, `CommandContext`, `SiteConfig`, domain records, `DataPaths`, `JsonLinesFileLoggerProvider` (the log sink). |
| `bleak.Data.InfiniteDataLoader` | Streaming CSV reader/writer/splitter, external sort and merge join that run in bounded memory on files larger than RAM. |
| `bleak.Data.Transformer` | The ETL engine behind *Extract / Transform / Load*. Parses `.etlconfig`, compiles it to a plan, executes joins and writes JSONL. |
| `bleak.Data.Anonymizer` | The engine behind *Anonymize PII*. Parses `.anonconfig` and streams files through column anonymizers. |
| `DataMonger.MockApi` | A small ASP.NET minimal API that stands in for a real HTTP service in samples and tests. |
| `*.Tests` | MSTest projects for each library and the app. |

```
DataMonger ──▶ DataMonger.Common
    │  └────▶ bleak.Data.InfiniteDataLoader ──▶ DataMonger.Common
    ├───────▶ bleak.Data.Transformer ────────▶ bleak.Data.InfiniteDataLoader
    └───────▶ bleak.Data.Anonymizer
```

## The command model

```csharp
public interface IConsoleCommand
{
    string Name { get; }                       // text shown in the menu
    bool RunsPerSite => true;                  // false = run once, not once per configured site
    Task<(int success, int failure)> Execute();
}
```

At startup `Program` loads `appsettings.json`, reads `LocalData/products.csv`, builds a `CommandContext` per
site, instantiates every type implementing `IConsoleCommand` (constructor takes a `CommandContext`), and sorts the
menu by `Name`. A command with `RunsPerSite == true` is instantiated and executed once for each site in order; a
command with `RunsPerSite == false` is executed once with a context built from the first site.

Several upload commands also show their own **site picker** instead of relying on the per-site loop; each
command page says which.

### `CommandContext`

| Member | Meaning |
|---|---|
| `Products` | Product list read from `LocalData/products.csv` at startup (used by the demo-traffic and email commands). |
| `Rest` | HTTP client wrapper used for API calls. |
| `ActivityUri`, `IdentifyUri` | Pre-built `…/ver2/{SiteId}/activities` and `…/ver2/{SiteId}/subscribers/identify` URLs for the current site. |
| `BaseUrl`, `SiteId`, `Username`, `Password` | Raw site coordinates (credentials sent with each request). |
| `AllSites` | Every configured site, for commands that let you choose. |
| `MaxParallelism` | Upper bound on concurrent requests (50). |
| `LoggerFactory` | Creates loggers that write to the run's JSON-lines log file (see below). |

## Logging

`Program` builds one `ILoggerFactory` per run: levels come from the `Logging` section of `appsettings.json`
(Information by default) and the only sink is `JsonLinesFileLoggerProvider`, which queues each line on a channel and
lets one background task append it to `LocalData/logs/datamonger_<yyyyMMdd_HHmmss>.jsonl`. `Program` opens a `RunId`
scope around the whole run and logs "run started" / "run finished". Each command takes its logger from
`ctx.LoggerFactory.CreateLogger<T>()`, opens a `{Command, SiteId}` scope, and logs through source-generated
`[LoggerMessage]` methods (EventIds in blocks of 100 per class). Helpers that do real I/O (`ConfigFolders`, `CsvLoader`,
`CommandConfigFile`, `SynthConfigLoader`, `ProductCatalog`) take an optional logger, and every bleak API that accepts
one (`EtlEngine.Run`, `AnonymizeEngine.Run`, `CsvSplitter.Split`, `JsonlSplitWriter`, `RestClient`) gets
`ctx.LoggerFactory`. The progress lines the bleak 1.x engines used to print through a console callback are now
printed by the commands themselves from the engines' result objects. Console output is separate and
unchanged. See [Configuration](Configuration.md#logging) for levels and the line format.

## Data flow

```
 CSV inputs ──▶ [Split CSV]──▶ smaller CSVs
      │
      ├────────▶ [Anonymize PII] ──▶ anonymized CSVs
      │
      └────────▶ [Extract / Transform / Load] ──▶ JSONL feeds ──▶ [API ⇒ Upload JSONL …] ──▶ HTTP API
 synthconfig ──▶ [Synthesize …] ──▶ JSONL feeds ─────────────────────┘
```

## Where files live

| Location | Contents | In git? |
|---|---|---|
| `LocalData/` (sibling of the project folders) | Your input and output data, logs | No (ignored) |
| `DataMonger/data/etlconfig`, `data/anonconfig`, `data/synthconfig` | Bundled configs offered by each command's picker | Yes |
| `Documentation/<Command>/samples/` | Invented sample inputs, configs and expected output | Yes |
| `DataMonger/appsettings.user.json` | Real credentials | No (ignored) |

Paths under `LocalData/` are resolved by `DataPaths.Root()`: first four directories above the app's `bin/Debug/net10.0`
folder (which is the repo root when run from a normal build), then the current working directory.
