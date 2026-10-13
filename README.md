# DataMonger

A .NET toolkit for wrangling large customer-data files: split and join huge CSVs with bounded memory,
transform them with a config-driven ETL engine, anonymize PII, generate synthetic data, and push
JSONL feeds to an HTTP API.

Released under [The Unlicense](LICENSE).

## Projects

| Project | What it is |
|---|---|
| `DataMonger` | Interactive console app. Each command is an `IConsoleCommand` discovered by reflection. |
| `DataMonger.Common` | Shared types: command context, site config, logging, path helpers. |
| [`bleak.Data.InfiniteDataLoader`](https://github.com/jamalkhan/bleak.Data.InfiniteDataLoader) | Streaming CSV reader/writer/splitter, external sort, and merge join that work on files larger than RAM. |
| [`bleak.Data.Transformer`](https://github.com/jamalkhan/bleak.Data.Transformer) | Config-driven ETL engine (`.etlconfig`): sources, joins, typed fields, transform pipelines, JSONL output. See its [README](https://github.com/jamalkhan/bleak.Data.Transformer/blob/main/src/bleak.Data.Transformer/README.md) and [ETL-CONFIG.md](https://github.com/jamalkhan/bleak.Data.Transformer/blob/main/src/bleak.Data.Transformer/ETL-CONFIG.md). |
| [`bleak.Data.Anonymizer`](https://github.com/jamalkhan/bleak.Data.Anonymizer) | Config-driven PII anonymizer (`.anonconfig`). See [ANON-CONFIG.md](https://github.com/jamalkhan/bleak.Data.Anonymizer/blob/main/src/bleak.Data.Anonymizer/ANON-CONFIG.md). |
| `*.Tests` | MSTest projects for `DataMonger` and `DataMonger.Common`. |

The three `bleak.Data.*` libraries live in their own repositories and are consumed here as NuGet packages.

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
