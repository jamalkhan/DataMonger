# Anonymize PII (config-driven)

Replaces personally identifiable information in delimited files with obviously-fake values, driven entirely by a
`.anonconfig` JSON file. For each input file the config lists the columns to anonymize and the method to use for
each (fake names, e-mail addresses, phone numbers, dates, addresses, hex tokens, GUIDs, constants, and so on).
Every other column, including join keys such as `customer_id`, passes through unchanged, so the anonymized files
still join to each other exactly as the originals did. The originals are never modified or deleted: results are
written to a separate folder under a renamed file name.

The command is a thin console front end over the `bleak.Data.Anonymizer` library
(`AnonymizeEngine.Run`). It is a local, site-independent transform: it never calls an API and does not use the
site settings in `appsettings.json` (the app still needs to start; see [Configuration](../Configuration.md)).

## Contents

| Page | What it covers |
|---|---|
| [Readme.md](Readme.md) | This page: how to run it, prompts, outputs, remarks. |
| [AnonConfig.md](AnonConfig.md) | The complete `.anonconfig` schema: every property, default and allowed value. |
| [Anonymizers.md](Anonymizers.md) | Reference for every anonymizer method: options, group semantics, determinism, output examples. |
| [FileHandling.md](FileHandling.md) | Byte-copy fast path, streaming, output naming, refusing to overwrite a source, error policy, missing columns. |
| [samples/README.md](samples/README.md) | Runnable inputs, configs and real expected output. |

## Syntax / how to run

Start the app and choose the menu entry **Anonymize PII (config-driven)**. The menu is sorted alphabetically,
so the number depends on which commands exist; choose by name.

```text
=== DataMonger ===
...
Available commands (will run against all 1 site(s)):
  1) Anonymize PII (config-driven)
  ...
  q) Quit
Choice: 1
```

Because the command does not depend on a site (`RunsPerSite` is false), it runs exactly once, regardless of how
many sites are configured. A second run in the same process is refused with
`[skip] AnonymizePii already ran this session (it is site-independent).` (the app exits after one command, so
you will only see this if the command is invoked twice in one process).

### Prompts, in order

**1. Config folder (first run only)**

The first time you run this command it asks where your `.anonconfig` files live and remembers the answer:

```text
First run: choose the folder where your anonymization config files (.anonconfig) live.
Config folder [~/Documents/DataMonger/AnonConfig]:
```

