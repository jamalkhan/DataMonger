# DataMonger (console app)

Interactive console app targeting .NET 10. Every `IConsoleCommand` in `Commands/` is discovered by
reflection and listed in the menu.

## Startup

`Program` builds a .NET Generic Host (`Host.CreateApplicationBuilder`, content root = the build output folder) for
configuration, logging and dependency injection, but never starts it, so the console shows only the app's own output.
`Hosting/DataMongerServiceCollectionExtensions.AddDataMonger` registers the services: logging to the per-run JSON-lines
file only (no console logger), a named `HttpClient` from `IHttpClientFactory`, the shared `RestClient`, and the
config-folder and file-loader helpers, all as singletons. Commands are not registered; `Hosting/CommandFactory` creates
the chosen one per site with `ActivatorUtilities.CreateInstance`, passing that site's `CommandContext` and resolving
the rest of the constructor (its `ILogger<T>`, `RestClient`, helpers) from the container. A new command only needs a
constructor that takes a `CommandContext` plus registered services; `ServiceRegistrationTests` fails if one is missing.
See [Architecture](../Documentation/Architecture.md) and [Configuration](../Documentation/Configuration.md).

Packages referenced here: `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Http`,
`Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.Logging`,
`Microsoft.Extensions.Logging.Configuration` and `System.Threading.RateLimiting` (all 10.0.14), `bleak.Api.Rest` 2.6.0,
`bleak.Data.InfiniteDataLoader` and `bleak.Data.Anonymizer` 2.0.1, and `bleak.Data.Transformer` 2.1.0.

## Configuring API credentials

`appsettings.example.json` is committed and contains only placeholder values. `appsettings.json` and
`appsettings.user.json` are gitignored — never commit real credentials.

To run against real APIs:

1. Copy `appsettings.example.json` to `appsettings.user.json` next to it and fill in your real
   `Sites` entries (`BaseUrl`, `SiteId`, `Username`, `Password`).
2. On every build (`dotnet build` or `dotnet run`), a target in `DataMonger.csproj` copies
   `appsettings.user.json` (or `appsettings.json`, if you prefer that name) over the build output's
   `appsettings.json`, so the app picks up your credentials with no manual copy step.

If neither file exists, the build falls back to the placeholder `appsettings.example.json`; the app
starts, but any API call fails against the fake site.

Because configuration goes through the Generic Host, environment variables and command-line arguments override
`appsettings.json` (for example `Logging__LogLevel__bleak=Debug`); see
[Configuration](../Documentation/Configuration.md).

## Local data

Input and output data does not live in this repo. Put it under `../LocalData/` (gitignored). Config
files for the ETL, anonymizer, and synthetic-data commands live in `data/etlconfig`,
`data/anonconfig`, and `data/synthconfig`; keep configs that reference real data in `LocalData/`
rather than committing them.
