# TODO

Known bugs, silent data issues and behaviors worth knowing. Everything below was found by reading the code
and, where noted, reproducing it by running the command. File and line references are as of this writing and
will drift. Sections are ordered by how likely an item is to bite you.

Legend: **[silent]** = produces wrong or lost data without an error · **[crash]** = unhandled exception or aborted
run · **[behavior]** = works as coded but is surprising · **[docs]** = comment or document disagrees with code.

## 1. Data-loss and silent-corruption bugs

- **[silent] `hasHeader: false` drops the first data row (Anonymizer).** `bleak.Data.Anonymizer/src/bleak.Data.Anonymizer/Engine/AnonymizeEngine.cs:135`:
  `CsvReader` always consumes row 1 as the header; with `false` the header is merely not written, so row 1 is lost and column
  names are matched against it. Fix: add a headerless mode to `CsvReader`, or write the first row back out.
  `CsvSplitter.cs:50` (`includeHeader: false`) has the same flaw but is not reachable from the command.
- **[silent] ETL `hasHeader` is never read.** `bleak.Data.Transformer/src/bleak.Data.Transformer/Config/EtlConfig.cs:107`. The reader always treats row 1
  as the header, so a headerless source silently loses its first row.
- **[silent] A stray quote inside an unquoted cell merges rows or swallows the rest of the file.**
  `bleak.Data.InfiniteDataLoader/src/bleak.Data.InfiniteDataLoader/CsvReader.cs` (`ParseAll`) enters quoted mode on any quote character, not only at the start of a
  field (`12" pipe`). It affects ETL, Anonymize and Split CSV. No error is raised and the stray quote is dropped.
- **[silent] Split CSV hardcodes `,` as the delimiter.** Splitting a pipe- or tab-delimited file corrupts rows that contain a
  comma or quote.
- **[silent] `toDateTime` depends on the machine's time zone.** `bleak.Data.Transformer/src/bleak.Data.Transformer/Transforms/ValueConvert.cs` (~83-85) parses
  with `DateTimeStyles.RoundtripKind`, converts offset input to local time, and `Engine/RecordWriter.cs` (~37, ~991) then appends
  `Z` without converting to UTC. `2024-03-05T14:30:00+02:00` yields `12:30Z` on a UTC host and `21:30Z` in Asia/Tokyo. Fix:
  `DateTimeStyles.AdjustToUniversal`, or `ToUniversalTime()` when the kind is Local.
- **[silent] Two ETL outputs with the same `fileBaseName` and folder overwrite each other.** `Output/JsonlSplitWriter.cs` opens with
  `FileMode.Create`. The run report still shows the first output's record count. Both outputs default to the folder `output`.
- **[silent] Warnings from a record that is later skipped are still written and counted as "record kept".**
  `Engine/EtlEngine.cs:304-327`: under `onError: skip`, warnings raised before a later field fails are not drained.
- **[silent] Typos are not validated.** An unknown `from` column on a field yields null in every record and the field is omitted
  (`Engine/EtlCompiler.cs:311-322`). Misspelled JSON properties in any config (`.etlconfig`, `.anonconfig`) are ignored by the loader. A
  typo in an operand `{ "field": ... }` yields null and arithmetic then throws. Only `coalesce`, lookup and join columns are validated.
- **[silent] `mergeBy`, `mergePrefix` and `keyedSingle` without `keyBy` are ignored.** `EtlCompiler.CompileRelation`.
- **[silent] `load.format` is never read.** `EtlConfig.cs:536`. Any string, even `"xml"`, writes JSONL.
- **[silent] `version` in `.anonconfig` is never read**, and unknown properties are ignored.
- **[silent] Duplicate JSON property names.** keyedMap key collisions and unprefixed `mergeBy` collisions write duplicate keys.
  `distinctBy` and `children` on a keyedMap output are silently ignored, so those children are never rendered.
