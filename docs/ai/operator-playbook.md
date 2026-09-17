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

1. `GET /marketplace/mapping-graph/blocks` then `GET /marketplace/connections/{id}/mappings` then `GET …/mappings/{mappingId}/graph`.
2. `propose_plan`, then `POST …/mappings/{mappingId}/graph/group-titles` `{ "mappings": [{ "from": "cabinet group name", "to": "shop title" }] }`.
3. Export with `export-batch`. Do not change group names in the cabinet.

## Import marketplace products

`POST /marketplace/connections/{id}/products/import-unlinked` (or `import-selected` after preview). Same write as Connect catalogs. Pending HTTP 200 is success.
