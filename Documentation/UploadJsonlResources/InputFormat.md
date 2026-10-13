# UploadJsonlResources input format

One JSON object per line. The command reads only one property, `resource_id`, to build the URL; the whole line is the `PUT` body.

```json
{"resource_type": "product", "resource_id": "TENT-2P", "name": "Trail 2-Person Tent", "category": "Camping", "price": 199.0, "brand": "Acme", "url": "https://shop.example.com/p/tent-2p"}
```

| Property | Required by the command | Use |
|---|---|---|
| `resource_id` | **Yes** (string, or a scalar that converts to a string such as a number; not null, not blank) | Last segment of the URL, URL-escaped with `Uri.EscapeDataString` |
| `resource_type` | No (not read) | Sent in the body as-is; by convention `product` in the samples |
| anything else | No | Sent in the body as-is |

| Condition | Result |
|---|---|
| Blank line | skipped silently |
| Not a JSON object | `[WARN] Skipping malformed line N: ...`, failed |
| `resource_id` missing/null/blank | `[WARN] Skipping line N: missing 'resource_id'.`, failed |
| `resource_id` with spaces or `/` | accepted; escaped in the URL (`LAMP 1/2` -> `LAMP%201%2F2`) |

Samples: [samples/input/products.jsonl](samples/input/products.jsonl) (10 valid products) and [samples/input/products_bad.jsonl](samples/input/products_bad.jsonl) (line 2 missing id, line 3 blank, line 4 id needing escaping, line 5 truncated JSON, line 6 numeric id, line 1 valid).

See also [Readme.md](Readme.md), [Troubleshooting.md](Troubleshooting.md).
