# Configuration

## `appsettings.json`

The app reads `appsettings.json` from its output folder at startup. It is **required**: startup fails if the file
is missing or the `Sites` array is empty.

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
| `LocalData/logs/app.log` | Application log. |

## Concurrency

`MaxParallelism` is fixed at 50 in `Program.cs`; it is the ceiling on concurrent requests for commands that
use it. It is not currently configurable.
