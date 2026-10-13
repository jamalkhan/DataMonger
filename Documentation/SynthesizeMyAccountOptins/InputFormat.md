# Input format

[Synthesize MyAccount Optin Events](Readme.md) reads a profile file: UTF-8 text, one JSON object per line, as written
by [Synthesize User Props](../SynthesizeUserProps/Readme.md) (field reference: [Schema.md](../SynthesizeUserProps/Schema.md)).
Blank lines are ignored; a line that is not a JSON object is skipped with a warning.

Only these fields are read; everything else is ignored, so hand-written minimal profiles work too
([samples/profiles.jsonl](samples/profiles.jsonl)). A missing or wrongly typed field is treated as absent.

| Field | Type | Used for | If absent |
|---|---|---|---|
| `identifiers[]` (`type` `email`, `value`) | array | `email`; also decides whether an event can be generated at all | `contact.email`, then top-level `email`; with none the profile is skipped |
| `identifiers[]` (`type` `phone`, `value`) | array | `contact_value` of an `sms` opt-in | `contact.phone`, then the email |
| `user_id` | string | `user_id`; required | the profile is skipped |
| `timezone` | string (IANA id), top level | The UTC offset and local day of the event | `America/New_York` |
| `consent.email` / `sms` / `push` / `postal` | booleans | Which channels may be chosen: those that are `true` | no `consent` object: `email` only; all `false`: skipped |
| `addresses[]` | array | `contact_value` of a `postal` opt-in (billing address, else the first) | the email |
| `memberships[].storefront_id` | string | `storefront_id` (first membership that has one) | `acme-us` |
| `created_at` | ISO 8601 timestamp | Opt-ins are never dated before it | the full 4-year window |

The full list that the session-event command reads is in
[Synthesize Session Events: InputFormat](../SynthesizeSessionEvents/InputFormat.md#profile-input).

Minimal profile that can produce an opt-in:

```json
{"user_id":"CUST_9","timezone":"Asia/Tokyo","identifiers":[{"type":"email","value":"ada@example.com"}]}
```
