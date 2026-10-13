# UploadJsonlEvents input format

The command reads **JSON Lines** files: UTF-8 text, one JSON value per line, `.jsonl` extension (other extensions are ignored when you give a folder; a single file typed by path is read whatever its extension).

## Line shape

Each line must be a complete JSON **object**. The command does not interpret it; it forwards the line text as the HTTP body. By convention for the event ("activity") API the object wraps the event in an `activity` property:

```json
{"activity": {"user_id": "1001", "event_name": "add_to_cart", "event_id": "evt-0002", "timestamp": "2026-03-01T10:02:00Z", "brand": "Acme", "properties": {"sku": "TENT-2P", "quantity": 1, "price": 199.0}}}
```

### Validation performed by the command

| Check | Behavior |
|---|---|
| Blank / whitespace-only line | Skipped silently; not counted as success or failure. Still included in the `of N total` and progress totals. |
| Line is not valid JSON, or is valid JSON but not an object (`JObject.Parse`) | Not sent; counted as failed; `[WARN] Skipping malformed line N: <parser message>`; logged with `status=n/a`. |
| First non-blank line, preview mode | Same parse check, but failure aborts the whole file (`[FAIL] First line is not valid JSON: ...`). |
| First non-blank line, folder mode | Not parsed locally; sent as-is. |
| Required fields | **None checked.** There is no check for `activity`, `user_id`, `event_name`, timestamps, etc. Field validation is entirely the API's job; the mock API accepts any JSON body. |

### Field mapping

| Record content | Goes to |
|---|---|
| Whole line | request body (unchanged, including key order and whitespace) |
| Any field | nothing in the URL; the URL is `{BaseUrl}/ver2/{SiteId}/activities` for every line |

Line numbers in messages are 1-based file line numbers (blank lines count).

## Sample files

| File | Purpose |
|---|---|
| [samples/input/events_a.jsonl](samples/input/events_a.jsonl) | 10 valid events (`page_viewed`, `add_to_cart`, `purchase`) for 4 users |
| [samples/input/events_bad.jsonl](samples/input/events_bad.jsonl) | line 1 truncated JSON, line 3 blank, line 4 not JSON, lines 2 and 5 valid |

## See also

[Readme.md](Readme.md), [Troubleshooting.md](Troubleshooting.md)
