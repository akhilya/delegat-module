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

Sections start collapsed, in this order: account, catalog, connections, mappings, sync, then live commerce (Prices, Orders, Fulfillment, Shipments, Promotions). Each row is one line; the contract, query, and error codes are inside the operation. Every non-2xx body is `{ "error": { "code", "message", "details" } }`. The key stays in the browser after refresh.

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
| 404 | `product_not_found` / `category_not_found` / `group_not_found` / `variant_not_found` / `store_attribute_not_found` / `connection_not_found` / `operation_not_found` / `group_member_not_found` / `mapping_not_found` | Named resource missing |
| 400 | `mapping_category_mismatch` | Product was assigned a mapping whose local category is not the product’s category |
| 400 | `product_has_no_category` | Product has no local category, so no marketplace mapping can be chosen |
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
| POST | `/` | Create. Body: product fields (`sku`, `name`, …) same shape as SPA create. `categoryId` must belong to this store. Barcode must be EAN-8/13 when set. Characteristic `value` is stored as a string; JSON numbers, booleans, and arrays are accepted and stored as text (arrays as JSON text). |
| PATCH | `/{productId}` | Partial update. Sending `attributeValues` or `storeAttributeValues` **replaces** that collection (same as the dashboard). Characteristic `value` is stored as a string; JSON numbers, booleans, and arrays are accepted. |
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
| GET | `/product-links` | Links. Query: `connectionId?`, `page`, `pageSize`. Each item includes `categoryMappingId`, `categoryMappingName`, and `externalCategoryName` when a mapping is chosen |

### Mappings

Base: `/stores/{storeId}/connections/{connectionId}/…`

One local category may have several mappings when it exports to different marketplace categories. Assigning a product picks one of those rows. It does not edit the field canvas.

| Method | Path | Body | Notes |
|--------|------|------|--------|
| GET | `/mappings` | | `id`, `name`, `localCategoryId`, `localCategoryName`, `externalCategoryId`, `externalCategoryName` |
| POST | `/product-mappings/resolve` | `{ productIds }` | `selectedMappingId`, `needsSelection`, `availableMappings` for each product |
| POST | `/product-mappings` | `{ selections: [{ productId, categoryMappingId }] }` | `categoryMappingId` must belong to this connection and to the product’s local category. Unknown mapping → `404 mapping_not_found`. Other local category → `400 mapping_category_mismatch`. No local category → `400 product_has_no_category`. A product with no link yet gets a pending link |

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

### Marketplace commerce

Paths are unchanged. Swagger lists them as separate sections: Prices, Orders, Fulfillment, Shipments, Promotions.

Base: `/stores/{storeId}/connections/{connectionId}/…`

Order **lists and gets** read Delegat’s snapshot (eventually consistent; additive `syncedAt`). They do **not** call the marketplace. A background worker plus cabinet page-load (5-minute cooldown) pull new rows. Inactive connection → `400 connection_inactive`. Missing adapter capability → `400 adapter_capability_missing`. Seller status POST still goes to the marketplace, then upserts. Labels, shipments, prices, and promotions stay live pass-through.

#### Prices

| Method | Path | Capability | Notes |
|--------|------|------------|--------|
| GET | `/prices` | ImportPrices | Query `ids` = comma-separated marketplace product ids (omit = page what the adapter can list). Includes discounts / sale / marketing price when the marketplace sends them. |

#### Orders

| Method | Path | Capability | Notes |
|--------|------|------------|--------|
| GET | `/orders` | GetOrders | Snapshot. Query: `page`, `pageSize`, `cursor` (numeric cursor is the next page number; `nextCursor` is `page+1` when `hasMore`), `status`, `from`, `to`, `channel` (`fbs` / `fbo` / `shop`), `search`. Same item fields as before plus `syncedAt`. Never synced → empty page, not a live cabinet dump. Normalized `status`: `new`, `confirmed`, `awaiting_pack`, `awaiting_deliver`, `delivering`, `delivered`, `cancelled`, `returned`, `unpaid`, `unknown`. Yandex Market maps those to Partner API `UNPAID` / `PROCESSING` / `PICKUP` / `DELIVERY` / `DELIVERED` / `CANCELLED` / `RETURNED` (`new` is `UNPAID`, packing is `PROCESSING` — Yandex has no `NEW` / `AWAITING_PACK`). Cabinet New (`new` / `unpaid`) is the accept queue (Ozon `awaiting_approve`). Wildberries FBS has no accept step: new assembly tasks (`supplierStatus=new`, `GET /api/v3/orders/new`) are `awaiting_pack`; in a supply (`confirm`) is `awaiting_deliver`; handed to WB (`complete`) is `delivering` until `wbStatus=sold`. Each item may include `imageUrl`, `name`, `barcode`, and `localProductId` as before. Wildberries `displayId` is the numeric order id (not `rid`). Search matches stored posting / display ids. |
| GET | `/orders/{orderId}` | GetOrders | Snapshot. Missing → `404 order_not_found` (never synced). Ozon: posting number or the cabinet order number. Same item fields as the list plus `syncedAt`. |
| GET | `/orders/{orderId}/label` | GetOrderLabels | Packing label / barcode sticker. Body: `{ contentType, fileName, contentBase64, format, barcode?, extra? }`. `format` is `pdf`, `png`, or `svg`. **Only in statuses where the marketplace can print:** Ozon FBS PDF (`/v2/posting/fbs/package-label`) after assemble (`awaiting_deliver`); FBO has none. Wildberries PNG sticker (`/api/v3/orders/stickers`) for `confirm` (in a supply) and `complete` (in delivery), not new assembly. Yandex Market PDF (`/v2/campaigns/…/orders/…/delivery/labels`) while `PROCESSING` (FBS/DBS); FBY has none. WooCommerce and OpenCart → `400 adapter_capability_missing`. Wrong status or marketplace error → `400 order_label_failed`. |
| POST | `/orders/{orderId}/status` | UpdateOrderStatus | Body `{ action, reason?, trackingNumber?, deliveryService?, extra? }`. `action`: `confirm`, `pack`, `ship`, `deliver`, `complete`, `cancel`. **Not every marketplace accepts every action.** Ozon FBS: `pack`/`confirm` assemble (`/v4/posting/fbs/ship`), `ship` hands over (`/v2/posting/fbs/awaiting-delivery`), `cancel` before handover. Ozon does **not** let the seller mark delivered (`deliver`/`complete` → `400 order_status_failed`). Wildberries FBS: `pack`/`confirm` add to a supply (`extra.supplyId` uses that supply; omit it and the adapter creates one), optional `extra.boxId` / `extra.createBox` for a TRBX; `ship` delivers **the whole supply** (`extra.supplyId`); `cancel` on new assembly tasks; sold is WB logistics. Yandex: `deliver`/`complete` are for DBS (shop delivery), not FBS/FBY. Woo/OpenCart: shop-owned statuses. Failure → `400 order_status_failed`. |

