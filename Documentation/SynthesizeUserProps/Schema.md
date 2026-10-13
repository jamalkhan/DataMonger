# Profile schema (output of Synthesize User Props)

One JSON object per line. The profile describes a customer of the invented retailer **Acme Outfitters** (brands
**Acme** and **Acme Kids**, both configurable in the [`.synthconfig`](SynthConfig.md)). Every value is random but
internally consistent (see [Consistency rules](#consistency-rules)). The same file is the input of
[Synthesize Session Events](../SynthesizeSessionEvents/Readme.md) and
[Synthesize MyAccount Optin Events](../SynthesizeMyAccountOptins/Readme.md); the fields those commands read are listed in
[InputFormat](../SynthesizeSessionEvents/InputFormat.md#profile-input).

A real record is committed at [samples/expected-output/preview_record.json](samples/expected-output/preview_record.json).

## Contents

* [Conventions](#conventions)
* [Top-level fields](#top-level-fields)
* [identifiers[]](#identifiers)
* [addresses[]](#addresses)
* [consent](#consent)
* [preferences](#preferences)
* [order_summary](#order_summary)
* [memberships[]](#memberships)
* [contact](#contact)
* [Consistency rules](#consistency-rules)

## Conventions

* **Source** names where a value comes from: a [`.synthconfig`](SynthConfig.md) key (picklist, probability, name pool)
  or a derivation. "Anchor" is the run's notion of "today": `anchorDate` (UTC midnight) when set, otherwise the real
  current UTC time.
* Dates are `yyyy-MM-dd`. Timestamps are UTC with a `Z` suffix and whole seconds: `yyyy-MM-ddTHH:mm:ssZ`.
* `seq` below is the numeric part of the user id: `startUserSeq` for the first record, then +1 per record.
* All formatting is culture-invariant.

## Top-level fields

| Field | Type | Source | Example |
|---|---|---|---|
| `user_id` | string | `CUST_` + `seq` | `"CUST_1001"` |
| `first_name` | string | one of `firstNames`, uniform | `"Barbara"` |
| `last_name` | string | one of `lastNames`, uniform | `"Turing"` |
| `full_name` | string | `first_name` + space + `last_name` | `"Barbara Turing"` |
| `dob` | date | 20 to 60 years (7300 to 21900 days) before the anchor | `"1977-10-21"` |
| `gender` | string | picklist `gender` | `"F"` |
| `timezone` | string (IANA id) | picklist `timezone` | `"America/Los_Angeles"` |
| `locale` | string | picklist `locale` | `"en-US"` |
| `country` | string | the part of `locale` after the dash, upper-cased (`"US"` when the locale has no dash) | `"US"` |
| `created_at` | timestamp | 30 to 1459 days before the anchor, plus a random time of day | `"2026-01-03T17:41:52Z"` |
| `source_system` | string | constant `acme_synth` | `"acme_synth"` |
| `acquisition_channel` | string | picklist `acquisitionChannel` | `"web"` |
| `is_employee` | boolean | `probabilities.employee` | `false` |
| `is_active` | boolean | `probabilities.active` | `true` |
| `identifiers` | array | see below | |
| `addresses` | array | see below | |
| `consent` | object | see below | |
| `preferences` | object | see below | |
| `order_summary` | object | see below | |
| `memberships` | array | one per configured brand | |
| `contact` | object | flat copy of the contact data and the memberships | |

## identifiers[]

Always exactly two entries, in this order.

| Field | Type | Source | Example |
|---|---|---|---|
| `type` | string | `email`, then `phone` | `"email"` |
| `value` (email) | string | `<first>.<last>+<seq>@<domain>`, lower-cased. Names are reduced to letters and digits (so `O'Brien` becomes `obrien`). `<domain>` is `emailDomainTemplate` with `{lastName}` replaced by the same reduced last name. The `+seq` makes every address unique | `"barbara.turing+1001@turing.example.com"` |
| `value` (phone) | string | `+1212555` followed by a number from `0100` to `0199` (fictional numbers) | `"+12125550152"` |

## addresses[]

One `billing` address, plus a `shipping` address with probability `probabilities.secondAddress`.

| Field | Type | Source | Example |
|---|---|---|---|
| `type` | string | `billing`, then optionally `shipping` | `"billing"` |
| `line1` | string | random number 1 to 9998, a space, and picklist `streetName` | `"3809 Oak Ave"` |
| `city` | string | first part of a `cityRegionPostal` entry | `"Portland"` |
| `region` | string | second part of the same entry | `"OR"` |
| `postal_code` | string | third part of the same entry | `"97201"` |
| `country` | string | the profile's `country` | `"US"` |

City, region and postal code are always taken from one entry, so they agree with each other. The billing and
shipping addresses are drawn independently.

## consent

Marketing consent per channel. Each flag is an independent coin flip.

| Field | Type | Source | Example |
|---|---|---|---|
| `email` | boolean | `probabilities.emailConsent` | `true` |
| `sms` | boolean | `probabilities.smsConsent` | `false` |
| `push` | boolean | `probabilities.pushConsent` | `false` |
| `postal` | boolean | `probabilities.postalConsent` | `true` |

[Synthesize MyAccount Optin Events](../SynthesizeMyAccountOptins/Readme.md) only emits opt-in events on channels whose
flag is `true`.

## preferences

| Field | Type | Source | Example |
|---|---|---|---|
| `favorite_channel` | string | picklist `favoriteChannel` | `"online"` |
| `favorite_category` | string | picklist `favoriteCategory` | `"Outerwear"` |
| `contact_frequency` | string | picklist `contactFrequency` | `"monthly"` |

## order_summary

Counters describing the customer's past orders. With probability `probabilities.hasOrders` the customer has orders;
otherwise the counters are zero or null.

| Field | Type | Source | Example |
|---|---|---|---|
| `order_count` | integer | 1 to 40, or 0 when there are no orders | `22` |
| `first_order_at` | timestamp or null | random instant between `created_at` and the anchor; null when `order_count` is 0 | `"2026-03-17T02:52:00Z"` |
| `last_order_at` | timestamp or null | random instant between `first_order_at` and the anchor (equal to it when `order_count` is 1); null when 0 | `"2026-08-13T11:21:11Z"` |
| `average_order_value` | number | 25.00 to 180.00 (2 decimals); 0 when there are no orders | `31.84` |
| `lifetime_spend` | number | `average_order_value * order_count`, rounded to 2 decimals; 0 when there are no orders | `700.48` |
| `returns_count` | integer | random from 0 to `order_count / 4` (integer division) | `0` |

## memberships[]

One entry per brand in `brands`, in the same order.

| Field | Type | Source | Example |
|---|---|---|---|
| `brand_code` | string | `brands[].code` | `"AC"` |
| `brand_name` | string | `brands[].name` | `"Acme"` |
| `storefront_id` | string | `brands[].storefrontId` | `"acme-us"` |
| `member_id` | string | `M` + `seq` + two-digit brand index (0-based) | `"M100100"` |
| `tier` | string | picklist `membershipTier` (`Bronze`, `Silver` or `Gold`) | `"Bronze"` |
| `previous_tier` | string or null | with probability `probabilities.tierChanged`, the tier one step down or one step up (50/50); null when there was no change or the step would leave the Bronze-Gold range (so never equal to `tier`) | `"Silver"` |
| `points` | integer | derived from the tier: Bronze 0-999, Silver 1000-4999, Gold 5000-14999 | `289` |
| `joined_at` | date | random date between `created_at` and the anchor | `"2026-04-10"` |
| `tier_since` | date | random date between `joined_at` and the anchor's date | `"2026-08-23"` |
| `tier_expires_at` | date | `tier_since` plus one year | `"2027-08-23"` |
| `status` | string | picklist `membershipStatus` | `"active"` |

## contact

A flat section that repeats the primary contact data and one set of prefixed keys per brand. It exists so tools that
want a single flat object (for example a CSV export or a lookup keyed by prefix) do not have to walk the arrays.

| Field | Type | Source | Example |
|---|---|---|---|
| `email` | string | same as the email identifier | `"barbara.turing+1001@turing.example.com"` |
| `phone` | string | same as the phone identifier | `"+12125550152"` |
| `email_domain` | string | the part of the email after `@` | `"turing.example.com"` |
| `<prefix>_member_id` | string | `memberships[].member_id` of that brand; `<prefix>` is `brands[].contactPrefix` | `"ac_member_id": "M100100"` |
| `<prefix>_tier` | string | `memberships[].tier` | `"ac_tier": "Bronze"` |
| `<prefix>_points` | integer | `memberships[].points` | `"ac_points": 289` |
| `<prefix>_status` | string | `memberships[].status` | `"ac_status": "active"` |

With the two example brands the contact therefore has the keys `ac_member_id`, `ac_tier`, `ac_points`, `ac_status`,
`ak_member_id`, `ak_tier`, `ak_points`, `ak_status`. Adding a third brand adds four more keys.

## Consistency rules

The generator guarantees (and the unit tests assert):

* The email contains the customer's name and ends in a domain derived from the last name; emails are unique within a run.
* `full_name` is `first_name` + `last_name`; `country` agrees with `locale`.
* Postal code, region and city come from one picklist entry.
* `created_at` is never after the anchor; `first_order_at` is not before `created_at`; `last_order_at` is not before
  `first_order_at` or after the anchor.
* `lifetime_spend` equals `average_order_value * order_count`; `returns_count` never exceeds `order_count`; when
  `order_count` is 0 the dates are null and the money fields are 0.
* `points` always lie in the range of the membership `tier`; `previous_tier` never equals `tier`;
  `joined_at <= tier_since <= anchor`.
* The `contact` section mirrors the memberships exactly.

## See also

* [Readme.md](Readme.md): how to run the command
* [SynthConfig.md](SynthConfig.md): every config key and picklist
* [Profile input of the event commands](../SynthesizeSessionEvents/InputFormat.md#profile-input)
