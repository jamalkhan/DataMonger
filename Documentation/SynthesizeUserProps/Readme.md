# Synthesize User Props (fake data)

Generates synthetic customer profiles for the invented retailer **Acme Outfitters** as JSONL, with no input data at
all. Everything it needs (how many customers, which brands, name pools, probabilities and the value pools of every
enumerated field) comes from one `.synthconfig` file, so you change a scenario by editing the config, not the code.
Output can be made fully reproducible with a `seed` and an `anchorDate`.

The output is the profile file that [Synthesize Session Events](../SynthesizeSessionEvents/Readme.md) and
[Synthesize MyAccount Optin Events](../SynthesizeMyAccountOptins/Readme.md) read, and it can be sent to an
identify endpoint with [Upload JSONL Identifies](../UploadJsonlIdentifies/Readme.md).

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | This page: prompts, outputs, behavior |
| [SynthConfig.md](SynthConfig.md) | Every key of the `.synthconfig` file, every picklist, validation |
| [Schema.md](Schema.md) | Field-level reference of the generated profile |
| [samples/README.md](samples/README.md) | Runnable config, real expected output, and how to reproduce it |

## Syntax / how to run

Start the app (`dotnet DataMonger.dll`) and choose the menu entry

```
Synthesize User Props (fake data)
```

The menu is sorted alphabetically, so its number depends on which commands are installed (14 in the sample build). The
command does not use any site or API (`RunsPerSite = false`); it runs once, not once per configured site.

Prompts, in order:

| # | Prompt text | Accepted input | Default |
|---|---|---|---|
| 1 | `Config folder [<default folder>]:` (**first run only**, preceded by `First run: choose the folder where your synthetic-data config files (.synthconfig) live.`) | A folder path (surrounding double quotes stripped, `~/` expanded). It is created if missing, the bundled example configs are copied into it when it holds no `.synthconfig`, and the choice is saved in `settings.json` under the DataMonger home folder. | `<home>/Config/SynthConfig`, where `<home>` is `$DATAMONGER_HOME` or `~/Documents/DataMonger` |
| 2 | `Select a number, enter a path to your own .synthconfig, 'c' to change the config folder, or 'q' to cancel:` (after the list `Available synthetic-data configs (<folder>):`) | A number from the list, a path to a `.synthconfig` file, `c` (asks prompt 1 again for a new folder and re-lists), or `q`. Empty input re-prompts. End of input cancels. | none (must answer) |
| 3 | `Number of customers to synthesize [N]:` | A positive integer; commas are removed (`1,000`). Anything else prints `[warn] invalid number; using default` and uses the default. | `recordCount` from the config |
| 4 | `Output folder for the .jsonl file(s) [path]:` | A folder path (quotes stripped). Created if missing. | The user's Desktop folder, or the current directory if the OS reports none |
| 5 | `Continue and generate the full set of N record(s) as .jsonl? [y/N]:` | `y` or `n`; empty means `n`; anything else re-prompts. | `n` |

Prompt 1 appears only the first time, or later if the saved folder no longer exists. After the run the command prints
a summary of the settings in effect (config path, record count, brands, seed, anchor, output folder, split size, run
timestamp), then the preview file, then the final list of files.

Scripted example (answers piped on stdin; the first line is the menu number, the empty lines accept defaults):

```
14              <- menu entry
                <- prompt 1: accept the default config folder (first run)
1               <- prompt 2: acme.synthconfig
                <- prompt 3: keep recordCount
out/profiles    <- prompt 4
y               <- prompt 5
```

## Inputs

* One `.synthconfig` file (JSON with comments). See [SynthConfig.md](SynthConfig.md).
* The config folder is seeded on first use with the bundled `data/synthconfig/*.synthconfig` files (the build copies
  them next to the app; the command looks in `./data/synthconfig` first and then in the app folder's
  `data/synthconfig`). The repository ships `acme.synthconfig`, so a fresh install has one config to pick.
