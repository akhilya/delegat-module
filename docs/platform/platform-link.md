# Platform Link — instance notes (stub)

**Contract canon** (HMAC, `/platform/v1/*`, catalog shapes, SSO exchange) lives in the Control Plane repo:

- Local: `C:\Users\ARTEM\projects\delegat-module-platform\docs\platform-link.md`
- Product context: `…/docs/control-plane.md`

This file keeps **instance-side** behavior only (client in `backend/Services/PlatformLink`).

## Environment (injected at provision)

| Variable | Required | Description |
|----------|----------|-------------|
| `CONTROL_PLANE_URL` | for link | CP base URL (no trailing slash) |
| `INSTANCE_ID` | for link | `inst_{guid:N}` |
| `INSTANCE_LINK_SECRET` | for link | Shared secret (backend only) |
| `PLATFORM_CATALOG_REFRESH_MINUTES` | no | Default `360` |
| `PLATFORM_HEARTBEAT_MINUTES` | no | Default `5` |

If URL or secret is empty, Platform Link is **disabled**.

## Instance behavior

1. On startup / refresh interval, if linked: pull catalogs into MySQL (`referenceCurrencies`, `referenceTimezones`, `referencePlugins`).
2. Instance API (DB only): `GET /reference/currencies|timezones|plugins|status`.
3. First-run wizard (`/setup`) uses `/reference/status` for currency/timezone; if catalogs are not ready, can create a store with server defaults (`RUB` / `Europe/Moscow`).
4. Auth JWT must have non-empty `iss`/`aud`. CP provision sets `JWT_ISSUER` / `JWT_AUDIENCE` (default `delegat`); API falls back to `delegat` if env is missing. Cookies use HTTPS/`COOKIE_SECURE`, not `ASPNETCORE_ENVIRONMENT=Production`.
5. **Usage / entitlements:** heartbeat may include a usage snapshot; instance polls `GET /platform/v1/entitlements` for plan name and `overagePurgeAt` (cached in `instancePlatformEntitlements`). Admin UI: Settings → Admin → Usage. Auto-trim runs on the instance when the purge deadline is due and usage is still over caps.
6. **Seller docs:** Control Plane downloads Wildberries (items and FBS orders), Ozon Seller, and Yandex Market documentation once a day. The cabinet assistant reads the current excerpt with `GET /api/agent/stores/{storeId}/marketplace-docs/{adapter}?q=` (Platform Link `GET /platform/v1/marketplace-docs/{adapter}`) and does not fetch those portals itself.

## Related

- Instance capabilities: [capabilities.md](capabilities.md)
- CP provisioning / JWT env: `delegat-module-platform` → `docs/provisioning.md`, `docs/runbooks/provision-failure.md`
