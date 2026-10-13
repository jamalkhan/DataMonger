# EmailConversion.jsonl event schema

Reference for the file written by [Generate Email Conversion data](Readme.md) and read by
[API => Email Conversion Events](../ApiEmailConversionEvents/Readme.md). One JSON object per line (JSON Lines). Real
example: [samples/expected-output/EmailConversion.jsonl](samples/expected-output/EmailConversion.jsonl).

## Row

```jsonc
{ "recipient": { "user_id": "1003", "first_name": "Grace", "last_name": "Hopper", "email": "grace.hopper@example.com" },
  "events": [ { /* event */ }, { /* event */ } ] }
```

| Field | Type | Description |
|---|---|---|
| `recipient.user_id` | string | `user_id` column of `identified.csv` |
| `recipient.first_name`, `recipient.last_name` | string | name columns |
| `recipient.email` | string | email column; the uploader uses it as the label in failure messages |
| `events` | array of event | at least `email_sent`; ordered by `event_time` (non-decreasing) |

## Event: common fields

Present on **every** event, in this order:

| Field | Type | Description |
|---|---|---|
| `event_id` | string | random GUID-formatted id, unique per event (not an RFC 4122 v4 value; derived from the generator's random source, so it is reproducible with a seed) |
| `event_type` | string | one of `email_sent`, `email_delivered`, `email_opened`, `email_clicked`, `email_bounced`, `purchase` |
| `event_time` | string | UTC, ISO 8601, millisecond precision, e.g. `2025-01-14T17:21:29.463Z` |
| `recipient_email` | string | same as `recipient.email` |
| `user_id` | string | same as `recipient.user_id` |
| `message_id` | string | `msg-` + 16 lowercase hex characters; identical for all events of one row |
| `campaign` | object | see below |

### campaign

| Field | Type | Description |
|---|---|---|
| `id` | integer | 5001 to 5008 |
| `name` | string | `<slug>-<yyyyMMdd>`, e.g. `wardrobe-refresh-20250115` |
| `brand` | string | `Acme` or `Acme Kids` |
| `category` | string | category of the campaign's featured product |
| `subject` | string | subject line |
| `preheader` | string | preheader text |

Pools: see [Readme](Readme.md#campaign-pool).

## Event-specific fields

Appended after the common fields.

| `event_type` | Extra fields | When |
|---|---|---|
| `email_sent` | none | always the first event |
| `email_delivered` | none | 95% of rows; U(0, 30 min) after sent |
| `email_bounced` | `bounce_type` (`hard`/`soft`), `bounce_reason` (string) | 5% of rows; terminal (no later events) |
| `email_opened` | `device_type` (`desktop`/`mobile`/`tablet`) | after delivery; may repeat |
| `email_clicked` | `link_url` (string), `device_type` | 0 to 2 minutes after an open; `link_url` is the featured product URL plus `utm_source=email&utm_medium=email&utm_campaign=<campaign.name>` (joined with `?`, or `&` if the URL already has a query) |
| `purchase` | `order` (object) | 40% of clicked rows, up to 10 minutes after the click |

### order (on `purchase`)

| Field | Type | Description |
|---|---|---|
| `order_id` | string | `ORD-` + 9-digit number |
| `currency` | string | always `USD` |
| `total` | number | sum of `unit_price * quantity` over `items` |
| `items` | array | 1 to 3 distinct products: the clicked product first, then 0 to 2 others |

Each item:

| Field | Type | Source column of `products.csv` |
|---|---|---|
| `sku` | string | `id` (1) |
| `product_name` | string | `product_name` (5) |
| `category` | string | `category` (6) |
| `subcategory` | string | `subcategory` (7) |
| `unit_price` | number | `price` (3) |
| `quantity` | integer | `quantity` (4), taken from the file, not randomized |
| `size` | string | `size` (8) |
| `url` | string | `url` (9) |

## Example event (click)

```json
{"event_id":"17cd1130-9116-15ae-7198-6f385cf44b23","event_type":"email_clicked","event_time":"2025-01-13T14:05:46.521Z",
 "recipient_email":"grace.hopper@example.com","user_id":"1003","message_id":"msg-f97c4ccaf3ed0360",
 "campaign":{"id":5001,"name":"spring-arrivals-20250115","brand":"Acme","category":"Bottoms",
 "subject":"New Spring Arrivals: 30% Off","preheader":"Refresh your wardrobe with this season's must-haves"},
 "link_url":"https://shop.example.com/p/2006?utm_source=email&utm_medium=email&utm_campaign=spring-arrivals-20250115",
 "device_type":"mobile"}
```
(Wrapped for readability; in the file it is compact JSON inside `events`. Fields appear in the order the code writes
them; `device_type` follows `link_url` on a click.)

## See also

[Readme](Readme.md) | [API => Email Conversion Events](../ApiEmailConversionEvents/Readme.md)
