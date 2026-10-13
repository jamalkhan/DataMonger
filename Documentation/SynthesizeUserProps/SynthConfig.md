# .synthconfig reference

The `.synthconfig` file is the only input of [Synthesize User Props](Readme.md). It is JSON, deserialized by
`SynthConfigLoader` into `SynthConfig`. Comments (`//`, `/* */`) and trailing commas are allowed. Key names are
matched case-insensitively (`recordCount`, `RecordCount` and `RECORDCOUNT` are the same key), and so are the picklist
names. Unknown keys are ignored. A complete, commented example ships with the app as
`DataMonger/data/synthconfig/acme.synthconfig` and is committed unchanged as [samples/acme.synthconfig](samples/acme.synthconfig).

The config is **validated before anything is written** (see [Validation](#validation)); a bad config stops the run
with a list of every problem and no output file is created.

## Contents

* [Quick reference](#quick-reference)
* [Run settings](#run-settings): `recordCount`, `seed`, `anchorDate`, `startUserSeq`, `fileBaseName`, `splitEvery`
* [Names and email](#names-and-email): `firstNames`, `lastNames`, `emailDomainTemplate`
* [brands](#brands): `code`, `name`, `storefrontId`, `contactPrefix`
* [probabilities](#probabilities)
* [picklists](#picklists)
* [Validation](#validation)
* [Reproducible runs](#reproducible-runs)

## Quick reference

| Key | Type | Required | Default |
|---|---|---|---|
| `recordCount` | integer | no | `10` |
| `seed` | integer | no | none (random) |
| `anchorDate` | string `yyyy-MM-dd` | no | the real current UTC date and time |
| `startUserSeq` | integer | no | `1001` |
| `fileBaseName` | string | no | `profiles_synth` |
| `splitEvery` | integer | no | `1000000` |
| `firstNames` | string[] | yes (at least one) | empty |
| `lastNames` | string[] | yes (at least one) | empty |
| `emailDomainTemplate` | string | no | `{lastName}.example.com` |
| `brands` | object[] | yes (at least one) | empty |
| `probabilities` | object | no | every probability at its default |
| `picklists` | object of string[] | yes (all 11 names below) | empty |

## Run settings

### recordCount

How many customers to generate.

| | |
|---|---|
| **Type** | integer |
| **Required** | no |
| **Default** | `10` |
| **Allowed values** | 1 or more |

**Remarks.** Only the default of the first prompt: the command asks `Number of customers to synthesize [N]:` and
whatever you type there wins (empty keeps this value; a non-positive or non-numeric answer prints a warning and keeps
this value).

**Example**

```jsonc
"recordCount": 12
```

**See also:** [splitEvery](#splitevery)

### seed

Seed of the random number generator.

| | |
|---|---|
| **Type** | integer (32-bit) |
| **Required** | no |
| **Default** | none: a different random seed every run |
| **Allowed values** | any `int`, negative values included |

**Remarks.** Every random choice in the run (names, picklists, coin flips, dates) comes from one generator created from
this seed, so the same seed, the same config and the same [anchorDate](#anchordate) always produce byte-identical
records. Without `anchorDate`, "today" is the real clock, so a seeded run is only reproducible within the same UTC day.
The command prints a `[note]` when you set `seed` without `anchorDate`.

**Example**

```jsonc
"seed": 42
```

**See also:** [anchorDate](#anchordate), [Reproducible runs](#reproducible-runs)

### anchorDate

The date the generator treats as "today".

| | |
|---|---|
| **Type** | string, `yyyy-MM-dd` |
| **Required** | no |
| **Default** | the real current UTC date and time |
| **Allowed values** | a calendar date in exactly that format (culture-independent); anything else is a validation error |

**Remarks.** Used as UTC midnight of that date. All relative dates (birth date, `created_at`, order dates,
membership dates) are measured back from it, and none is after it. Set it together with [seed](#seed) for fully
reproducible output; leave it out to get profiles that look "recent" on the day you run.

**Example**

```jsonc
"anchorDate": "2026-10-01"
```

**See also:** [seed](#seed)

### startUserSeq

Number of the first customer.

| | |
|---|---|
| **Type** | integer |
| **Required** | no |
| **Default** | `1001` |
| **Allowed values** | any integer (use a positive one) |

**Remarks.** The first record is `CUST_<startUserSeq>`, the next `CUST_<startUserSeq + 1>`, and so on. The number also
appears in the email (`+<seq>`) and in the member ids. Change it to generate a second batch with ids that do not
collide with the first.

**Example**

```jsonc
"startUserSeq": 5001
```

### fileBaseName

Prefix of the output file names.

| | |
|---|---|
| **Type** | string |
| **Required** | no |
| **Default** | `profiles_synth` |
| **Allowed values** | any non-blank string that is valid in a file name |

**Remarks.** Files are `<fileBaseName>_<timestamp>.json` (preview), and `<fileBaseName>_<timestamp>.jsonl` or
`<fileBaseName>_<timestamp>_<n>.jsonl` (data). See [Outputs](Readme.md#outputs).

**Example**

```jsonc
"fileBaseName": "acme_profiles"
```

### splitEvery

Maximum number of records per output file.

| | |
|---|---|
| **Type** | integer (64-bit) |
| **Required** | no |
| **Default** | `1000000` |
| **Allowed values** | 1 or more; 0 or negative is a validation error |

**Remarks.** When more records are generated than fit in one file the output rolls to the next file
(`_1`, `_2`, ...). Use a small value to keep files manageable for upload tools, or to demonstrate splitting (the sample
uses 5 so 12 records give 3 files). A run that fits in one file gets no `_<n>` suffix.

**Example**

```jsonc
"splitEvery": 5000
```

## Names and email

### firstNames

First-name pool.

| | |
|---|---|
| **Type** | string[] |
| **Required** | yes |
| **Default** | empty (invalid) |
| **Allowed values** | at least one name |

**Remarks.** One name is picked uniformly at random per customer, independently of the last name, so the number of
distinct customers is `firstNames x lastNames`. Names are reduced to letters and digits when used in an email.

**Example**

```jsonc
"firstNames": ["Ada", "Alan", "Grace"]
```

**See also:** [lastNames](#lastnames), [emailDomainTemplate](#emaildomaintemplate)

### lastNames

Last-name pool.

| | |
|---|---|
| **Type** | string[] |
| **Required** | yes |
| **Default** | empty (invalid) |
| **Allowed values** | at least one name |

**Remarks.** See [firstNames](#firstnames). The picked last name also supplies the email domain through
`emailDomainTemplate`.

**Example**

```jsonc
"lastNames": ["Lovelace", "Turing", "Hopper"]
```

### emailDomainTemplate

Template of the email domain.

| | |
|---|---|
| **Type** | string |
| **Required** | no |
| **Default** | `{lastName}.example.com` |
| **Allowed values** | any non-blank string; the token `{lastName}` (case-insensitive) is replaced |

**Remarks.** `{lastName}` becomes the picked last name reduced to letters and digits; the whole address is
lower-cased. The address is `<first>.<last>+<seq>@<domain>`. Keep the domain under `example.com` (or another reserved
test domain) so no real mailbox is ever addressed.

**Example**

```jsonc
"emailDomainTemplate": "{lastName}.example.com"   // ada.lovelace+1001@lovelace.example.com
```

## brands

The brands each customer is a member of. Every customer gets one membership per brand, plus a set of prefixed keys in
the flat [`contact`](Schema.md#contact) section.

| | |
|---|---|
| **Type** | object[] |
| **Required** | yes (at least one) |
| **Default** | empty (invalid) |

**Example**

```jsonc
"brands": [
  { "code": "AC", "name": "Acme",      "storefrontId": "acme-us",      "contactPrefix": "ac" },
  { "code": "AK", "name": "Acme Kids", "storefrontId": "acme-kids-us", "contactPrefix": "ak" }
]
```

| Key | Type | Required | Meaning |
|---|---|---|---|
| `code` | string | yes | Short brand code. Written to `memberships[].brand_code`. Unique (case-insensitive). |
| `name` | string | yes | Display name. Written to `memberships[].brand_name`. |
| `storefrontId` | string | yes | Storefront the brand sells through. Written to `memberships[].storefront_id`; the event commands copy the first membership's value onto every event as `storefront_id`. |
| `contactPrefix` | string | yes | Prefix of the flat contact keys (`<prefix>_tier`, ...). Unique (case-insensitive). Use lower case and letters, digits or underscores. |

**Remarks.** The order of `brands` is the order of `memberships`; the first brand is the one the event commands use for
`storefront_id`.

## probabilities

Chance (0 to 1) that each yes/no field is `true`. The whole object and every key in it are optional.

| Key | Default | Controls |
|---|---|---|
| `employee` | `0.05` | `is_employee` |
| `active` | `0.90` | `is_active` |
| `emailConsent` | `0.80` | `consent.email` |
| `smsConsent` | `0.50` | `consent.sms` |
| `pushConsent` | `0.50` | `consent.push` |
| `postalConsent` | `0.60` | `consent.postal` |
| `hasOrders` | `0.85` | Whether `order_summary` has orders (otherwise zeros and nulls) |
| `secondAddress` | `0.40` | Whether a `shipping` address is added to the `billing` one |
| `tierChanged` | `0.30` | Whether a membership has a `previous_tier` (a step to a neighbouring tier; see [Schema](Schema.md#memberships)) |

**Allowed values:** a number from 0 to 1 inclusive. `0` means never, `1` means always. Anything else is a validation error.

**Remarks.** Each draw is independent, so for example a customer can be `is_active: false` and still have orders.

**Example**

```jsonc
"probabilities": { "emailConsent": 0.95, "smsConsent": 0.10, "hasOrders": 1 }
```

## picklists

An object that maps a picklist name to a pool of string values. Each time the generator needs one of these fields it
picks one value from the pool **uniformly at random**, so **repeating a value weights it**: `["Bronze", "Bronze",
"Silver", "Gold"]` gives Bronze about half of the time. Picklist names are matched case-insensitively. Extra picklists
are ignored. A picklist that is missing, empty, or contains a blank value is a validation error.

| Picklist | Used for | Constraints | Example values |
|---|---|---|---|
| `acquisitionChannel` | `acquisition_channel` | none | `web`, `store`, `mobile_app`, `referral` |
| `gender` | `gender` | none | `F`, `M`, `X` |
| `timezone` | `timezone` | IANA ids; the event commands fall back to `America/New_York` for an id the machine does not know | `America/Chicago` |
| `locale` | `locale`, and `country` (the part after the dash) | use `language-REGION` | `en-US`, `en-CA` |
| `streetName` | `addresses[].line1` (after a random house number) | none | `Maple St` |
| `cityRegionPostal` | `addresses[].city`, `region`, `postal_code` | every entry must have exactly three parts separated by `\|`: `City\|Region\|PostalCode` | `Portland\|OR\|97201` |
| `membershipTier` | `memberships[].tier` | every value must be `Bronze`, `Silver` or `Gold` (case-insensitive); drives `points` | `Bronze` |
| `membershipStatus` | `memberships[].status` | none | `active`, `lapsed`, `suspended` |
| `favoriteChannel` | `preferences.favorite_channel` | none | `online`, `store` |
| `favoriteCategory` | `preferences.favorite_category` | none | `Outerwear`, `Footwear` |
| `contactFrequency` | `preferences.contact_frequency` | none | `daily`, `weekly`, `monthly` |

**Example**

```jsonc
"picklists": {
  "membershipTier": ["Bronze", "Bronze", "Bronze", "Silver", "Silver", "Gold"],   // weighted 3:2:1
  "cityRegionPostal": ["Portland|OR|97201", "Austin|TX|73301"]
}
```

**Remarks.** Because lookups are case-insensitive, `"Gender"` and `"gender"` name the same picklist. If the file
contains two keys that differ only in case, the later one wins.

## Validation

`SynthConfigLoader.FromFile` parses and validates in one step and throws `SynthConfigException` that carries every
problem found. The command prints:

```
[FAIL] Invalid config <path>:
  - splitEvery must be at least 1.
  - brand contactPrefix 'ac' is used more than once.
```

Rules checked:

| Rule | Message (abbreviated) |
|---|---|
| JSON must parse (comments and trailing commas allowed) | `Could not parse JSON: ...` |
| `recordCount >= 1` | `recordCount must be at least 1.` |
| `splitEvery >= 1` | `splitEvery must be at least 1.` |
| `fileBaseName` not blank | `fileBaseName must not be blank.` |
| `anchorDate` is `yyyy-MM-dd` | `anchorDate '...' is not a yyyy-MM-dd date.` |
| `firstNames`, `lastNames` not empty | `firstNames must contain at least one name.` |
| `emailDomainTemplate` not blank | `emailDomainTemplate must not be blank.` |
| at least one brand; `code`, `name`, `storefrontId`, `contactPrefix` not blank | `brands must contain at least one brand.` / `brands[0].storefrontId must not be blank.` |
| brand `code` and `contactPrefix` unique (case-insensitive) | `brand code 'AC' is used more than once.` |
| every probability in 0..1 | `probabilities.EmailConsent must be between 0 and 1 (got 1.5).` |
| all 11 picklists present, non-empty, no blank values | `picklist 'gender' is missing or empty.` |
| `cityRegionPostal` entries have 3 parts | `picklist 'cityRegionPostal' entry '...' must look like 'City\|Region\|PostalCode'.` |
| `membershipTier` values are Bronze, Silver or Gold | `picklist 'membershipTier' value 'Platinum' is not one of Bronze, Silver, Gold.` |

Validation happens before the preview file is created, so a bad config (for example `splitEvery: 0`) leaves no files
behind.

## Reproducible runs

For byte-identical output across runs and machines set both `seed` and `anchorDate` and keep the rest of the config
unchanged. The sample run in [samples/README.md](samples/README.md) does exactly that, and the committed
[expected-output](samples/expected-output/) matches what the command writes (apart from the timestamp in the file
names). Changing any picklist, name pool, probability or the brand list changes the output, because every pool size
affects how the random numbers are consumed.
