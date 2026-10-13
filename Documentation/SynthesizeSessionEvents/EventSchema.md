# Session event schema

Reference for the events written by [Synthesize Session Events](Readme.md): the common fields, each event type, how
the events of one session are sequenced and timed, and every probability. Real examples are in
[samples/expected-output/](samples/expected-output/) (the whole output in `session_events.jsonl`, and the first
customer's session as pretty-printed files in `preview-session/`).

## Contents

* [Common fields](#common-fields)
* [page_view](#page_view)
* [add_to_cart and remove_from_cart](#add_to_cart-and-remove_from_cart)
* [purchase](#purchase)
* [Sequencing logic](#sequencing-logic)
* [Time-window logic](#time-window-logic)
* [Probabilities and ranges](#probabilities-and-ranges)
* [Identifiers](#identifiers)

## Common fields

Every event is one JSON object with these fields first, in this order.

| Field | Type | Source | Example |
|---|---|---|---|
| `schema_version` | string | constant `1.0` | `"1.0"` |
| `event_id` | string | `evt-` + 8 random lowercase hex digits | `"evt-3ca8ff77"` |
| `event_type` | string | `page_view`, `add_to_cart`, `remove_from_cart` or `purchase` | `"page_view"` |
| `event_time` | string | ISO 8601 with the profile's UTC offset, whole seconds: `yyyy-MM-ddTHH:mm:sszzz` | `"2026-09-18T15:51:45-07:00"` |
| `session_id` | string | `sess-` + 8 random hex digits; the same for every event of the profile's session | `"sess-f69562e1"` |
| `user_id` | string or null | the profile's `user_id`; JSON `null` when the profile has none | `"CUST_1001"` |
| `email` | string | the profile's email (see [InputFormat](InputFormat.md#profile-input)) | `"barbara.turing+1001@turing.example.com"` |
| `storefront_id` | string | the profile's first membership `storefront_id`, else `acme-us` | `"acme-us"` |
| `channel` | string | one of `desktop`, `mobile_web`, `mobile_app`, picked once per session | `"mobile_app"` |
| `locale` | string | the profile's `locale`, else `en-US` | `"en-US"` |
| `currency` | string | `CAD` when the profile's country (its `country`, else the region of its locale) is `CA`, otherwise `USD` | `"USD"` |
| `source_system` | string | constant `web_store` | `"web_store"` |

The event-specific fields follow.

## page_view

A customer viewing a product page. Adds `page` and `product`.

| Field | Type | Source | Example |
|---|---|---|---|
| `page.type` | string | constant `product_detail` | `"product_detail"` |
| `page.url` | string | the product's `url` (empty string when the feed has none) | `"https://www.acme.example.com/p/te-200"` |
| `product.sku` | string | feed `sku` | `"TE-200-WHT-L"` |
| `product.product_id` | string | feed `product_id`, default the sku | `"TE-200"` |
| `product.name` | string | feed `name` | `"Acme Everyday Tee"` |
| `product.brand` | string | feed `brand`, else derived from the url (`Acme Kids` for a `kids` host or first path segment, otherwise `Acme`) | `"Acme"` |
| `product.category` | string | feed `category`; if it holds several values separated by `\|` one is picked at random | `"Tops"` |
| `product.subcategory` | string | feed `subcategory`, same rule | `"T-Shirts"` |
| `product.color` | string | feed `color` (empty when absent) | `"White"` |
| `product.size` | string | feed `size` (empty when absent) | `"L"` |
| `product.price` | number | feed `price` | `18.0` |
| `product.list_price` | number | feed `list_price`, default `price` | `18.0` |
| `product.url` | string | feed `url` | `"https://www.acme.example.com/p/te-200"` |
| `product.image_url` | string | feed `image_url` | `"https://img.acme.example.com/te-200-white.jpg"` |
| `product.rating` | number | random, 3.00 to 5.00 (2 decimals) | `3.05` |
| `product.review_count` | integer | random, 0 to 1999 | `960` |

## add_to_cart and remove_from_cart

Both carry the **cart as it is after the event**: for `add_to_cart` it includes the line just added (or the line whose
quantity was bumped), for `remove_from_cart` it no longer includes the removed line (and can be an empty array).

| Field | Type | Source | Example |
|---|---|---|---|
| `items` | array | the cart lines, in the order they were added | |
| `items[].sku` | string | product sku | `"TE-200-WHT-L"` |
| `items[].product_id` | string | product id | `"TE-200"` |
| `items[].name` | string | product name | `"Acme Everyday Tee"` |
| `items[].brand` | string | product brand | `"Acme"` |
| `items[].category` | string | product category | `"Tops"` |
| `items[].color` | string | product color | `"White"` |
| `items[].size` | string | product size | `"L"` |
| `items[].quantity` | integer | line quantity (starts at 1, 2 or 3; see [probabilities](#probabilities-and-ranges); bumps add 1) | `2` |
| `items[].unit_price` | number | product price | `18.0` |
| `items[].line_total` | number | `unit_price * quantity` | `36.0` |
| `item_count` | integer | sum of the quantities | `2` |
| `cart_total` | number | sum of the `line_total` values | `36.0` |

## purchase

At most one per session, always the last event. It buys a random **subset** of the cart lines (1 to 5 lines, never
more than the cart holds); the cart is not rewritten afterwards.

| Field | Type | Source | Example |
|---|---|---|---|
| `order.order_id` | string | `ORD-` + 7 random characters from `A-Z0-9` | `"ORD-Z1Q7GV0"` |
| `order.item_count` | integer | sum of the purchased quantities | `1` |
| `order.subtotal` | number | sum of the purchased `line_total` values | `18.0` |
| `order.discount` | number | with a promo code: subtotal times a random 10% to 20% (2 decimals); otherwise 0 | `0` |
| `order.shipping` | number | 0 for store pickup, 0 when the subtotal is at least 50, otherwise a random 5.99 to 7.99 | `6.87` |
| `order.tax` | number | `(subtotal - discount)` times a random 6% to 9% (2 decimals) | `1.15` |
| `order.total` | number | `subtotal - discount + shipping + tax`, rounded to 2 decimals | `26.02` |
| `order.currency` | string | same as the top-level `currency` | `"USD"` |
| `order.payment_method` | string | uniform from `card`, `debit`, `wallet`, `gift_card` | `"debit_card"` |
| `order.shipping_method` | string | `store_pickup` for pickup orders; otherwise `free_shipping` when `shipping` is 0, else `standard` | `"standard"` |
| `order.fulfillment` | string | `pickup` or `ship` | `"ship"` |
| `order.promo_codes` | string[] | `["WELCOME10"]` when a promo applies, otherwise `[]` | `[]` |
| `items[]` | array | the purchased lines; fields as in the cart events (`sku`, `product_id`, `name`, `brand`, `category`, `color`, `size`, `quantity`, `unit_price`, `line_total`) | |

Consistency: `subtotal` equals the sum of `items[].line_total`; `total = subtotal - discount + shipping + tax`.

## Sequencing logic

Implemented by `SessionEventsBuilder.BuildForProfile`. For one profile:

1. **No email, no session.** If the profile has no resolvable email, nothing is generated. Otherwise draw the number of
   page views `V` (0 to 10; for the forced preview 2 to 10). With `V = 0` (and not forced) there is no session.
2. Draw the number of adds `A` (0 to 5; forced: 1 to 5) and, if `A > 0`, the number of removes `R` (0 to 2).
3. Build a queue of `V - 1` "view" tokens, `A` "add" tokens and `R` "remove" tokens and **shuffle** it
   (Fisher-Yates, using the seeded generator).
4. **Event #1 is always a `page_view`** (a random product from the catalog, added to the "viewed" pool).
5. Repeatedly take the **first feasible token** of the queue: a "view" or "add" is always feasible; a "remove" only if
   the cart is not empty (an infeasible "remove" stays in the queue and a later token is taken). If only "remove" tokens
   are left and the cart is empty they are discarded.
   * view: a new random product is viewed (the same product can be viewed twice).
   * add: with an empty cart, or when there are viewed products not yet in the cart and a 60% roll succeeds, a random
     **viewed, not-in-cart** product is added as a new line with a random quantity; otherwise the quantity of a random
     existing line is raised by one. So an `add_to_cart` item has always been viewed earlier in the same session.
   * remove: a random cart line is removed entirely, whatever its quantity.
6. After the queue is drained, if `A > 0`, the cart is not empty, and (forced, or a 30% roll succeeds) a single
   `purchase` event is appended for a random subset of the cart. Because removes can empty the cart, even a forced session
   occasionally ends without a purchase.

Resulting guarantees (asserted by the unit tests): first event is a `page_view`; every cart item was viewed earlier;
`remove_from_cart` never fires on an empty cart; a `purchase`, if present, is the last event and contains only items that
are in the cart at that time.

## Time-window logic

Implemented by `EventSession`.

* **Time zone.** The profile's `timezone` (an IANA id). Missing, blank or unknown ids use `America/New_York`.
* **Start.** The session starts on one of the last 14 local days before the anchor ("today" in that time zone): a random
  day offset 0 to 13 and a random second of the local day. If that would push the 45-minute window past the anchor
  instant, the session is moved back one day. So **no event is ever later than the anchor** and the first event is at most
  14 days before it. (The offset is that of local midnight; a session after a daylight-saving switch within that day
  keeps the midnight offset.)
* **Window.** All events fall within **45 minutes** of the first event. Event #1 is at the start time; every later event
  advances the clock by a random 10 to 299 seconds, clamped to the end of the window (so a very long session can end with
  several events sharing the last instant).
* **Order.** Events are written in generation order, which equals chronological order.
* **Expression.** `event_time` carries the profile's offset (for example `-07:00`, `+09:00`, `+05:30`).

## Probabilities and ranges

| Item | Value |
|---|---|
| Page views per session | 0 to 10 (forced preview: 2 to 10); 1 in 11 sessions is empty |
| Adds / removes per session | 0 to 5 adds (forced: 1 to 5); 0 to 2 removes when there is at least one add |
| Add picks a new line (when possible) | 60% (40% bumps an existing line by 1) |
| New line quantity | 70% one, 25% two, 5% three |
| Purchase | 30% when eligible (always for the forced preview) |
| Lines bought | 1 to min(5, cart lines), random subset |
| Store pickup | 20% of purchases |
| Promo code (`WELCOME10`) | 20% of non-pickup purchases; discount 10% to 20% |
| Shipping | free for pickup or subtotal >= 50, else 5.99 to 7.99 |
| Tax | 6% to 9% of the discounted subtotal |
| Gap between events | 10 to 299 seconds |
| Session window | 45 minutes |
| Session start | within the last 14 local days |
| Channel | uniform over `desktop`, `mobile_web`, `mobile_app` |

## Identifiers

`event_id` and `session_id` come from the seeded generator, so a seeded run reproduces them. They are random, not
guaranteed globally unique (32 bits), which is fine for test data.

## See also

* [Readme.md](Readme.md), [InputFormat.md](InputFormat.md)
* [Opt-in event schema](../SynthesizeMyAccountOptins/EventSchema.md)
