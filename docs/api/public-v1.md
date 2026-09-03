# Public API v1

Partner REST API for ERP / 1C / n8n and similar integrations.  
Separate from the SPA session (`/api/*` + JWT cookie + CSRF).

> **Maintainers:** any platform change that affects catalog, sync, connections, auth, quotas, or operations must update this document and `backend/Controllers/PublicApi/` in the same change. See `.cursor/rules/public-api-docs-sync.mdc`.

## Auth

1. **Settings → API** - create a key (requires permission `api.access`).
2. Copy the plaintext once (`dk_…`). It is shown only at creation.
3. Every request:

```http
Authorization: Bearer dk_your_key_here
```

| Rule | Detail |
|------|--------|
| Permission | Key owner must have `api.access` (otherwise auth fails) |
| Key scope | Bound to the **user**; access = stores where the user is an active member |
| CSRF | Not required for `/api/v1` |
| Revoke | Immediate via Settings → API or `DELETE /api-keys/{id}` (SPA JWT) |
| Logging | Inbound `/api/v1` calls are logged for the key owner (Settings → API → Request logs) |
| Rate limit | ~120 req/min per API key (or IP if key unknown) |

### Swagger / OpenAPI

| Environment | UI | OpenAPI JSON |
|-------------|----|--------------|
| Any instance (incl. production) | `https://{your-instance}/swagger` | `https://{your-instance}/swagger/v1-public/swagger.json` |
| Local API (direct) | `http://localhost:5000/swagger` | `http://localhost:5000/swagger/v1-public/swagger.json` |

In the app: **Settings → API** → "Open Swagger", or **Tools → Try Public API** for GET `/me`, `/stores`, and a custom `/api/v1` path (uses the key, not the dashboard session).

1. Open Swagger → **Authorize**
2. Paste only `dk_…` (do **not** type `Bearer`)
3. Try it out - request must include `Authorization: Bearer dk_…`

On production only **Public API v1** is listed (SPA/internal API docs are hidden).

## Base URL

```
https://{your-instance}/api/v1
```

Store-scoped resources:

```
/api/v1/stores/{storeId}/...
```

Discover stores: `GET /api/v1/stores`.

### Instance enablement

Public API can be turned off in **Administration** without restarting the instance (`GET/PATCH /settings/public-api`, permission `settings.platform`). Seed default: env `PUBLIC_API_ENABLED` (default `true`); afterwards the DB row `instancePublicApiSettings` is the source of truth.

Plan flag `API_USAGE_ENABLED` (Control Plane: Lite off, Pro+ on) is separate. Unset defaults to enabled for self-host; when `CONTROL_PLANE_URL` is set, unset defaults to disabled. When the plan flag is off, every `/api/v1/*` and `/swagger` request returns **503** with `error.code = API_USAGE_DISABLED`, even if the admin toggle is on.

When the admin toggle is off (and the plan allows API), every `/api/v1/*` and `/swagger` request returns **503** with `error.code = PUBLIC_API_DISABLED`. The API keys page is hidden from the admin sidebar until Public API is available.

## Conventions

### Pagination

```json
{ "items": [], "page": 1, "pageSize": 50, "total": 123 }
```

Default `page=1`. List endpoints cap `pageSize` (typically 200).

### Errors

```json
{
  "error": {
    "code": "access_denied_store",
    "message": "No access to this store",
    "details": null
  }
}
```

`code` is a stable machine-readable identifier (prefer semantic codes such as `product_not_found` when provided; otherwise HTTP-derived codes below). `message` is always English. Clients that need localized UI should map `code` to their own strings.

