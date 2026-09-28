# Cabinet assistant playbook

Operator how-tos for catalog writes. Live catalog questions are SQL. **Which HTTP to call** is `search_actions` (not `GET /tools`, not a prompt paragraph per feature).

## Export local products to a shop

Export is **local catalog → marketplace / storefront** (Medusa «Магазин», WooCommerce, OpenCart, Ozon, Wildberries, Yandex Market).

1. Resolve the connection from CURRENT CONTEXT by the name the user sees (one GET is enough).
2. Optional: count cards without gallery photos (`product_images`, not `files`).
3. Whole catalog except those without photos: `propose_plan`, then `POST /marketplace/connections/{id}/products/export-batch` with `{ "allMatching": true, "skipWithoutImages": true }`. HTTP 200 pending is success; cards export in Operations.
4. Named subsets: collect `productIds`, then the same POST with `{ productIds }`. Tiny sets may use `POST …/products/export-bulk`.

Do **not** call `import-unlinked`, Catalog Connect, `GET /tools`, adapters, or mappings for this. Do not tell the operator to start export on the shop page. Import is the opposite direction (shop → this catalog). Linking existing cards to another marketplace is `match/preview` then `match/apply`.

## Finish a field mapping

A mapping binds one local category to one marketplace category. The canvas sends local values into marketplace fields. Attribute groups («Основные параметры», «Вложенные параметры») are groups of local characteristics. They are not shop collection titles.

1. Read `GET /marketplace/connections/{connectionId}/mappings/{mappingId}/graph` once. Use CURRENT PAGE ids.
2. A field is unwired when `source` is empty, `omit` is false, `aliasOf` is empty, and no edge `targetHandle` is `f:{fieldKey}`.
3. `localCategoryId` is on `GET …/connections/{connectionId}/mappings`. Characteristics are `GET /categories/{localCategoryId}` (`id`, `name`, `group`, `key`).
4. `propose_plan` with one `POST …/graph/connect` per field you can match by name:
   - characteristic: `{ "adapterFieldKey", "sourceType": "attribute", "sourceAttributeId" }`
   - card field: `{ "adapterFieldKey", "sourceType": "productField", "sourceField" }` (`name`, `sku`, `barcode`, `description`, `price`, `storageQuantity`, `images`)
   - price block: same body plus `"sink": "price"`
5. Skip fields you cannot match. Do not `PUT` the whole graph. Do not call `graph/group-titles` for this.

Linking categories (the mapping row itself) is `POST /marketplace/connections/{connectionId}/mappings` `{ localCategoryId, externalCategoryId, externalCategoryName }`. That create already auto-wires obvious fields. If the open page is already that pair, finish unwired fields instead of creating another row.

If the user said not to change the page, describe the wires and do not `propose_plan`.

## Medusa collection titles (keep local group names)

Shop collection titles are the **Merge into card** string field (`__group__`) with a typed from/to `valueMap`, not SQL `mappings` rows, not `valueId`, and not renaming `productgroups`.

1. `propose_plan` first (no GET graph / productgroups / search_actions before confirm). Use CURRENT PAGE mapping/connection ids and names the user gave.
2. After confirm: `POST …/mappings/{mappingId}/graph/group-titles` `{ "mappings": [{ "from": "cabinet group name", "to": "shop title" }] }`.
3. Export with `export-batch`. Do not change group names in the cabinet.

## Raise catalog prices and push them to shops

This is a write. `propose_plan` first (no GET prices, no knowledge search).

1. `PATCH /products/bulk` `{ "allMatching": true, "wait": true, "updates": { "priceDelta": 500 } }` — add that amount to every catalog card (sized variants with their own price too). `wait` so the next step sees new amounts.
2. For each named marketplace connection (Ozon, Wildberries, Yandex — ids from CURRENT CONTEXT): `POST /tools/export-all-prices/run` `{ "connectionId": <id> }`. HTTP 200 = Operations.

Do **not** `GET …/connections/{id}/prices` (live marketplace list, not a raise). Do not set the same absolute `updates.price` on every card.

## Import marketplace products

`POST /marketplace/connections/{id}/products/import-unlinked` (or `import-selected` after preview). Same write as Connect catalogs. Pending HTTP 200 is success.
