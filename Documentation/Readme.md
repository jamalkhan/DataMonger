# DataMonger documentation

DataMonger is a .NET toolkit for working with large customer-data files: splitting and joining CSVs larger
than memory, transforming them to JSONL with a config-driven ETL engine, anonymizing PII, generating synthetic
data, and replaying JSONL feeds against an HTTP API.

This documentation is organised by **command** (each entry in the console menu). Every command folder has a
`Readme.md` index, reference pages for its configuration, and a `samples/` folder with invented input files,
config files, and the real output the command produced from them.

> All data in this documentation is fictional ("Acme Outfitters"). Nothing here comes from a real customer.

## Contents

| Topic | Page |
|---|---|
| How the app is put together | [Architecture](Architecture.md) |
| `appsettings.json`, sites, local data folder | [Configuration](Configuration.md) |
| Local mock HTTP API used by every API sample | [Mock API](MockApi/Readme.md) |

### Commands

Menu entries are listed alphabetically by the app, so their numbers can change when commands are added; use the
name, not the number.

| Menu entry | Folder | Kind | Needs API |
|---|---|---|---|
| Extract / Transform / Load (config-driven) | [ExtractTransformLoad](ExtractTransformLoad/Readme.md) | Offline ETL engine | No |
| Anonymize PII (config-driven) | [AnonymizePii](AnonymizePii/Readme.md) | Offline | No |
| Split CSV into N-row files | [SplitCsv](SplitCsv/Readme.md) | Offline | No |
| Synthesize User Props (fake data) | [SynthesizeUserProps](SynthesizeUserProps/Readme.md) | Synthetic data | No |
| Synthesize Session Events (page views/cart/purchase, fake data) | [SynthesizeSessionEvents](SynthesizeSessionEvents/Readme.md) | Synthetic data | No |
| Synthesize MyAccount Optin Events (fake data) | [SynthesizeMyAccountOptins](SynthesizeMyAccountOptins/Readme.md) | Synthetic data | No |
| Generate Email Conversion data | [GenerateEmailConversions](GenerateEmailConversions/Readme.md) | Synthetic data | No |
| API => Upload JSONL Events (track event) | [UploadJsonlEvents](UploadJsonlEvents/Readme.md) | API upload | Yes |
| API => Upload JSONL Identifies (identify user) | [UploadJsonlIdentifies](UploadJsonlIdentifies/Readme.md) | API upload | Yes |
| API => Upload JSONL Resources (create/update) | [UploadJsonlResources](UploadJsonlResources/Readme.md) | API upload | Yes |
| API => Email Conversion Events | [ApiEmailConversionEvents](ApiEmailConversionEvents/Readme.md) | API upload | Yes |
| API => Bulk Runner (stress test) | [ApiBulkRunner](ApiBulkRunner/Readme.md) | API load test | Yes |
| Anonymous purchases | [PurchaseAnonymous](PurchaseAnonymous/Readme.md) | API demo traffic | Yes |
| Known (identified) purchases | [PurchaseKnown](PurchaseKnown/Readme.md) | API demo traffic | Yes |

## Quick start

Requires the .NET 10 SDK.

```sh
# 1. Build
dotnet build DataMonger.slnx

# 2. Configure sites (placeholders are fine for offline commands)
cp DataMonger/appsettings.example.json DataMonger/appsettings.user.json

# 3. Provide the one file the app always reads at startup: LocalData/products.csv
#    (a ready-made example is in PurchaseKnown/samples/inputs)
mkdir -p LocalData && cp Documentation/PurchaseKnown/samples/inputs/products.csv LocalData/

# 4. Run
dotnet run --project DataMonger
```

The first time you run a command that needs a config file (ETL, anonymize, synthesize user props), the app asks
where to keep your configs (default `~/Documents/DataMonger/Config/<Kind>Config`), copies the bundled examples in,
and remembers the choice; see [Configuration](Configuration.md#config-folders).

The app prints the configured sites, then a numbered menu. Type the number of a command and press Enter; the
command asks for whatever else it needs (input path, output folder, ...). Type `q` to quit.

To try the API commands without a real service, start the [mock API](MockApi/Readme.md) in a second terminal
and point a site at it.

## How a command runs

- Commands that work only on local files (ETL, anonymize, split, synthesize) run **once**, regardless of how many
  sites are configured.
- Commands that call an API run **once per configured site**, in the order the sites appear in
  `appsettings.json`.
- Every command returns a `(success, failure)` count that it prints in its summary.
- Diagnostics go to `LocalData/logs/app.log`.

See [Architecture](Architecture.md) for the details.