- **[silent] Blank join keys match blank join keys** in joins and lookups (`JoinKeys.Composite`, the lookup index).
- **[silent] `split` followed by a string conversion writes a .NET type name.** `type: "string"` on a field whose pipeline ends in
  `split` emits ``System.Collections.Generic.List`1[System.Object]``. Any op after `split` receives the list.
- **[silent] `externalMerge` sorting is not stable.** `List.Sort` / `PriorityQueue` do not preserve file order within a key, so a
  `cardinality: one` "first" child can differ from `hashInMemory`. Also, `cardinality: one` picks its first child *before* the
  relationship `filter` runs.
- **[silent] Unknown culture names are accepted** (`xx-NOPE`, `en_US`); only malformed names throw.
- **[silent] Row filters.** Blank cells make `lt`, `le` and `ne` true (blank is compared as `""`). `NumberStyles.Any` is advertised but
  `$10` does not parse with the invariant culture, so it falls back to ordinal text comparison (`Engine/RowFilter.cs` ~108-111).
- **[silent] Anonymizer `last4` with `digits` > 9** gives only 9 random digits and a zero-filled prefix (`BuiltinAnonymizers.cs:90-91`).
- **[silent] A split part counter above 9,999 breaks text sort order** (`D4` format: `part10000` sorts before `part2000`).
  Stale parts from an earlier run with more parts also survive a re-run.
- **[silent] `map` with `"default": null`** is indistinguishable from no default (`Default` is a nullable `JsonElement`).
- **[silent] Fractional JSON literals in arithmetic** are `double`, forcing double math (quote money literals as strings).

## 2. Crashes and aborted runs

- **[crash] ApiBulkRunner: every relative URL starting with `/` fails on macOS/Linux.** `Commands/ApiBulkRunner.cs:184`:
  `Uri.TryCreate(url, UriKind.Absolute, ...)` treats `/ver2/site/ping` as a `file:///` URI and the call fails with status 0
  ("The 'file' scheme is not supported"). Slash-less URLs work (the samples use them). Fix: accept `UriKind.Absolute` only for http/https,
  otherwise resolve against `BaseUrl`.
- **[crash] The app will not start without `LocalData/products.csv`,** even for offline commands, and a bad `products.csv`
  aborts startup even if you just quit (`Program.cs:34`). `DataMonger.Common/CsvLoader.cs` has no header detection (the
  first line is always dropped) and no quoting support. (A blank or short line now raises an `InvalidDataException` naming the
  file and line instead of an `IndexOutOfRangeException`.)
- **[crash] Upload preview prompts crash when stdin is redirected.** `Commands/UploadJsonlEvents.cs` (~410) and
  `UploadJsonlIdentifies.cs` use `Console.ReadKey` and throw `InvalidOperationException` under piped stdin. Events avoids it only in
  folder mode; Identifies has no skip. Scripted runs need a pty. Fix: fall back to `Console.ReadLine` when `Console.IsInputRedirected`.
- **[crash] `File.ReadAllLinesAsync` is unguarded** in `UploadJsonlIdentifies.cs:106` and `UploadJsonlResources.cs:68`; an unreadable
  file ends the process. Events handles this case.
- **[crash] A NaN or Infinity `double` result** (e.g. `divide` by literal 0 on a double) throws `ArgumentException` outside the field
  try/catch (`Engine/RecordWriter.cs` ~963-985), crashing the run instead of following `run.onError`. Fix: guard `double.IsFinite`.
- **[crash] keyedMap `keyExpr` failure ignores `onError`.** `Engine/RecordWriter.cs:704` (`WriteKeyedMap`) throws an `EtlRowException`
  that `Engine/EtlEngine.cs:260-270` does not catch (only `RowSkipException`), so a throwing pipeline (e.g. `map` with no default) aborts
  the run even under `skip`.
- **[crash] `map` with case keys that differ only by case** (`"A"` and `"a"`) fails at compile time with a raw `ArgumentException`
  instead of `EtlConfigException` (`TransformRegistry.cs`).