| HTTP | code (typical) | When |
|------|----------------|------|
| 401 | `unauthorized` | Missing/invalid/revoked key, inactive user |
| 403 | `forbidden` / `access_denied_store` | User is not a member of `storeId` |
| 404 | `not_found` | Resource missing in that store (prefer the semantic codes below when present) |
| 404 | `product_not_found` / `category_not_found` / `group_not_found` / `variant_not_found` / `store_attribute_not_found` / `connection_not_found` / `operation_not_found` / `group_member_not_found` | Named resource missing |
| 400 | `connection_inactive` | Sync starter or live commerce used a paused marketplace connection |
| 400 | `adapter_capability_missing` | Connection adapter does not support the requested commerce action |
| 400 | `order_status_failed` / `order_label_failed` / `promotion_update_failed` | Marketplace rejected the order status, label download, or promotion product update |
| 404 | `order_not_found` / `promotion_not_found` | Live marketplace order or promotion id was not found |
| 400 | `invalid_barcode` | Barcode is not a valid EAN-8 / EAN-13 |
| 400 | `import_nothing_queued` | Import-products skipped every item (unmapped categories, blank ids) |
| 409 | `conflict` | Quota / uniqueness / state conflict |
| 409 | `sku_already_used` / `store_attribute_key_used` / `category_has_products` / `product_limit_reached` | Named uniqueness or state conflict |
| 422 | `validation_error` | Invalid body (when used) |
| 429 | `rate_limited` | Too many requests |
| 503 | `PUBLIC_API_DISABLED` | Instance Public API toggle is off |
| 503 | `API_USAGE_DISABLED` | Plan does not include Public API (`API_USAGE_ENABLED=false`) |

### Async marketplace operations

Sync starters return **202 Accepted**:

```json
{
  "operationId": "a1b2c3...",
  "status": "running",
  "links": {
    "self": "/api/v1/stores/1/operations/a1b2c3..."
  }
}
```

Poll `GET /api/v1/stores/{storeId}/operations/{operationId}` until `status` is terminal (`completed`, `failed`, `partial`, etc.). Response may include recent `logs`.

---

## Endpoints

### Meta

| Method | Path | Description |
|--------|------|-------------|
| GET | `/me` | Current user (`id`, `userName`, `email`, `isAdmin`) |
| GET | `/stores` | Stores accessible with this key |

### Products (`/stores/{storeId}/products`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | List. Query: `search` (name, SKU, barcode, product id, marketplace external id/SKU, variant SKU/barcode), `categoryId` (`-1` = no category), `page`, `pageSize`. Items include `variantCount`. Trashed products are omitted. |
| GET | `/{productId}` | Detail: prices, `attributeValues`, `storeAttributeValues`, and `variants` (size, SKU, barcode, price, quantity, sortOrder) |
| POST | `/` | Create. Body: product fields (`sku`, `name`, …) same shape as SPA create. `categoryId` must belong to this store. Barcode must be EAN-8/13 when set. |
| PATCH | `/{productId}` | Partial update. Sending `attributeValues` or `storeAttributeValues` **replaces** that collection (same as the dashboard). |
| DELETE | `/{productId}` | Soft-delete (trash) |
| GET | `/{productId}/variants` | Size variants for the product |
| POST | `/{productId}/variants` | Create. Body: `{ sku?, barcode?, size?, price?, quantity? }` |
| PATCH | `/{productId}/variants/{variantId}` | Partial update (same fields + `sortOrder?`) |
| DELETE | `/{productId}/variants/{variantId}` | Delete a size variant |

Respects product quotas (`409 product_limit_reached` when exceeded). Duplicate SKU → `409 sku_already_used`.

### Categories (`/stores/{storeId}/categories`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | List with attributes + product counts (`productCount` and `productsCount` are the same number). Attribute objects match GET detail: `options`, `group`, `groupIcon` (`adapter:{adapterKey}` or a Lucide-compatible name), `maxLength`, `minValue`, `maxValue`, `maxValues` (select/multiselect cap; omitted when unlimited), `validationPattern` when set, `allowCustomValues`, `defaultValue`, `description`, `isMultiline`, `showOnProductPage`, `sortOrder`. Category objects include `hasSizes` (local Sizes tab; not a mapping-graph field). |
| GET | `/{categoryId}` | Detail (same attribute fields as list, including `groupIcon`). Product count field is `productsCount`. |
| POST | `/` | Create `{ name, description?, hasSizes? }` |
| PATCH | `/{categoryId}` | Update (`name?`, `description?`, `hasSizes?`) |
| DELETE | `/{categoryId}` | Delete (fails if products remain → `409 category_has_products`) |

