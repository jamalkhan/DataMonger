# ApiBulkRunner input format (`massive_apis.jsonl`)

Location: `LocalData/massive_apis.jsonl`. JSON Lines; each non-blank line is an object describing one call.

```json
{"verb": "PUT", "url": "ver2/{site}/resources/TENT-2P", "payload": {"resource_type": "product", "resource_id": "TENT-2P", "price": 199.0}}
```

## Properties

### url

Required. String. Relative to the current site's `BaseUrl`, or absolute.

| | |
|---|---|
| Type | string |
| Required | yes (missing/blank: `[WARN] Skipping line N: missing 'url'.`) |
| Default | none |

Processing: the token `{site}` is replaced by the site's `SiteId`; if the result parses as an absolute URI it is used as-is; otherwise it is appended to `BaseUrl` (`BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/')`). **Caveat on macOS/Linux:** a value beginning with `/` parses as an absolute `file://` URI and fails (see [Readme](Readme.md#remarks)); omit the leading slash.

### verb

| | |
|---|---|
| Type | string |
| Required | no |
| Default | `POST` |
| Allowed values | members of the HTTP client's `HttpVerbs` enum, case-insensitive (verified with `GET`, `POST`, `PUT`, `DELETE`). Unknown value: `[WARN] Skipping line N: unknown verb '<x>'.` |

Note that a numeric string such as `"1"` may also satisfy `Enum.TryParse`; do not rely on it.

### payload

| | |
|---|---|
| Type | JSON object/array/scalar, or a string, or absent/null |
| Required | no |
| Default | empty body |

An object/array/number is compacted (`Formatting.None`) and sent as the body. A JSON **string** is sent as its text unchanged (use it to send exact bytes, as in sample line 5). `null` or absent sends an empty body. The payload is sent for every verb, including GET.

## Line handling

| Line | Result |
|---|---|
| blank | ignored |
| not a JSON object (`JObject.Parse` error) | `[WARN] Skipping malformed line N: <message>` |
| missing `url` / unknown `verb` | warning, skipped |
| valid | queued as `L<N> <VERB>` |

Skipped lines are not counted as failures; the totals reflect only valid calls.

## Sample

[samples/input/massive_apis.jsonl](samples/input/massive_apis.jsonl), 11 lines: lines 1-7 valid (GET ping; POST activities; POST identify; PUT resource with an object payload; PUT resource with a string payload; a line with no `verb`, which defaults to POST; a DELETE with an absolute URL), line 8 unknown verb, line 9 missing url, line 10 not JSON, line 11 a valid GET to a route the mock does not have (404).

See also [Readme.md](Readme.md), [Troubleshooting.md](Troubleshooting.md).
