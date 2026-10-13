# Samples: Synthesize User Props

Everything here is invented (the retailer "Acme Outfitters", people with famous computer-science names, `example.com`
addresses, fictional 555-01xx phone numbers).

## Files

| File | Purpose |
|---|---|
| [acme.synthconfig](acme.synthconfig) | The example config, identical to the one the app ships in `DataMonger/data/synthconfig/` and seeds into your config folder on first run. Brands `Acme` and `Acme Kids`, 12 records, `seed` 42, `anchorDate` 2026-10-01, `splitEvery` 5 (so 12 records make 3 files). Every key is commented. |
| [expected-output/preview_record.json](expected-output/preview_record.json) | The pretty-printed preview record (record #1, `CUST_1001`). |
| [expected-output/acme_profiles_1.jsonl](expected-output/acme_profiles_1.jsonl), [_2](expected-output/acme_profiles_2.jsonl), [_3](expected-output/acme_profiles_3.jsonl) | The 12 records, split 5 + 5 + 2. |

The real output files have a run timestamp in their names (`acme_profiles_<yyyyMMdd_HHmmss>_1.jsonl`); the committed
copies have it removed.

## Reproduce

1. Build the app and create a scratch folder (the app wants a `LocalData/products.csv` at startup even though this
   command does not use it):

   ```sh
   dotnet build DataMonger.slnx
   mkdir -p work/LocalData && cd work
   printf 'Id,ResourceType,Price,Quantity,ProductName,Category,Subcategory,Size,Url\n' > LocalData/products.csv
   ```

2. Run the command with a private home folder (`DATAMONGER_HOME`) so nothing touches your real Documents folder, and
   pipe the answers:

   ```sh
   export DATAMONGER_HOME="$PWD/home"
   printf '14\n\n1\n\nout/profiles\ny\n' | dotnet ../DataMonger/bin/Debug/net10.0/DataMonger.dll
   ```

   | Line | Answers |
   |---|---|
   | `14` | the menu number of "Synthesize User Props (fake data)" (check the menu; it depends on the installed commands) |
   | empty | first-run prompt `Config folder [...]:`, accepts `$DATAMONGER_HOME/Config/SynthConfig`, which is created and seeded with `acme.synthconfig` |
   | `1` | the first (only) config in the list |
   | empty | `Number of customers to synthesize [12]:`, keeps 12 |
   | `out/profiles` | output folder |
   | `y` | generate the full set |

   On later runs the folder is remembered, so omit the empty line for the first-run prompt.

3. Compare:

   ```sh
   for n in 1 2 3; do diff out/profiles/acme_profiles_*_$n.jsonl ../Documentation/SynthesizeUserProps/samples/expected-output/acme_profiles_$n.jsonl; done
   ```

   No difference is expected. To run the sample config directly instead of the seeded copy, answer prompt 2 with a path
   (`../Documentation/SynthesizeUserProps/samples/acme.synthconfig`).

## What a run prints

```
Config          : <home>/Config/SynthConfig/acme.synthconfig
Records         : 12
Brands          : AC, AK
Seed            : 42
Anchor (now)    : 2026-10-01 00:00:00Z
Output folder   : out/profiles
Split every     : 5 records
Run timestamp   : <yyyyMMdd_HHmmss>

Preview record written: out/profiles/acme_profiles_<timestamp>.json

Continue and generate the full set of 12 record(s) as .jsonl? [y/N]: === Done ===
Synthesized: 12 records across 3 file(s):
    out/profiles/acme_profiles_<timestamp>_1.jsonl
    out/profiles/acme_profiles_<timestamp>_2.jsonl
    out/profiles/acme_profiles_<timestamp>_3.jsonl
```

## What to look for

* `CUST_1001`..`CUST_1012` in order; `acme_profiles_1.jsonl` starts with exactly the record in `preview_record.json`
  (compacted).
* Each profile has two `memberships` (Acme, Acme Kids) and eight prefixed keys in `contact` (`ac_*`, `ak_*`) that repeat
  the same tier, points and status.
* Points agree with the tier (Gold 5000 or more, Bronze below 1000); `previous_tier` is never the same as `tier`.
* Customers with `order_summary.order_count` 0 have null order dates and zero spend.
* Change `seed` (or the `anchorDate`) and re-run: the content changes; keep both and it does not.
* Try an invalid config (for example `"splitEvery": 0`) to see `[FAIL] Invalid config ...` and that no file is created.

## Using the output

The profiles are the input of the other two synthesizers; see the samples of
[Synthesize Session Events](../../SynthesizeSessionEvents/samples/README.md) and
[Synthesize MyAccount Optin Events](../../SynthesizeMyAccountOptins/samples/README.md).
