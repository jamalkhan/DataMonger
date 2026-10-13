# UploadJsonlIdentifies input format

One JSON object per line (JSON Lines, `.jsonl`). The command forwards each line verbatim as the request body of `POST /ver2/{SiteId}/subscribers/identify`.

```json
{"user_id": "1001", "email": "ada.lovelace@example.com", "first_name": "Ada", "last_name": "Lovelace", "phone": "555-0100", "brand": "Acme Kids", "marketing_opt_in": true}
```

## What the command checks

| Check | Result |
|---|---|
| Blank line | skipped silently |
| Not a JSON object (`JObject.Parse` fails) | not sent, counted failed, `[WARN] Skipping malformed line N: ...` (console only) |
| First non-blank line not valid JSON | `[FAIL] First line is not valid JSON` and the file is aborted |
| Required fields | **none**: no field is required or inspected by the command |

## Field mapping

The whole line becomes the body; nothing is copied to the URL, headers or elsewhere. Field names, types and order are not changed. Which fields the real service requires is outside the tool; the mock accepts any JSON object.

The sample records use these invented fields: `user_id`, `email`, `first_name`, `last_name`, `phone` (`555-01XX`), `brand` (`Acme` or `Acme Kids`), `marketing_opt_in` (boolean).

## Sample files

* [samples/input/identifies.jsonl](samples/input/identifies.jsonl): 10 users.
* [samples/input/identifies_bad.jsonl](samples/input/identifies_bad.jsonl): line 2 is truncated JSON.

See also [Readme.md](Readme.md), [Troubleshooting.md](Troubleshooting.md).