* Nothing else is read: no CSV, profile or product files.

## Outputs

Everything is written to the output folder chosen at prompt 4.

| File | When | Content |
|---|---|---|
| `<fileBaseName>_<timestamp>.json` | Always, after step 1 | The first record, pretty-printed, for review |
| `<fileBaseName>_<timestamp>.jsonl` | The full run produced a single file | One compact profile per line |
| `<fileBaseName>_<timestamp>_<n>.jsonl` | The full run produced more than one file | The same, split every `splitEvery` records; `n` starts at 1 |

`<timestamp>` is the **local** time the run started, `yyyyMMdd_HHmmss`. It is the only part of the output that is not
reproducible (it is in the file names, not in the content). The record layout is in [Schema.md](Schema.md).

## Remarks

* **Two steps.** The command first writes record #1 as a pretty-printed preview and asks whether to continue. Answering
  `n` stops after the preview and writes no `.jsonl`. When you answer `y`, record #1 is replayed verbatim as the first
  line of the `.jsonl` (the `.jsonl` starts with exactly what you reviewed), and records 2..N continue from the same
  random generator.
* **Validation first.** The config is parsed and fully validated (see [SynthConfig.md#validation](SynthConfig.md#validation))
  before any file is created. An invalid config prints `[FAIL] Invalid config <path>:` followed by one `  - ...` line per
  problem and the command returns with one failure and writes nothing.
* **Determinism.** With `seed` and `anchorDate` set, two runs with the same config produce byte-identical records on any
  machine and under any culture. Without `seed` every run differs. Without `anchorDate` "today" is the real UTC clock, so
  a seeded run repeats only within one day (the command prints a `[note]`). The preview record is part of the same
  random sequence, so it does not change the result.
* **User ids.** Record *i* (0-based) is `CUST_<startUserSeq + i>`, so successive runs with the same `startUserSeq`
  produce the same ids and emails; change `startUserSeq` to generate a non-overlapping batch.
* **Memory and speed.** Records are streamed straight to disk; memory use does not grow with `recordCount`.
* **Idempotency.** Each run writes new files named with the run timestamp; nothing is overwritten unless two runs start in
  the same second with the same `fileBaseName` and folder.
* **Once per session.** The command remembers that it already ran in this process and prints `[skip]` if it is invoked
  again (the menu normally exits after one command).
* **Result.** The command reports the number of records written as its success count (1 if you stop after the preview,
  0 failures) and 1 failure for a missing/invalid config or any exception.
* **Errors.** `[FAIL] No config file provided or it does not exist. Aborting.` (prompt 2 cancelled, end of input);
  `[FAIL] Invalid config ...` (validation or JSON errors); `[FAIL] Could not read config: ...` (file I/O);
  `[FAIL] <ExceptionType>: <message>` followed by the stack trace for anything unexpected.

## Compatibility with the upload commands

Each line is a self-contained JSON object. [Upload JSONL Identifies](../UploadJsonlIdentifies/Readme.md) sends every line
unchanged as the request body, and the mock API (`DataMonger.MockApi`) accepts any well-formed JSON object, so these
profiles upload as they are (verified against the mock API: the 12 lines of the committed sample output were all accepted with HTTP 202 by the identify route). The profiles do not use a wrapper
object such as `{"subscriber": {...}}`. If the service you target expects a different shape, reshape the file first
(for example with [Extract / Transform / Load](../ExtractTransformLoad/Readme.md)).

## Examples

* [samples/README.md](samples/README.md): the example config, exact reproduction steps and the committed expected output
  (12 profiles in 3 files plus the preview record).

## Related

* [Synthesize Session Events](../SynthesizeSessionEvents/Readme.md): page views, cart events and purchases per profile
* [Synthesize MyAccount Optin Events](../SynthesizeMyAccountOptins/Readme.md): marketing opt-in events per profile
* [Upload JSONL Identifies](../UploadJsonlIdentifies/Readme.md)
* [Mock API](../MockApi/Readme.md)
