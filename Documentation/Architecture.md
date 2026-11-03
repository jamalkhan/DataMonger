# Architecture

## Projects

| Project | Role |
|---|---|
| `DataMonger` | Console app on the .NET Generic Host (configuration, logging, DI). Discovers every `IConsoleCommand` by reflection, shows a menu, runs the chosen command. Contains the commands in `Commands/`, the service registrations and `CommandFactory` in `Hosting/`, the synthetic-data builders in `Synth/`, and JSONL helpers in `Jsonl/`. |
| `DataMonger.Common` | Shared types: `IConsoleCommand`, `CommandContext`, `SiteConfig`, domain records, `DataPaths`, `ConfigFolders`, `CsvLoader`, `CommandConfigFile`, `JsonLinesFileLoggerProvider` (the log sink). |
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

At startup `Program` builds a .NET Generic Host (`Host.CreateApplicationBuilder`, content root = the build output
folder) purely for configuration, logging and dependency injection: the host is built and its `IServiceProvider` used,
but it is never started (no `Run`/`RunAsync`), so there are no hosted services and no lifetime messages on the console.
Configuration comes from the host's default sources: `appsettings.json` (required) and `appsettings.{Environment}.json`
from the output folder, then environment variables, then command-line arguments.

`Program` then validates `Sites`, registers the app's services with `AddDataMonger(logFilePath)`
(`DataMonger/Hosting/DataMongerServiceCollectionExtensions.cs`), builds the host and asks `ConfigFolders` to make sure
every config folder is known. For the menu it finds every type implementing `IConsoleCommand` by reflection, creates
each one through `CommandFactory` with a `CommandContext` for the first site, and sorts the menu by `Name`. A command
with `RunsPerSite == true` is created and executed once for each site in order, each time with that site's context; a
command with `RunsPerSite == false` is created and executed once with a context built from the first site.

Several upload commands also show their own **site picker** instead of relying on the per-site loop; each
command page says which.

### Services and command construction

Commands are not registered in the container. `CommandFactory` creates them with
`ActivatorUtilities.CreateInstance(serviceProvider, commandType, context)`: the per-site `CommandContext` is passed as a
runtime argument, and every other constructor parameter is resolved from the container. A command asks only for what
it uses, for example:

```csharp
public partial class PurchaseKnown(
    CommandContext ctx,                 // runtime argument: this site's data
    RestClient rest,                    // from the container
    ConfigFolders configFolders,
    CommandConfigFile configFile,
    CsvLoader csvLoader,
    ILogger<PurchaseKnown> logger) : IConsoleCommand
```

`AddDataMonger` registers (all singletons: they are stateless or thread-safe, and one process is one run):

| Service | Notes |
|---|---|
| Logging | `ILoggerFactory` / `ILogger<T>` with `JsonLinesFileLoggerProvider` as the **only** provider (the host's console, debug and event-source providers are removed, so nothing is logged to the console). The container creates and disposes the provider. |
| `HttpClient` | Named client `DataMonger.Rest` from `IHttpClientFactory` (`AddHttpClient`). Its handler is a `SocketsHttpHandler` with a 2-minute `PooledConnectionLifetime`, since the client is held for the whole run; the factory's own request logging is removed. |
| `RestClient`, `JsonSerializer` | `bleak.Api.Rest` client built from that `HttpClient`, the serializer and `ILogger<RestClient>`. Used by every API command. |
| `ConfigFolders` | Config-folder prompts and `settings.json`. |
| `CsvLoader`, `CommandConfigFile`, `SynthConfigLoader` | File loaders with their own logger category. Pure helpers (`CommandConfigFile.ResolvePath`, `SynthConfigLoader.FromJson`) stay static. |
| `CommandFactory` | Creates commands, including one command starting another (Synthesize Session Events hands off to Upload JSONL Events). |

Commands that call the bleak engines whose APIs take a factory (`EtlEngine.Run`, `AnonymizeEngine.Run`,
`CsvSplitter.Split`) inject `ILoggerFactory`. Commands that create a `JsonlSplitWriter` inject
`ILogger<JsonlSplitWriter>`, and Synthesize Session Events injects `ILogger<ProductCatalog>` for the static
`ProductCatalog.Load` (a catalog is per-run data, not a service).

`DataMonger.Tests/ServiceRegistrationTests.cs` builds the same host and creates every command through
`ActivatorUtilities`, so a constructor parameter without a registration fails the build's tests.

### `CommandContext`

Per-site, per-run data only; services come from the container.

| Member | Meaning |
|---|---|
| `ActivityUri`, `IdentifyUri` | Pre-built `…/ver2/{SiteId}/activities` and `…/ver2/{SiteId}/subscribers/identify` URLs for the current site. |
| `BaseUrl`, `SiteId`, `Username`, `Password` | Raw site coordinates (credentials sent with each request). |
| `AllSites` | Every configured site, for commands that let you choose. |
| `MaxParallelism` | Upper bound on concurrent requests (50). |

## Logging

Logging is the host's `ILoggerFactory`: levels come from the `Logging` section of the configuration
(Information by default) and the only sink is `JsonLinesFileLoggerProvider`, which queues each line on a channel and
lets one background task append it to `LocalData/logs/datamonger_<yyyyMMdd_HHmmss>.jsonl`. The container owns the
provider, so disposing the host at the end of `Main` drains and flushes the file. `Program` opens a `RunId`
scope around the whole run and logs "run started" / "run finished". Each command receives an `ILogger<T>` through its
constructor, opens a `{Command, SiteId}` scope, and logs through source-generated
`[LoggerMessage]` methods (EventIds in blocks of 100 per class). Helpers that do real I/O (`ConfigFolders`, `CsvLoader`,
`CommandConfigFile`, `SynthConfigLoader`) get their own `ILogger<T>` from the container and `ProductCatalog.Load`
takes one as a parameter, so each keeps its own category. Every bleak API that accepts a logger gets one from the
container: `RestClient` an `ILogger<RestClient>`, `JsonlSplitWriter` an `ILogger<JsonlSplitWriter>`, and `EtlEngine.Run`,
`AnonymizeEngine.Run` and `CsvSplitter.Split` the injected `ILoggerFactory`. The progress lines the bleak 1.x engines
used to print through a console callback are now printed by the commands themselves from the engines' result
objects. Console output is separate and unchanged. See [Configuration](Configuration.md#logging) for levels and the
line format.

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