### Groups (`/stores/{storeId}/groups`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | List |
| GET | `/{groupId}` | Detail + `productIds` |
| POST | `/` | Create |
| PATCH | `/{groupId}` | Update |
| DELETE | `/{groupId}` | Delete |
| POST | `/{groupId}/products` | Body `{ productIds: number[] }`. A product can be in only one group; if it already belongs to another group, it is moved. |
| DELETE | `/{groupId}/products/{productId}` | Remove member |

### Store attributes (`/stores/{storeId}/store-attributes`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | List global attributes (same DTO as the dashboard, including `options`, `allowCustomValues`, `maxLength`, `minValue`, `maxValue`, `maxValues`, `validationPattern`) |
| GET | `/{attributeId}` | Detail |
| POST | `/` | Create. Body matches the dashboard (`name`, `key`, `type`, `options?`, `allowCustomValues?`, `maxLength?`, `minValue?`, `maxValue?`, `maxValues?`, `validationPattern?`, `defaultValue?`, `description?`, …). Duplicate `key` → `409 store_attribute_key_used`. |
| PATCH | `/{attributeId}` | Update |
| DELETE | `/{attributeId}` | Delete |

### Connections & links (`/stores/{storeId}/…`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/connections` | Marketplace connections (**no credentials**) |
| GET | `/connections/{connectionId}` | Connection meta |
| GET | `/product-links` | Links. Query: `connectionId?`, `page`, `pageSize` |

### Operations

| Method | Path | Description |
|--------|------|-------------|
| GET | `/operations` | History. Query: `status?`, `page`, `pageSize` |
| GET | `/operations/{operationId}` | Status + up to 200 log lines |

### Sync (always `202`)

Base: `/stores/{storeId}/connections/{connectionId}/…`

| Method | Path | Body | Notes |
|--------|------|------|--------|
| POST | `/export-products` | `{ productIds?: number[] }` | Empty/`omit` = all store products. Inactive connection → `400 connection_inactive` |
| POST | `/import-products` | `{ products: [{ externalId, externalCategoryId?, productName?, targetCategoryId?, forceImportWithoutMapping? }], forceImportWithoutMapping? }` | Needs category mappings unless forced. Selling price comes from the card, or from the prices API when the card omits it. Queues at most the remaining `MAX_PRODUCTS` slots (`409 product_limit_reached` when the store is already at the cap) |
| POST | `/export-prices` | `{ linkIds?: number[] }` | |
| POST | `/import-prices` | `{ linkIds?: number[] }` | |
| POST | `/export-stocks` | `{ linkIds?: number[] }` | |
| POST | `/import-stocks` | _(empty)_ | Adapter must support import stocks |

### Marketplace commerce (live, not stored)

Base: `/stores/{storeId}/connections/{connectionId}/…`

These calls go to the marketplace cabinet and return immediately. They do **not** create an Operation and do **not** persist orders or promotions in Delegat. Inactive connection → `400 connection_inactive`. Missing adapter capability → `400 adapter_capability_missing`.

