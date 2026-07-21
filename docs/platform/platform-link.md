# Platform Link (instance ↔ Control Plane)

Contract for machine-to-machine calls from a Delegat instance to Control Plane.
Product context: [control-plane.md](../architecture/control-plane.md) §9.  
MVP / domains: [../plans/2026-07-21-control-plane-mvp-brief.md](../plans/2026-07-21-control-plane-mvp-brief.md).

**Production CP base URL:** `https://module.delegat.pro`  
**Instance URL pattern:** `https://{label}.module.delegat.pro`

> After `delegat-module-platform` is created, this file moves there as `docs/platform-link.md`; instance repo keeps a stub.

## Environment (injected at provision)

| Variable | Required | Description |
|----------|----------|-------------|
| `CONTROL_PLANE_URL` | for link | Base URL, e.g. `https://module.delegat.pro` (no trailing slash) |
| `INSTANCE_ID` | for link | Public instance id (`inst_…`) |
| `INSTANCE_LINK_SECRET` | for link | Shared secret (backend only; never in frontend) |
| `PLATFORM_CATALOG_REFRESH_MINUTES` | no | Default `360` (6h) |
| `PLATFORM_HEARTBEAT_MINUTES` | no | Default `5`; heartbeat skipped when link is off |

If URL or secret is empty, Platform Link is **disabled**: no CP calls; reference tables stay empty until linked (or a future init wizard).

## Auth (HMAC-SHA256)

Every request to `/platform/v1/*` includes:

| Header | Value |
|--------|--------|
| `X-Instance-Id` | `INSTANCE_ID` |
| `X-Timestamp` | Unix seconds (UTC) |
| `X-Signature` | lowercase hex HMAC-SHA256 |

Canonical string (UTF-8):

```text
METHOD\nPATH\nTIMESTAMP\nBODY_SHA256
```

- `METHOD` - uppercase (`GET`, `POST`)
- `PATH` - path + query as sent (e.g. `/platform/v1/catalog/currencies`)
- `TIMESTAMP` - same as `X-Timestamp`
- `BODY_SHA256` - lowercase hex SHA-256 of raw body; for empty body use SHA-256 of empty bytes

CP rejects if signature invalid, instance unknown/`purged`, or `|now - timestamp| > 300` seconds.

## Control Plane endpoints

Prefix on CP: `/platform/v1`

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/catalog/currencies` | ISO currency catalog |
| `GET` | `/catalog/timezones` | IANA timezone catalog |
| `GET` | `/catalog/plugins` | Adapter plugin registry |
| `POST` | `/errors` | Sampled 5xx / incidents |
| `POST` | `/heartbeat` | Optional liveness + version |

### Catalog DTOs

```json
{ "code": "USD", "label": "US Dollar", "symbol": "$" }
```

```json
{ "id": "Europe/Moscow", "label": "Moscow (UTC+3)" }
```

```json
{
  "key": "ozon",
  "displayName": "Ozon",
  "latestVersion": "1.2.0",
  "minPlatformVersion": "0.0.0",
  "artifactUrl": null,
  "changelog": null
}
```

List responses: JSON array of items (or `{ "items": [ ... ] }`).

### Errors body (instance → CP)

```json
{
  "instanceId": "inst_…",
  "occurredAt": "2026-07-21T12:00:00Z",
  "statusCode": 500,
  "path": "/products",
  "method": "GET",
  "message": "…",
  "exceptionType": "NullReferenceException",
  "fingerprint": "sha256-prefix",
  "platformVersion": "1.2.3"
}
```

Do **not** send catalog PII or marketplace credentials.

## Instance behavior

1. On startup (and on refresh interval), if link is enabled, pull catalogs and **upsert** into MySQL:
   - `referenceCurrencies`, `referenceTimezones`, `referencePlugins`
2. Instance API serves UI from DB only:
   - `GET /reference/currencies`
   - `GET /reference/timezones`
   - `GET /reference/plugins`
   - `GET /reference/status` - `{ linked, lastSyncedAt, currenciesCount, timezonesCount, pluginsCount, ready }`
3. Empty DB + no CP → empty lists; UI empty state (no frontend hardcode).
4. Future **initialization wizard** uses `/reference/status` and these catalogs for first-run store setup.

## Crypto choice

HMAC-SHA256 as above (not instance JWT). Rotation: Ops → new secret → redeploy env.
