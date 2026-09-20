# Cabinet assistant playbook

Operator how-tos for catalog writes. Live catalog questions are SQL. **Which HTTP to call** is `search_actions` (not `GET /tools`, not a prompt paragraph per feature).

## Export local products to a shop

Export is **local catalog → marketplace / storefront** (Medusa «Магазин», WooCommerce, OpenCart, Ozon, Wildberries, Yandex Market).

1. Resolve the connection from CURRENT CONTEXT by the name the user sees (one GET is enough).
2. Optional: count cards without gallery photos (`product_images`, not `files`).
3. Whole catalog except those without photos: `propose_plan`, then `POST /marketplace/connections/{id}/products/export-batch` with `{ "allMatching": true, "skipWithoutImages": true }`. HTTP 200 pending is success; cards export in Operations.
4. Named subsets: collect `productIds`, then the same POST with `{ productIds }`. Tiny sets may use `POST …/products/export-bulk`.

Do **not** call `import-unlinked`, Catalog Connect, `GET /tools`, adapters, or mappings for this. Do not tell the operator to start export on the shop page. Import is the opposite direction (shop → this catalog). Linking existing cards to another marketplace is `match/preview` then `match/apply`.

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
