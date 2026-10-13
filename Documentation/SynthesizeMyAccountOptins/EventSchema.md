# Opt-in event schema

Reference for the `marketing_opt_in` events written by [Synthesize MyAccount Optin Events](Readme.md). One event is one
JSON object per line. Real examples: [samples/expected-output/](samples/expected-output/).

## Contents

* [Fields](#fields)
* [When an event is generated](#when-an-event-is-generated)
* [Choosing the channel and the contact](#choosing-the-channel-and-the-contact)
* [Time-window logic](#time-window-logic)
* [Probabilities and ranges](#probabilities-and-ranges)

## Fields

| Field | Type | Source | Example |
|---|---|---|---|
| `schema_version` | string | constant `1.0` | `"1.0"` |
| `event_id` | string | `evt-` + 8 random lowercase hex digits | `"evt-053cd989"` |
| `event_type` | string | constant `marketing_opt_in` | `"marketing_opt_in"` |
| `event_time` | string | the moment of consent, ISO 8601 with the profile's UTC offset: `yyyy-MM-ddTHH:mm:sszzz` | `"2026-06-01T00:58:15-07:00"` |
| `user_id` | string | the profile's `user_id` | `"CUST_1001"` |
| `email` | string | the profile's email | `"barbara.turing+1001@turing.example.com"` |
| `storefront_id` | string | the profile's first membership `storefront_id`, else `acme-us` | `"acme-us"` |
| `source_system` | string | constant `account_service` | `"account_service"` |
| `consent.channel` | string | one of `email`, `sms`, `push`, `postal`; see below | `"postal"` |
| `consent.contact_value` | string | the contact the opt-in refers to; see below | `"3809 Oak Ave, Portland, OR 97201"` |
| `consent.status` | string | constant `subscribed` | `"subscribed"` |
| `consent.subscribed_at` | string | same instant and format as `event_time` | `"2026-06-01T00:58:15-07:00"` |
| `consent.opt_source` | string | uniform from `preference_center`, `checkout`, `email_signup` | `"checkout"` |
| `consent.page_source` | string | uniform from `account_page`, `footer_form`, `order_confirmation` | `"account_page"` |

## When an event is generated

A profile produces at most one event. It is skipped when any of these hold:

* the profile has no resolvable email, or no `user_id`;
* the 70% roll fails;
* the profile has no consented channel (every `consent.*` flag is `false`).

A profile with no `consent` object at all is treated as consenting to `email` only. See
[InputFormat.md](InputFormat.md).

## Choosing the channel and the contact

The channel is picked uniformly from the profile's channels whose `consent.<channel>` flag is `true`, so an opt-in never
contradicts the profile's own consent. `consent.contact_value` depends on the channel, and falls back to the email when the
profile lacks the needed field:

| Channel | `contact_value` |
|---|---|
| `email` | the email |
| `sms` | the phone identifier (`identifiers[]` of type `phone`, else `contact.phone`) |
| `postal` | `line1, city, region postal_code` of the `billing` address (else the first address) |
| `push` | the `user_id` |

## Time-window logic

* The consent moment is a random local day within the window below, at a random second of that local day, in the
  profile's time zone (`timezone`; missing, blank or unknown ids use `America/New_York`). The UTC offset of the local
  midnight is used for the whole day.
* **Window:** from 0 up to **1460 days (4 years)** before the anchor ("today"). If the profile has a `created_at`, the
  window is shortened to the profile's age in whole days, so the opt-in is never dated before the customer existed
  (it can be at most a day earlier because of the time-of-day draw).
* The result is **clamped to the anchor**, so an opt-in drawn for "today" never lies in the future.
* `event_time` and `consent.subscribed_at` are always identical.

## Probabilities and ranges

| Item | Value |
|---|---|
| Opt-in roll per eligible profile | 70% |
| Days before the anchor | 0 to 1460 (limited by `created_at`) |
| Channel | uniform among consented channels |
| `opt_source` | uniform over 3 values |
| `page_source` | uniform over 3 values |

## See also

* [Readme.md](Readme.md), [InputFormat.md](InputFormat.md)
* [Session event schema](../SynthesizeSessionEvents/EventSchema.md)