- **[crash] Anonymizer `abort` leaves a half-written output file** (header only), and the failure message names no file, row or column.
- **[crash] Upload/Resources:** a `resource_id` that is a JSON object or array was not tested and may throw from `Value<string>()`.
- **[crash] `ApiEmailConversionEvents`** catches only `JsonException` per line.

## 3. Wrong results reported

- **[behavior] The process exit code is always 0.** `Program.cs` ignores each command's `(success, failure)` result.
- **[behavior] Upload failure line numbers.** A failure on the first non-blank line is always reported as `L1` / `line=1`, even when it is
  not file line 1 (`UploadJsonlEvents.cs:198-199`, `UploadJsonlIdentifies.cs:138`). Fix: use `firstLineIndex + 1`.
- **[behavior] A `q` at the config prompt prints `[FAIL] No config file provided or it does not exist. Aborting.`** and counts a failure.
  A plain "cancelled" message returning `(0, 0)` would be clearer (`ConfigFolders.PromptForConfig` returns null for `q`, EOF and cancel alike).
- **[behavior] The ETL "lookup reused from an earlier output" log** also fires for a second field in the same output (`EtlEngine.cs:123`).
- **[behavior] The externalMerge error text prints `customer_idFalse`** (`Join/ExternalMergeJoinStrategy.cs`).

## 4. Behaviors worth knowing

- **No retries anywhere.** The upload commands, Bulk Runner and the purchase commands do not retry, and `Retry-After` is ignored.
  A failed request is simply counted.
- **Parallelism and limits are constants,** not configuration: `MaxParallelism = 50` (`Program.cs`), 1000 for Bulk Runner, and the
  email uploader's rate limit and 0-120 s inter-event delays (`ApiEmailConversionEvents.cs`). The delay is now a prompt (`0` = none).
- **Per-site semantics differ by command.** The upload commands (Events, Identifies, Resources) run their own site picker and
  guard with a static flag so they run once per process; the flag is set *before* anything can fail, so an aborted first iteration is
  never retried for the next site. ApiBulkRunner and the purchase commands run once per configured site. None of the upload commands set
  `RunsPerSite = false`.
- **Events `processed_files.log` is write-only;** there is no resume.
- **Known purchases:** an identify failure skips that customer's purchase.
- **Config folders:** `~` in the bundled example configs resolves to the user profile, **not** `DATAMONGER_HOME`, so relocating the home
  folder does not relocate `Data/csvs`. The first-run prompt reads one line; piping stdin needs a line for it.
- **The prompted ETL/Anonymize output folder is resolved against the config file's folder, not the current directory**
  (`AnonymizeEngine.cs:62`). A relative answer lands next to the config.
- **A Split CSV glob without a directory part fails** (`customers_*.csv` → "directory not found"); `./customers_*.csv` works
  (`SplitCsv.cs:135-137`).
- **hashInMemory buffers the whole primary source** for keyedMap outputs (`HashInMemoryJoinStrategy.Group`). Use `externalMerge` for
  very large inputs.
- **Several ETL transform step failures are per-layer:** step `onError`, field `onError`, then `run.onError`. See
  [Errors](Documentation/ExtractTransformLoad/Errors.md).
- **Synthesized sessions:** the forced-preview funnel does not guarantee a purchase, because removes can empty the cart.
- **Menu numbers change** whenever a command is added (the menu is sorted by name). Documentation refers to commands by name.
- **PGP sources need `gpg` on the PATH and the private key in the keyring.** No encrypted sample is committed (that would mean
  committing a private key); `Sources.md` has the recipe.
- **Authentication is whatever `bleak.Api.Rest` does;** the package is binary-only here, so the scheme was not verified (the mock only
  records that an `Authorization` header was present).

## 5. Decisions still open

