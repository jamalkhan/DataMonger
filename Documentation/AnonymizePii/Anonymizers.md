# Anonymizer methods

Reference for every built-in value `method` can take in a [column rule](AnonConfig.md#columns). Part of the
[Anonymize PII](Readme.md) command. Method names are matched case-insensitively; option names are case-sensitive.

## Contents

* [Overview](#overview)
* [Group semantics](#group-semantics)
* [Determinism](#determinism)
* [simpsonsName](#simpsonsname)
* [simpsonsEmail](#simpsonsemail)
* [fakePhone](#fakephone)
* [last4](#last4)
* [randomHex](#randomhex)
* [fakeDate](#fakedate)
* [guid](#guid)
* [fakeAddress](#fakeaddress)
* [constant](#constant)
* [Choosing a method](#choosing-a-method)

## Overview

| Method | Produces | Options | Uses `group` |
|---|---|---|---|
| [`simpsonsName`](#simpsonsname) | A first, middle, last or full name of a cartoon character | `part`, `group` | Yes (`name:` namespace) |
| [`simpsonsEmail`](#simpsonsemail) | `first.last##@domain` | `domain`, `group` | Yes (shares the `name:` namespace) |
| [`fakePhone`](#fakephone) | A 555-01XX style number | `format` | No |
| [`last4`](#last4) | A zero-padded random digit string | `digits` | No |
| [`randomHex`](#randomhex) | A lowercase hex string | `length` | No |
| [`fakeDate`](#fakedate) | A random date | `format`, `minYear`, `maxYear` | No |
| [`guid`](#guid) | A random GUID | `format` | No |
| [`fakeAddress`](#fakeaddress) | A Springfield-themed address part | `part`, `group` | Yes (`addr:` namespace) |
| [`constant`](#constant) | A fixed literal | `value` | No |

Common behavior for all of them:

* The original cell value is **ignored**: fakes are generated from the random stream, never derived from the
  source value, so the output cannot be reversed to the original.
* The anonymizer is only called for non-empty cells unless the rule says
  [`whenEmpty: generate`](AnonConfig.md#whenempty).
* Values are random **per occurrence**. The same source value in two cells gets two different fakes.
* The fake pools are deliberately recognizable: names and e-mail local parts come from about 55 recurring
  characters of a well-known cartoon sitcom, phone numbers are in the fictional 555-01XX block, addresses live on
  fictional streets, and the default e-mail domain is `springfield.example`. A human glancing at the data sees it
  is fake.
* Output text is written as-is and then CSV-quoted by the writer if needed, so a fake never breaks the file layout.

## Group semantics

Fakes are independent per cell, which would make a row like `first_name=Bart, last_name=Burns,
full_name=Lisa Hutz, email=moe.flanders3@...` internally incoherent. A `group` ties columns of **one row**
together.

* `group` is a string option. Any non-string value (a number, for example) is ignored, meaning "no group".
* The first rule in the row that needs the group creates the shared value (a *persona* for names and e-mail, an
  *address* for `fakeAddress`); later rules with the same group in the same row reuse it.
* State is cleared at the start of every row, so groups never carry over between rows. Row 1 and row 2 get
  different people even with the same group name.
* Name and e-mail share one namespace (`name:<group>`); addresses use another (`addr:<group>`). So `group:
  "person"` on a name column and `group: "person"` on an address column do **not** interfere.
* A persona has a first name, a middle name (a second random character's first name), a last name and
  full name = first + space + last. `part` selects which piece a column shows.
* A rule without `group` draws a fresh independent value (not shared with anything).
* If the first column of the group is empty and `passthrough`, nothing is created at that point; the persona is
  created by the next non-empty column of the group. Empty cells therefore never perturb coherence.

Real output from the [basic sample](samples/README.md#1-basic-everything-in-one-run) (seed 42), row 1001. The
source row is `Ada,Lovelace,Ada Lovelace,ada.lovelace@example.com,...,12 Analytical Way,,London,NY,10001`:

```text
first_name  last_name  full_name    email                              address_line1       city        state  postal_code
Lionel      Hutz       Lionel Hutz  lionel.hutz13@springfield.example  7243 Third Street  Shelbyville  FL     33158
```

Name, full name and e-mail agree with each other; street, city, state and postal code form one address. Row 1004
has an empty e-mail: the name columns are still faked and the e-mail stays empty.

Without a group (the [all-methods sample](samples/README.md#3-all-methods)), the same row shows independent values,
for example `Lionel,Homer,Selma Bouvier,lisa.simpson7@mail.example`.

## Determinism

All randomness comes from one `System.Random` per run, created in the engine:

* `run.seed` set: `new Random(seed)`. Output is reproducible, byte for byte, for the same config, inputs and library
  build. Verified by running the [basic sample](samples/README.md) twice and diffing the output folders (identical).
* `run.seed` not set: `new Random()`. Every run differs.

Things that change the generated values of a seeded run, because they change how many numbers are drawn and in
what order: reordering `files` or `columns`, adding or removing rules or files, editing `options` that alter draw
counts (for example setting `domain`, which skips the domain pick), changing which source cells are empty (empty
`passthrough` cells draw nothing), or switching a rule to `whenEmpty: generate`. Draws are made in file order,
row order, rule order.

The `Random` algorithm is part of .NET; a seed is reproducible within a runtime version but is not guaranteed
across major .NET versions. Treat a seed as "reproducible for this build", not as a permanent contract.

---

## simpsonsName

A name drawn from a pool of recurring cartoon characters (Homer Simpson, Ned Flanders, Moe Szyslak, Montgomery
Burns, and so on).

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |
| **Default** | `part: "full"` |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `part` | string | `full` | `first`, `middle`, `last`, `full` (case-insensitive; any other value behaves as `full`) | Which piece of the persona to output. |
| `group` | string | none | any string | Share one persona with other name/e-mail rules in the same row. |

**Remarks.** `middle` is another character's first name (so `Lionel Homer Hutz`); it is useful for middle-name
columns. `full` is `first last` (for example `Milhouse Van Houten`; last names can contain spaces). Roughly 55
distinct characters, so duplicates are common in large files; that is by design, not a collision bug. Names are
plain ASCII.

**Example**

```jsonc
{ "name": "first_name", "method": "simpsonsName", "options": { "part": "first", "group": "person" } },
{ "name": "last_name",  "method": "simpsonsName", "options": { "part": "last",  "group": "person" } },
{ "name": "full_name",  "method": "simpsonsName", "options": { "group": "person" } }
// -> Bart | Simpson | Bart Simpson   (one person per row)
```

**See also:** [Group semantics](#group-semantics), [simpsonsEmail](#simpsonsemail).

## simpsonsEmail

A fake e-mail address `<first>.<last><1-99>@<domain>`.

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `domain` | string | random pick from [`run.emailDomains`](AnonConfig.md#emaildomains) (default `springfield.example`) | any string | Fixed domain for this column. |
| `group` | string | none | any string | Base the local part on the row's shared persona so it matches the name columns. |

**Remarks.** The local part is the persona's first and last name lowercased, keeping only letters and digits, with
spaces turned into dots (`Milhouse Van Houten` becomes `milhouse.van.houten`), followed by a random number from 1
to 99 (so `groundskeeper.willie13`). Without `group`, a new persona is used, so the address will not relate to any
name column. The fake domain is under a reserved or user-chosen domain so no mail can leak to real people. An empty
`emailDomains` list falls back to `springfield.example`.

**Example**

```jsonc
{ "name": "email", "method": "simpsonsEmail", "options": { "group": "person" } }
// -> lionel.hutz13@springfield.example

{ "name": "email", "method": "simpsonsEmail", "options": { "domain": "mail.example" } }
// -> lisa.simpson7@mail.example
```

**See also:** [run.emailDomains](AnonConfig.md#emaildomains), [Group semantics](#group-semantics).

## fakePhone

A phone number in the 555-0100 to 555-0199 range reserved for fiction.

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `format` | string | `digits` | `digits`, `dashes` (case-insensitive; anything else behaves as `digits`) | Output shape. |

**Remarks.** Always ten digits, the area code and exchange are fixed at `555-555` and the last four are
`0100`..`0199` (100 possible numbers). Not influenced by the original's formatting; if the source column mixes
formats the output is uniform. Does not use `group`.

**Example**

```jsonc
{ "name": "phone", "method": "fakePhone" }                              // -> 5555550124
{ "name": "phone", "method": "fakePhone", "options": { "format": "dashes" } }  // -> 555-555-0116
```

## last4

A zero-padded random number, intended for "last four digits of a card or phone" columns.

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `digits` | integer (or numeric string) | `4` | 1..18 (values outside are clamped) | Output length. |

**Remarks.** The number is drawn below 10^min(digits, 9) and left-padded with zeros to `digits` characters. For up
to 9 digits every value is possible; for 10 to 18 digits only the last 9 positions are random and the front is
zero-filled (`digits: 12` gives `000338369840`). Use more than 9 digits only when length matters more than
entropy. Output keeps leading zeros, so it is a string, not a number.

**Example**

```jsonc
{ "name": "card_last4", "method": "last4" }                      // -> 0637
{ "name": "card_last4", "method": "last4", "options": { "digits": 6 } }   // -> 934516
```

## randomHex

A random lowercase hexadecimal string.

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `length` | integer (or numeric string) | `64` | 1..1024 (clamped) | Number of hex characters. |

**Remarks.** The default of 64 characters matches a SHA-256 digest, so it is a drop-in replacement for hashed-email
columns while making sure the original cannot be fingerprinted (the output is unrelated to the input; this is not a
hash). The same input value gets different outputs in different rows, so hashes cannot be used to join across
rows or files after anonymization. Use `length: 32` for MD5-shaped and `40` for SHA-1-shaped columns.

**Example**

```jsonc
{ "name": "email_sha256", "method": "randomHex" }                     // -> 56480d96...870d00 (64 chars)
{ "name": "email_sha256", "method": "randomHex", "options": { "length": 16 } }  // -> ba68a3f343d64137
```

## fakeDate

A random calendar date, for birth dates and other sensitive dates.

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `format` | string | `yyyy-MM-dd` | any [.NET custom date/time format](https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings) | Output format, formatted with the invariant culture. |
| `minYear` | integer | `1940` | 1..9999 | First year of the range (starts 1 January). |
| `maxYear` | integer | `2005` | 1..9999 | Last year of the range (ends 31 December). If lower than `minYear` it is raised to `minYear`. |

**Remarks.** A day is picked uniformly between 1 January `minYear` and 31 December `maxYear`. A year outside
1..9999 (for example `minYear: 0`) or an invalid `format` throws for every cell, which is how the
[bad-cell samples](samples/README.md#4-bad-cell-skip-vs-abort) exercise [`onError`](AnonConfig.md#onerror). The
output is text, so a date column's original format is not inferred: set `format` to match the source file.
Single-letter standard specifiers work per .NET rules with the invariant culture (`d` gives `05/02/1962`; an unsupported one such as `i` throws `Input string was not in a correct format` for every cell). An empty `format` string (`""`) is accepted as given and yields the general format, `05/02/1962 00:00:00`.

**Example**

```jsonc
{ "name": "dob", "method": "fakeDate" }                                   // -> 1957-05-01
{ "name": "dob", "method": "fakeDate",
  "options": { "format": "MM/dd/yyyy", "minYear": 1990, "maxYear": 1995 } } // -> 01/21/1994
```

## guid

A fresh random GUID.

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `format` | string | `d` | `d` (lowercase, dashed), `n` (32 hex digits, no dashes), `upper` (uppercase, dashed); other values behave as `d` | Output shape. |

**Remarks.** The 16 bytes come from the run's random generator (not from `Guid.NewGuid()`), so with a `seed` the
GUIDs are reproducible. They are not RFC 4122 version-4 GUIDs (the version and variant bits are not set); they
only look like GUIDs. Replacing a key column with `guid` will break joins to other files; prefer leaving keys
unchanged.

**Example**

```jsonc
{ "name": "session_id", "method": "guid" }                              // -> 1344ee4c-8159-7fb1-20fd-1bc64f45b556
{ "name": "customer_id", "method": "guid", "options": { "format": "n" } } // -> 57e5163446a7c7870091e0819dee3c18
{ "name": "token", "method": "guid", "options": { "format": "upper" } }   // -> 1D90234E-715B-4C16-1EEC-0A94FEFA74D3
```

## fakeAddress

One piece of a coherent, Springfield-themed mailing address.

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |
| **Default** | `part: "line1"` |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `part` | string | `line1` | `line1` (alias `street`), `line2`, `line3`, `city`, `state`, `province`, `postal` (aliases `postalCode`, `zip`), `postalExt`; case-insensitive; unknown values behave as `line1` | Which part to output. |
| `group` | string | none | any string | Share one address with other `fakeAddress` rules in the same row. |

**Remarks.** An address is: a street number 1..9998 plus one of nine invented/show streets (`7243 Third Street`),
one of six fictional towns (`Springfield`, `Shelbyville`, `Ogdenville`, `North Haverbrook`, `Capital City`,
`Cypress Creek`), a state from `NT OH OR CA IL NY TX FL WA MA`, a Canadian province from `ON QC BC AB MB NS`, a
five-digit postal code 10000..99998 (never with a leading zero) and a four-digit `postalExt` (`0000`..`9998`).
`line2` and `line3` are always the empty string (there is no apartment or suite concept), so use them to blank
those columns out; with the default `passthrough`, empty source cells stay empty anyway. Use `state` for a US
style column and `province` for a Canadian one. With `group`, all parts of the row's address agree; without it,
every column gets an independent random address, so `city` and `state` will not belong together.

**Example**

```jsonc
{ "name": "address_line1", "method": "fakeAddress", "options": { "part": "line1",  "group": "home" } },
{ "name": "city",          "method": "fakeAddress", "options": { "part": "city",   "group": "home" } },
{ "name": "state",         "method": "fakeAddress", "options": { "part": "state",  "group": "home" } },
{ "name": "postal_code",   "method": "fakeAddress", "options": { "part": "postal", "group": "home" } }
// -> 7243 Third Street | Shelbyville | FL | 33158
```

**See also:** [Group semantics](#group-semantics).

## constant

Replaces the cell with a fixed literal.

| | |
|---|---|
| **Type** | string result |
| **Required options** | None |

### Options

| Option | Type | Default | Allowed values | Meaning |
|---|---|---|---|---|
| `value` | string | empty string | any string | The text to write. |

**Remarks.** Use it to blank a column (omit `value`: every non-empty cell becomes empty), to set a fixed domain or area code, or to redact free text with a marker such as
`[redacted]`. With the default `passthrough`, empty cells stay empty rather than becoming the constant; add
`whenEmpty: generate` to fill those too. Only string values are read: a number such as `"value": 5` is ignored (use
`"5"`).

**Example**

```jsonc
{ "name": "notes", "method": "constant", "whenEmpty": "generate", "options": { "value": "[redacted]" } }
```

---

## Choosing a method

| Column holds | Use |
|---|---|
| Given name / family name / middle name / display name | `simpsonsName` with `part` and a shared `group` |
| E-mail address | `simpsonsEmail` (same `group` as the name columns) |
| Phone | `fakePhone` |
| Birth date, signup date, other dates | `fakeDate` with a matching `format` |
| Street, city, state/province, postal code | `fakeAddress` with one `group` |
| Hashed identifier (MD5/SHA-1/SHA-256 of e-mail etc.) | `randomHex` with matching `length` |
| Card or phone "last four" | `last4` |
| Opaque ids you want to destroy | `guid` (breaks joins) |
| Free text, notes, fixed flags | `constant` |
| Join keys (`customer_id`, order ids) | Leave out of the config so they stay joinable |

Custom methods: when you use the library directly, implement `IAnonymizer` (a `Key` and a
`Generate(original, ctx, options)`) and call `AnonymizerRegistry.Register(...)` before `AnonymizeEngine.Run`.
The console command always uses the built-in registry, so custom methods cannot be selected from it.