Press Enter to accept the default, or type a folder (quotes are trimmed, `~` expands). The command creates the
folder, copies the bundled `example.anonconfig` into it when it holds no `.anonconfig` yet, and saves the choice in
`<home>/settings.json` (`ConfigFolders.Anon`; the home folder is `$DATAMONGER_HOME` or `~/Documents/DataMonger`). A
complete first-run transcript is in [samples/expected-output/console/first-run.txt](samples/expected-output/console/first-run.txt).
Later runs skip this question unless the saved folder was deleted, in which case it is asked again (and the folder
is re-created and re-seeded). See [Configuration](../Configuration.md#config-folders) for the details shared by all
config-driven commands.

**2. Config selection**

```text
Available anonymization configs (~/Documents/DataMonger/AnonConfig):
  1) example.anonconfig
Select a number, enter a path to your own .anonconfig, 'c' to change the config folder, or 'q' to cancel:
```

| Answer | Result |
|---|---|
| A number `1`..`N` | Uses that config from the config folder. |
| A path (absolute, relative to the current directory, or starting with `~/`) | Uses that file if it exists; the saved config folder is not changed. Surrounding double quotes are trimmed. |
| `c` (any case) | Asks for a different config folder (`Choose the folder for your anonymization config files (.anonconfig).`), creates/seeds/saves it and lists it. |
| `q` (any case) | Cancels: prints `[FAIL] No config file provided or it does not exist. Aborting.` |
| Empty line | Ignored; the prompt repeats. |
| Anything else (a number out of range, a path that does not exist) | Prints `[warn] enter 1-N, a valid .anonconfig path, 'c', or 'q'.` (or `[warn] file not found: <input>` when the folder holds no configs) and repeats the prompt. |
| End of input (stdin closed) | Cancels, same as `q`. |

There is no default; an answer is required. When the folder holds no `.anonconfig` the list is replaced by
`No .anonconfig files found in <folder>.`.

**3. Output folder (only when the config does not set `outputFolder`)**

```text
Output folder for the anonymized files [<Desktop folder>]:
```

The default is the current user's Desktop directory (or the current directory if that cannot be determined).
Press Enter to accept it. This prompt is skipped entirely when the config has a non-blank `outputFolder`.
See the Remarks for how a relative answer is resolved.

Nothing else is prompted for: input files, columns and methods all come from the config.

### The config folder

The list in prompt 2 is the content of your **anonymization config folder** (default
`~/Documents/DataMonger/AnonConfig`), not a folder bundled with the build. Within it, every `*.anonconfig` file
directly inside the folder (no subfolders) is listed, sorted by name, case-insensitively. The seeded
`example.anonconfig` reads `~/Documents/DataMonger/Data/csvs` and writes to `~/Documents/DataMonger/Data/csvs_anonymized`
(see [Configuration](../Configuration.md#relation-to-the-data-folder)); it is a template, so edit your copy or point
`inputFolder`/`outputFolder` elsewhere before running it on real files.

## Inputs

* A `.anonconfig` file; see [AnonConfig.md](AnonConfig.md). JSON with `//` and `/* */` comments and trailing
  commas allowed.
* The delimited files named by the config's `files[].input` entries. See [FileHandling.md](FileHandling.md).

## Outputs

* One output file per `files[]` entry, written to the output folder (or to wherever the job's `output` points).
  The default name swaps the `src_` prefix for `anonymized_` (`src_customers.csv` becomes
  `anonymized_customers.csv`).
* Files with an empty `columns` list are byte-copied; all others are re-written as UTF-8 without a BOM with `\n`
  record terminators.
* Console output only; the command writes no log file and no report.

### Progress and summary output

After the prompts the command echoes what it is about to do:

```text
Config        : configs/basic.anonconfig
Files         : 4
On error      : Skip
Output folder : (from config)
Rename        : src_* -> anonymized_*
```

`Output folder` shows `(from config)` when the config supplied it, otherwise the folder you typed. Then the
engine logs one line per file as it finishes:

| Line | Meaning |
|---|---|
| `[anon] src_x.csv -> anonymized_x.csv: 10 rows, 119 cells across 13 column(s)` | A file was anonymized. `, N error(s)` is appended when cells failed under `onError: skip`. |
| `[copy] src_orders.csv -> anonymized_orders.csv (no PII columns)` | Byte-copy fast path. |
| `[warn] src_x.csv: column 'c' not found in header — skipped.` | A configured column does not exist; the file is still processed. |
| `[warn] src_x.csv row 3 column 'dob': <message> (left unchanged)` | One cell failed under `onError: skip`. The row number counts data rows from 1. |
| `[skip] src_y.csv: input not found: <path>` | Input file missing under `onError: skip`. |

The `=== Done ===` section then repeats one line per file and totals:

```text
=== Done ===
anonymized_customers.csv: 10 rows, 119 cells anonymized
anonymized_orders.csv: copied (no PII columns)
anonymized_newsletter.psv: 5 rows, 8 cells anonymized
anonymized_tickets.csv: 3 rows, 5 cells anonymized

Files: 4, rows anonymized: 18, cells: 132
Total elapsed: 00:00:00.010
```

Per-file detail is `N rows, M cells anonymized`, plus `, K error(s)` and `, missing columns: a, b` when
applicable; a skipped file shows `SKIPPED (<reason>)` and is not counted in the totals. The `Total elapsed` value
varies from run to run (the saved samples omit that line).

A fatal problem prints `[FAIL] <ExceptionType>: <message>` followed by a stack trace; see
[FileHandling.md](FileHandling.md#error-policy) for what is and is not fatal. The process exit code is `0`
regardless of success or failure (`Program.Main` ignores the command's result), so scripts must inspect the output
rather than `$?`.

## Remarks

* **Originals are never modified or deleted.** Input files are opened read-only (`FileShare.Read`). The engine
  refuses to write a file onto its own source path (comparison is case-insensitive on Windows and macOS).
  Output files that already exist are overwritten without warning.
* **Only configured columns change.** Matching is by header name, case-insensitive. Keys and all unlisted
  columns pass through untouched.
* **Random per occurrence, not a mapping.** Each cell is faked independently, so the same real e-mail address in
  two rows (or two files) becomes two different fake addresses. The same property is why join keys are left alone.
  Use `group` to make columns within one row agree; see [Anonymizers.md](Anonymizers.md#group-semantics).
* **Reproducibility.** Without `run.seed` every run produces different fakes. With a seed the run is
  reproducible: the same config, inputs and engine version give byte-identical output. The seed drives a single
  random stream shared by all files in the run, so adding, removing or reordering files or columns changes the
  values generated after that point. See [Anonymizers.md](Anonymizers.md#determinism).
* **Empty cells are not fabricated** unless the column sets `whenEmpty: generate`.
* **Sequential and streaming.** Files are processed one after another on a single thread, one record at a time,
  so memory use does not depend on file size.
* **Relative paths in the config resolve against the config file's folder**, not the current directory. The one
  exception is the answer to the output-folder prompt, which is also resolved against the config file's folder
  (not the current directory), even though you typed it at the console. Type an absolute path to avoid surprises.
* **Delimiter and quote** are per file; see [AnonConfig.md](AnonConfig.md#files). `hasHeader: false` is
  currently unreliable; see the note in [AnonConfig.md](AnonConfig.md#hasheader).
* **Custom anonymizers** can be registered with `AnonymizerRegistry.Register` when calling the library directly,
  but the console command always uses the built-in set.

## Examples

All of these are runnable from the [samples](samples/README.md) folder:

| Scenario | Config |
|---|---|
| Everyday run: names/e-mail/address groups, phone, date, hex, byte-copy of a PII-free file, pipe-delimited file, quoted newlines | [basic.anonconfig](samples/configs/basic.anonconfig) |
| `whenEmpty` passthrough vs generate | [when-empty.anonconfig](samples/configs/when-empty.anonconfig) |
| Every method with options | [all-methods.anonconfig](samples/configs/all-methods.anonconfig) |
| Bad cell with `onError: skip` | [bad-cell-skip.anonconfig](samples/configs/bad-cell-skip.anonconfig) |
| Bad cell with `onError: abort` | [bad-cell-abort.anonconfig](samples/configs/bad-cell-abort.anonconfig) |
| Missing column and missing input file | [missing.anonconfig](samples/configs/missing.anonconfig) |

Minimal reproduction, run from the `samples` folder with the built app (`DataMonger.dll`) on the path of your
choice. `DATAMONGER_HOME` keeps the saved config folder out of your real Documents folder; on a brand-new home the
first run asks for the config folder, so the extra empty line accepts the default:

```sh
export DATAMONGER_HOME=./scratch-home
printf '1\n\nconfigs/basic.anonconfig\n' | dotnet path/to/DataMonger.dll   # first run
printf '1\nconfigs/basic.anonconfig\n'    | dotnet path/to/DataMonger.dll   # later runs
```

## Related

* [Extract / Transform / Load](../ExtractTransformLoad/Readme.md): the sibling config-driven engine this command is modeled on.
* [Split CSV into N-row files](../SplitCsv/Readme.md): another local file transform.
* [Configuration](../Configuration.md)
