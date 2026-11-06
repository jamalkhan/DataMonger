# DataMonger

A .NET toolkit for wrangling large customer-data files: split and join huge CSVs with bounded memory,
transform them with a config-driven ETL engine, anonymize PII, generate synthetic data, and push
JSONL feeds to an HTTP API.

Released under [The Unlicense](LICENSE).

## Projects

| Project | What it is |
|---|---|
| `DataMonger` | Interactive console app (.NET 10) on the .NET Generic Host: configuration, logging and dependency injection come from `Host.CreateApplicationBuilder`. Each command is an `IConsoleCommand` discovered by reflection and created with its services injected; see [Architecture](Documentation/Architecture.md). |
| `DataMonger.Common` | Shared types: per-site command context, site config, config-folder and file loaders, the JSON-lines log sink, path helpers. |
| [`bleak.Data.InfiniteDataLoader`](https://github.com/jamalkhan/bleak.Data.InfiniteDataLoader) | Streaming CSV reader/writer/splitter, external sort, and merge join that work on files larger than RAM. |
| [`bleak.Data.Transformer`](https://github.com/jamalkhan/bleak.Data.Transformer) | Config-driven ETL engine (`.etlconfig`): sources, joins, typed fields, transform pipelines, JSONL output. See its [README](https://github.com/jamalkhan/bleak.Data.Transformer/blob/main/src/bleak.Data.Transformer/README.md) and [ETL-CONFIG.md](https://github.com/jamalkhan/bleak.Data.Transformer/blob/main/src/bleak.Data.Transformer/ETL-CONFIG.md). |
| [`bleak.Data.Anonymizer`](https://github.com/jamalkhan/bleak.Data.Anonymizer) | Config-driven PII anonymizer (`.anonconfig`). See [ANON-CONFIG.md](https://github.com/jamalkhan/bleak.Data.Anonymizer/blob/main/src/bleak.Data.Anonymizer/ANON-CONFIG.md). |
| `DataMonger.MockApi` | Local ASP.NET Core mock of the HTTP API, used by the API samples. |
| `*.Tests` | MSTest projects for `DataMonger` and `DataMonger.Common`. |

The three `bleak.Data.*` libraries live in their own repositories and are consumed here as NuGet packages.

## Dependencies

All projects target `net10.0`. NuGet packages:

| Package | Version | Used by |
|---|---|---|
| `Microsoft.Extensions.Hosting` | 10.0.14 | `DataMonger` (Generic Host: configuration, logging, DI) |
| `Microsoft.Extensions.Http` | 10.0.14 | `DataMonger` (`IHttpClientFactory` for the REST client) |
| `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Configuration.Json` | 10.0.14 | `DataMonger` |
| `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Configuration` | 10.0.14 | `DataMonger`; `Microsoft.Extensions.Logging` also in `DataMonger.Common.Tests` |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.14 | `DataMonger.Common` |
| `System.Threading.RateLimiting` | 10.0.14 | `DataMonger` |
| `bleak.Api.Rest` | 2.6.0 | `DataMonger` (brings in `Newtonsoft.Json`) |
| `bleak.Data.InfiniteDataLoader` | 2.1.0 | `DataMonger`, `DataMonger.Tests` |
| `bleak.Data.Anonymizer` | 2.0.1 | `DataMonger` |
| `bleak.Data.Transformer` | 2.2.0 | `DataMonger` |
| `MSTest` | 4.5.2 | test projects |
| `Microsoft.NET.Test.Sdk` | 18.11.0 | test projects |
| `coverlet.collector` | 10.1.0 | test projects |

`DataMonger.MockApi` uses only the ASP.NET Core shared framework.

## Build and test

Requires the .NET 10 SDK.

```sh
dotnet build DataMonger.slnx
dotnet test DataMonger.slnx
```

## Running

```sh
cp DataMonger/appsettings.example.json DataMonger/appsettings.json   # then edit with real values
dotnet run --project DataMonger
```

See [DataMonger/README.md](DataMonger/README.md) for credentials and local-data layout.

## Never commit real data or credentials

`.gitignore` excludes `appsettings.json`, `appsettings.user.json`, the `LocalData/` folder, logs, and
common data file types (`*.csv`, `*.jsonl`, `*.xlsx`). Keep customer data in `LocalData/` (a sibling
of the project folders) and credentials in `appsettings.user.json`.
