# Public API v1

Partner REST API for ERP / 1C / n8n and similar integrations.  
Separate from the SPA session (`/api/*` + JWT cookie + CSRF).

> **Maintainers:** any platform change that affects catalog, sync, connections, auth, quotas, or operations must update this document and `backend/Controllers/PublicApi/` in the same change. See `.cursor/rules/public-api-docs-sync.mdc`.

## Auth

1. **Settings → User → Public API** — create a key.
2. Copy the plaintext once (`dk_…`). It is shown only at creation.
3. Every request:

```http
Authorization: Bearer dk_your_key_here
```

| Rule | Detail |
|------|--------|
| Key scope | Bound to the **user**; access = stores where the user is an active member |
| CSRF | Not required for `/api/v1` |
| Revoke | Immediate via UI or `DELETE /api/api-keys/{id}` (SPA JWT) |
| Rate limit | ~120 req/min per API key (or IP if key unknown) |

### Swagger / OpenAPI

| Environment | UI | OpenAPI JSON |
|-------------|----|--------------|
| Any instance (incl. production) | `https://{your-instance}/swagger` | `https://{your-instance}/swagger/v1-public/swagger.json` |
| Local API (direct) | `http://localhost:5000/swagger` | `http://localhost:5000/swagger/v1-public/swagger.json` |

In the app: **Settings → User → Public API** → link «Открыть Swagger».

1. Open Swagger → **Authorize**
2. Paste only `dk_…` (do **not** type `Bearer`)
3. Try it out — request must include `Authorization: Bearer dk_…`

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
    "code": "forbidden",
    "message": "Нет доступа к этому магазину",
    "details": null
  }
}
```

| HTTP | code (typical) | When |
|------|----------------|------|
| 401 | `unauthorized` | Missing/invalid/revoked key, inactive user |
| 403 | `forbidden` | User is not a member of `storeId` |
| 404 | `not_found` | Resource missing in that store |
| 409 | `conflict` | Quota / uniqueness / state conflict |
| 422 | `validation_error` | Invalid body (when used) |
| 429 | `rate_limited` | Too many requests |

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
| GET | `/` | List. Query: `search`, `categoryId` (`-1` = no category), `page`, `pageSize` |
| GET | `/{productId}` | Detail + attribute values |
| POST | `/` | Create. Body: product fields (`sku`, `name`, …) same shape as SPA create |
| PATCH | `/{productId}` | Partial update |
| DELETE | `/{productId}` | Soft-delete (trash) |

Respects product quotas (`409` when exceeded).

### Categories (`/stores/{storeId}/categories`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | List with attributes + product counts |
| GET | `/{categoryId}` | Detail |
| POST | `/` | Create `{ name, description? }` |
| PATCH | `/{categoryId}` | Update |
| DELETE | `/{categoryId}` | Delete (fails if products remain → `409`) |

### Groups (`/stores/{storeId}/groups`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | List |
| GET | `/{groupId}` | Detail + `productIds` |
| POST | `/` | Create |
| PATCH | `/{groupId}` | Update |
| DELETE | `/{groupId}` | Delete |
| POST | `/{groupId}/products` | Body `{ productIds: number[] }` |
| DELETE | `/{groupId}/products/{productId}` | Remove member |

### Store attributes (`/stores/{storeId}/store-attributes`)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | List global attributes |
| GET | `/{attributeId}` | Detail |
| POST | `/` | Create |
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
| POST | `/export-products` | `{ productIds?: number[] }` | Empty/`omit` = all store products |
| POST | `/import-products` | `{ products: [{ externalId, externalCategoryId?, productName?, targetCategoryId?, forceImportWithoutMapping? }], forceImportWithoutMapping? }` | Needs category mappings unless forced |
| POST | `/export-prices` | `{ linkIds?: number[] }` | |
| POST | `/import-prices` | `{ linkIds?: number[] }` | |
| POST | `/export-stocks` | `{ linkIds?: number[] }` | |
| POST | `/import-stocks` | _(empty)_ | Adapter must support import stocks |

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

curl -H "Authorization: Bearer dk_..." \
  "https://instance.example/api/v1/stores/1/operations/{operationId}"
```

## Out of scope (v1)

Field-mapping graph editor, AI chat, plugin upload/reload, admin invites, webhooks, per-key OAuth scopes / RBAC on keys.

## Related

- Design: [docs/plans/2026-07-17-public-api-v1-design.md](../plans/2026-07-17-public-api-v1-design.md)
- Key management UI: Settings → User (SPA JWT `/api/api-keys`)
- Swagger UI: `/swagger` on every instance (production included)