- **Purchase request bodies keep the target API's wire-contract names** (`activity`, `subscriber`, `unique_client_id(s)`,
  `identify_person`, `uid`, event `purchased`, and the inconsistent item casing `resourceType` / `productname`) in
  `Commands/PurchaseKnown.cs` and `PurchaseAnonymous.cs`. Renaming breaks use with a real endpoint. Decide whether to keep them (documented
  as the API contract) or make the body shape configurable.
- **`MyAccount` remains in `SynthesizeMyAccountOptins`** (class, folder and menu text). It may be a client term; the events themselves are
  `marketing_opt_in`.
- **API routes `/ver2/{siteId}/activities`, `/subscribers/identify`, `/resources/{id}`** are built in `Program.cs` and the commands and
  are the platform's contract. The mock API mirrors them.
- **Folder `Documentation/` samples contain CRLF and UTF-8 BOM fixtures.** `.gitattributes` marks `Documentation/**/samples/**` as `-text`;
  keep that if you move them.
- **`.gitignore` ignores `*.csv`, `*.jsonl`, `*.xlsx`, `.claude/`, `CLAUDE.md`** with exceptions only under `Documentation/` and
  `**/TestData/**`. Sample data elsewhere needs an explicit `!` rule.
- **Git history:** the repo's initial commit is authored by a personal email address. Confirm that is acceptable before publishing.
- **`bleak.Data.InfiniteDataLoader` references `DataMonger.Common`** (an upward dependency from a library to the app's shared project);
  `CsvLoader` still has a TODO listing the callers to migrate.

## 6. Stale documentation and comments

- `bleak.Data.Transformer/src/bleak.Data.Transformer/ETL-CONFIG.md` says `datetime` emits ISO `"o"` (actual: `yyyy-MM-ddTHH:mm:ssZ`), says externalMerge keys the top
  level on a single integer column (it now uses ordinal string keys and supports composites), and does not mention that `hasHeader` is a
  no-op. `Config/EtlConfig.cs:309` says composite joins are supported only by `hashInMemory`. Prefer
  [Documentation/ExtractTransformLoad](Documentation/ExtractTransformLoad/Readme.md) and reconcile or retire the older files.
- `bleak.Data.Anonymizer/src/bleak.Data.Anonymizer/Config/AnonConfig.cs:26-30` says the output folder must never be the input folder; the engine only rejects an output
  *path* equal to the input path (a same-folder run with the `src_` → `anonymized_` rename works).
- `Commands/ApiBulkRunner.cs` header says it "fires them all at once"; the cap is 1000.
- `ValueConvert.cs:21` says empty/whitespace becomes null; the code checks only `IsNullOrEmpty`, so a whitespace cell stays `" "` and fails
  numeric parses.
- `ETL warnings`: the `record_key` column is derived from join keys (see Errors.md). A keyedMap warning scenario was verified only by reading
  the code.
- The mock API implements none of a real service's behavior (no auth, no schema checks, no deduplication).

## 7. Fixed in this pass (for reference)

- ETL warnings file: the key column is now a generic `record_key` derived from join keys (was a hardcoded `customer_key`); documented
  with tests.
- Config files are found through a per-user config folder chosen on first run (`~/Documents/DataMonger/Config/<Kind>Config`), shared by the
  ETL, Anonymize and Synthesize User Props commands (`ConfigFolders`).
- Generate Email Conversion data / API ⇒ Email Conversion Events: neutral event schema; the "clicked" event was emitted with the bounce
  event name; purchase events had no timestamp; failure messages read the wrong field; the uploader printed no summary; the generator ran per
  site and overwrote its file; optional seed and delay prompt added.
- Known purchases: `test_ext_id` and `loyalty_*` removed (`identified.csv` is now 4 columns); CSV loaders skip blank lines and report
  file/line for short rows.
- Synthesize commands: neutral schema and config keys; opt-in events ignored profile time zones; picklist names were case-sensitive;
  `Random.Shared`/`UtcNow` defeated seeding; culture-dependent birthday parsing; the preview file was created before validating
  `splitEvery`; events could be dated in the future; seed and anchor date added.