#### Fulfillment

| Method | Path | Capability | Notes |
|--------|------|------------|--------|
| GET | `/fulfillment/locations` | GetFulfillmentLocations | Query `kind`: `seller_warehouse` or `marketplace_office` (omit = both). Wildberries: seller warehouses (`GET /api/v3/warehouses`) and drop-off offices (`GET /api/v3/offices`). Ozon: seller warehouses only (`/v2/warehouse/list`). Yandex/Woo/OpenCart → `400 adapter_capability_missing`. Marketplace error → `400 fulfillment_location_failed`. Not stored. |

#### Shipments

| Method | Path | Capability | Notes |
|--------|------|------------|--------|
| GET | `/shipments` | ManageShipments | Query: `limit`, `cursor`, `openOnly`. Live supplies (Wildberries `GET /api/v3/supplies`). Response is a page plus `nextCursor` / `hasMore`. Yandex/Ozon/Woo/OpenCart → `400 adapter_capability_missing`. Marketplace error → `400 shipment_failed`. Not stored. |
| POST | `/shipments` | ManageShipments | Body `{ name?, destinationLocationId? }`. Creates an open Wildberries supply. `destinationLocationId` is display-only when the marketplace has no write. Failure → `400 shipment_failed`. |
| GET | `/shipments/{shipmentId}` | ManageShipments | Missing → `404 shipment_not_found`. Includes order ids and boxes when the API has them. |
| POST | `/shipments/{shipmentId}/orders` | ManageShipments | Body `{ orderIds }`. Adds assembly orders to the supply. Failure → `400 shipment_failed`. |
| GET | `/shipments/{shipmentId}/boxes` | ManageShipments | Wildberries TRBX list. |
| POST | `/shipments/{shipmentId}/boxes` | ManageShipments | Body `{ amount }` (default 1). Pickup-point supplies only. Failure → `400 shipment_failed`. |
| POST | `/shipments/{shipmentId}/boxes/{boxId}/orders` | ManageShipments | Body `{ orderIds }`. Puts orders into a TRBX. Failure → `400 shipment_failed`. |
| GET | `/shipments/{shipmentId}/boxes/{boxId}/label` | ManageShipments | Box QR sticker (same body as order labels). Empty box or marketplace error → `400 shipment_failed`. |
| GET | `/shipments/{shipmentId}/label` | ManageShipments | Supply QR. Wildberries: only after `ship` / deliver. Failure → `400 shipment_failed`. |

#### Promotions

| Method | Path | Capability | Notes |
|--------|------|------------|--------|
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

# List stored orders (snapshot)
curl -H "Authorization: Bearer dk_..." \
  "https://instance.example/api/v1/stores/1/connections/3/orders?page=1&pageSize=50"

# Packing label (Base64 file; Ozon / Wildberries / Yandex Market)
curl -H "Authorization: Bearer dk_..." \
  "https://instance.example/api/v1/stores/1/connections/3/orders/1234-0001/label"

# Open Wildberries supplies
curl -H "Authorization: Bearer dk_..." \
  "https://instance.example/api/v1/stores/1/connections/3/shipments?openOnly=true"
```

## Out of scope (v1)

Field-mapping graph editor, AI chat, plugin upload/reload, admin invites, webhooks, per-key OAuth scopes / RBAC on keys. Promotions are not stored in Delegat. Order snapshots are kept until the connection or store is deleted (no historical backfill of the cabinet). Operator MCP / Agent HTTP (`docs/api/mcp.md`) is not part of `/api/v1`.

## Related

- Design: [docs/plans/2026-07-17-public-api-v1-design.md](../plans/2026-07-17-public-api-v1-design.md)
- Key management UI: Settings → User (SPA JWT `/api/api-keys`)
- Swagger UI: `/swagger` on every instance (production included)