| Method | Path | Capability | Notes |
|--------|------|------------|--------|
| GET | `/prices` | ImportPrices | Query `ids` = comma-separated marketplace product ids (omit = page what the adapter can list). Includes discounts / sale / marketing price when the marketplace sends them. |
| GET | `/orders` | GetOrders | Query: `page`, `pageSize`, `cursor`, `status`, `from`, `to`, `channel` (`fbs` / `fbo` / `shop`), `search`. Response is the usual page plus `nextCursor` and `hasMore` when the marketplace is cursor-based. Normalized `status`: `new`, `confirmed`, `awaiting_pack`, `awaiting_deliver`, `delivering`, `delivered`, `cancelled`, `returned`, `unpaid`, `unknown`. Cabinet New (`new` / `unpaid`) is the accept queue (Ozon `awaiting_approve`). Wildberries FBS has no accept step: new assembly tasks (`supplierStatus=new`, `GET /api/v3/orders/new`) are `awaiting_pack`; in a supply (`confirm`) is `awaiting_deliver`; handed to WB (`complete`) is `delivering` until `wbStatus=sold`. List payloads have no statuses — the adapter loads them from `POST /api/v3/orders/status`. Each item may include `imageUrl` (Wildberries basket CDN, Ozon Seller API primary photo, or the local linked product photo). |
| GET | `/orders/{orderId}` | GetOrders | Missing → `404 order_not_found` |
| GET | `/orders/{orderId}/label` | GetOrderLabels | Packing label / barcode sticker. Body: `{ contentType, fileName, contentBase64, format, barcode?, extra? }`. `format` is `pdf`, `png`, or `svg`. Ozon FBS PDF (`/v2/posting/fbs/package-label`), Wildberries PNG sticker (`/api/v3/orders/stickers`), Yandex Market PDF (`/v2/campaigns/…/orders/…/delivery/labels`). WooCommerce and OpenCart → `400 adapter_capability_missing`. Failure → `400 order_label_failed` (order not packed yet, FBO, marketplace error). |
| POST | `/orders/{orderId}/status` | UpdateOrderStatus | Body `{ action, reason?, trackingNumber?, deliveryService?, extra? }`. `action`: `confirm`, `pack`, `ship`, `deliver`, `complete`, `cancel`. Failure → `400 order_status_failed`. |
| GET | `/promotions` | GetPromotions | Query: `page`, `pageSize`, `cursor`, `status`, `type`. Marketplace actions, calendar campaigns, or shop coupons. |
| GET | `/promotions/{promotionId}` | GetPromotions | Missing → `404 promotion_not_found` |
| POST | `/promotions/{promotionId}/products` | UpdatePromotions | Body `{ add: [{ externalProductId, actionPrice? }], removeExternalProductIds: [] }`. Failure → `400 promotion_update_failed`. |

---

## Examples

```bash
# Who am I
curl -H "Authorization: Bearer dk_..." \
  "https://instance.example/api/v1/me"

# List products
curl -H "Authorization: Bearer dk_..." \
  "https://instance.example/api/v1/stores/1/products?page=1&pageSize=50"

# Start price export, then poll
curl -X POST -H "Authorization: Bearer dk_..." \
  -H "Content-Type: application/json" \
  -d '{"linkIds":[10,11]}' \
  "https://instance.example/api/v1/stores/1/connections/3/export-prices"

# List orders (live marketplace cabinet)
curl -H "Authorization: Bearer dk_..." \
  "https://instance.example/api/v1/stores/1/connections/3/orders?page=1&pageSize=50"

# Packing label (Base64 file; Ozon / Wildberries / Yandex Market)
curl -H "Authorization: Bearer dk_..." \
  "https://instance.example/api/v1/stores/1/connections/3/orders/1234-0001/label"
```

## Out of scope (v1)

Field-mapping graph editor, AI chat, plugin upload/reload, admin invites, webhooks, per-key OAuth scopes / RBAC on keys. Orders and promotions are not stored in Delegat (no local order tables).

## Related

- Design: [docs/plans/2026-07-17-public-api-v1-design.md](../plans/2026-07-17-public-api-v1-design.md)
- Key management UI: Settings → User (SPA JWT `/api/api-keys`)
- Swagger UI: `/swagger` on every instance (production included)
