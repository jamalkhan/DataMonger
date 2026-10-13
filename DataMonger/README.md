# DataMonger (console app)

Interactive console app. Every `IConsoleCommand` in `Commands/` is discovered by reflection and
listed in the menu.

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

## Local data

Input and output data does not live in this repo. Put it under `../LocalData/` (gitignored). Config
files for the ETL, anonymizer, and synthetic-data commands live in `data/etlconfig`,
`data/anonconfig`, and `data/synthconfig`; keep configs that reference real data in `LocalData/`
rather than committing them.
