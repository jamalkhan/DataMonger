# DataMonger.MockApi

A tiny in-memory HTTP service that mimics the three endpoints the API commands call, so every API sample in
this documentation can be run locally with no credentials.

It accepts well-formed JSON, records every request, and can inject failures, throttling and latency.

## Running it

```sh
dotnet run --project DataMonger.MockApi                        # listens on http://localhost:5280
dotnet run --project DataMonger.MockApi -- --urls http://localhost:5290
```

Then point a site at it in `appsettings.json`:

```json
{ "Sites": [ { "BaseUrl": "http://localhost:5280", "SiteId": "demo", "Username": "demo", "Password": "REPLACE_WITH_PASSWORD" } ] }
```

## Endpoints

| Method | Path | Used by | Success response |
|---|---|---|---|
| `POST` | `/ver2/{siteId}/activities` | [UploadJsonlEvents](../UploadJsonlEvents/Readme.md), [PurchaseAnonymous](../PurchaseAnonymous/Readme.md), [PurchaseKnown](../PurchaseKnown/Readme.md), [ApiEmailConversionEvents](../ApiEmailConversionEvents/Readme.md) | `202` `{"status":"accepted","requestId":n,"route":"activities"}` |
| `POST` | `/ver2/{siteId}/subscribers/identify` | [UploadJsonlIdentifies](../UploadJsonlIdentifies/Readme.md), [PurchaseKnown](../PurchaseKnown/Readme.md) | `202` |
| `PUT` | `/ver2/{siteId}/resources/{resourceId}` | [UploadJsonlResources](../UploadJsonlResources/Readme.md) | `200` |
| any | `/ver2/{siteId}/ping` | [ApiBulkRunner](../ApiBulkRunner/Readme.md) (any verb) | `200`/`202`; body optional |
| `GET` | `/mock/requests` | You | Every request received so far (method, path, status, parsed JSON body) |
| `GET` | `/mock/stats` | You | Count per route and status |
| `DELETE` | `/mock/requests` | You | Clears the recorded requests and resets the counter |
| `GET` | `/mock/health` | You | `{"status":"ok"}` |

Request bodies must be valid JSON (an empty or malformed body returns `400`). Authentication is **not**
enforced; whether an `Authorization` header was present is recorded as `hadAuthorizationHeader`.

## Settings

Set in `DataMonger.MockApi/appsettings.json`, as environment variables (`MockApi__FailEveryNth=5`), or on the
command line (`--MockApi:FailEveryNth=5`).

| Setting | Default | Description |
|---|---|---|
| `Urls` | `http://localhost:5280` | Listen address (or `--urls`). |
| `MockApi:FailEveryNth` | `0` | Return `500` for every Nth request. `0` disables. |
| `MockApi:ThrottleEveryNth` | `0` | Return `429` with `Retry-After: 1` for every Nth request. `0` disables. Throttling is checked before failure injection. |
| `MockApi:LatencyMs` | `0` | Delay added to every request. |
| `MockApi:RequestLogPath` | *(unset)* | If set, each request is also appended to this file as one JSON line. |

## Inspecting traffic

```sh
curl -s http://localhost:5280/mock/stats
curl -s http://localhost:5280/mock/requests | head -40
curl -s -X DELETE http://localhost:5280/mock/requests
```

## Not a real service

The mock validates nothing about payload contents and implements none of a real service's behavior (no auth, no
deduplication, no schema checks). It exists to let you exercise DataMonger's request building, parallelism, retry
and failure handling.
