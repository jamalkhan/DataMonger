# Input formats

[Synthesize Session Events](Readme.md) reads two JSONL inputs: a **profile file** (the output of
[Synthesize User Props](../SynthesizeUserProps/Readme.md), or anything shaped like it) and a **product feed**.
[Synthesize MyAccount Optin Events](../SynthesizeMyAccountOptins/Readme.md) reads the same profile file.

Both inputs are UTF-8 text with one JSON value per line. Blank lines are ignored.

## Contents

* [Profile input](#profile-input)
* [Product feed input](#product-feed-input)

## Profile input

One JSON object per line. The commands read only the fields below and ignore everything else, so the full output of
Synthesize User Props works, and so do much smaller hand-written profiles
([samples/profiles.jsonl](samples/profiles.jsonl) mixes both). Every accessor is tolerant: a missing or wrongly typed
field is treated as absent, never as an error. A line that is not a JSON object is skipped with
`[WARN] Skipping line N: not a JSON object.`

| Field | Type | Used for | If absent |
|---|---|---|---|
| `identifiers[]` with `type` `email` and a `value` | array of objects | The email of every event (first matching entry wins) | falls back to `contact.email`, then to a top-level `email`; with none of these the profile produces **no events** |
| `identifiers[]` with `type` `phone` and a `value` | array of objects | `consent.contact_value` of an `sms` opt-in | falls back to `contact.phone`, then to the email |
| `user_id` | string | `user_id` on every event; a profile **without** it still gets session events (`user_id: null`) but never an opt-in event | session events carry `null`; no opt-in |
| `timezone` | string (IANA id) | The offset of every `event_time` and the local day/time of the session | `America/New_York` (also for unknown ids). Read from the **top level** only |
| `locale` | string | `locale` on session events | `en-US` |
| `country` | string | The currency: `CA` gives `CAD`, anything else `USD` | the region of `locale` |
| `memberships[]` with `storefront_id` | array of objects | `storefront_id` of every event (first membership that has one) | `acme-us` |
| `consent.email`, `consent.sms`, `consent.push`, `consent.postal` | booleans | The channel of an opt-in event: only channels set to `true` qualify | no `consent` object: only `email` qualifies |
| `created_at` | ISO 8601 timestamp | Opt-in events are never dated before it | the full 4-year window |

Notes:

* The earlier layout that wrapped everything in a `subscriber` object is **not** read; the profile is the line itself.
* A line must be a single JSON object; pretty-printed JSON spread over several lines is not supported.
* The complete field list of generated profiles is in [Schema.md](../SynthesizeUserProps/Schema.md).

Minimal valid profile:

```json
{"user_id":"CUST_9","timezone":"Asia/Tokyo","identifiers":[{"type":"email","value":"ada@example.com"}]}
```

## Product feed input

One product variant (one **sku**) per line, in a flat object. See [samples/products.jsonl](samples/products.jsonl) (12
usable rows plus three rows that demonstrate what is skipped).

| Key | Type | Required | Default | Meaning |
|---|---|---|---|---|
| `sku` | string | yes | | Unique variant id. A repeated sku is skipped (the first row wins). |
| `name` | string | yes | | Product name. |
| `price` | number | yes | | Current price, 0 or more. A string such as `"18.0"` is **not** accepted. |
| `product_id` | string | no | the sku | Groups variants (size/color) of one product. |
| `brand` | string | no | derived from `url` | `Acme`, `Acme Kids`, or any other text. |
| `category` | string | no | empty | One value, or several separated by `\|` (one is picked per view). |
| `subcategory` | string | no | empty | Same rule as `category`. |
| `color` | string | no | empty | |
| `size` | string | no | empty | |
| `list_price` | number | no | `price` | Regular price (shown next to a discounted `price`). |
| `url` | string | no | empty | Product page; also used to derive the brand. |
| `image_url` | string | no | empty | |
| `in_stock` | boolean | no | `true` | `false` removes the row from the catalog; any other value (or absence) keeps it. |

**Brand from the URL.** When a row has no `brand` the brand is `Acme Kids` if the url's host contains a label equal to
`kids` (`kids.acme.example.com`) or its first path segment is `kids` (`www.acme.example.com/kids/boots`); otherwise
`Acme` (also for an empty or unparseable url). Matching is case-insensitive. A non-empty `brand` always wins.

**What is skipped.** On loading, the command counts and reports: *invalid* rows (not JSON, not an object, or missing
`sku`/`name`/numeric `price`, or a negative price), *duplicate* skus, and *out-of-stock* rows. If no usable row remains
the command stops with `No purchasable products found in <path> (rows need sku, name and a numeric price).`

**Where the file is looked up.** The prompt lists `*.jsonl` files under `LocalData/jsonl/products` (found by walking up
from the current directory and the app folder), or you type a path.

Example rows:

```json
{"sku":"TE-200-WHT-M","product_id":"TE-200","name":"Acme Everyday Tee","category":"Tops","subcategory":"T-Shirts","color":"White","size":"M","price":18.0,"url":"https://www.acme.example.com/p/te-200","image_url":"https://img.acme.example.com/te-200-white.jpg"}
{"sku":"KB-300-RED-2","product_id":"KB-300","name":"Acme Kids Rain Boot","category":"Footwear|Kids","subcategory":"Boots","color":"Red","size":"2","price":34.99,"list_price":44.99,"url":"https://kids.acme.example.com/p/kb-300"}
```
