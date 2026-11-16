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
7. **Hosted AI:** when configured and entitled by the Control Plane, Platform AI appears in the cabinet model picker. Calls are HMAC-signed with a unique request ID and proxied by the Control Plane; its provider key is never sent to the instance. Organization-wide monthly token usage is quota-limited. BYOK model connections remain a separate path and do not use the hosted quota. See [AI system](../architecture/ai-system.md#optional-platform-ai) and the `Hosted AI` section of `delegat-module-platform/docs/platform-link.md` in the Control Plane repository.
8. **Console SSO into the cabinet:** the console "Open cabinet" action links to `{publicUrl}/signIn?platformSso=…` with a one-time code minted on the Control Plane — the sign-in page forwards it to `GET /api/auth/platformSso?code=…` (older builds without the handler simply show the sign-in form). The endpoint redeems the code via `POST /platform/v1/sso/instance/exchange` (Platform Link HMAC, not the user's session), maps the returned account email to an existing workspace user, and issues that user's normal cookie token pair before redirecting to `/products`. When no workspace user shares the email, a member whose response `role` is `owner` falls back to the first active workspace administrator — equivalent to the console's reset-admin capability; other roles get no implicit workspace access. `isActive` and lockout checks still apply; failures bounce to `/signIn?ssoError=…` with a per-reason localized toast. See `Console SSO into an instance cabinet` in `delegat-module-platform/docs/platform-link.md`.

## Related

- Instance capabilities: [capabilities.md](capabilities.md)
- CP provisioning / JWT env: `delegat-module-platform` → `docs/provisioning.md`, `docs/runbooks/provision-failure.md`
