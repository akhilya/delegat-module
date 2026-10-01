# Cabinet assistant playbook

Operator how-tos for catalog writes. Live catalog questions are SQL. **Which HTTP to call** is `search_actions` (not `GET /tools`, not a prompt paragraph per feature).

## Export local products to a shop

Export is **local catalog → marketplace / storefront** (Medusa «Магазин», WooCommerce, OpenCart, Ozon, Wildberries, Yandex Market).

1. Resolve the connection from CURRENT CONTEXT by the name the user sees (one GET is enough).
2. Optional: count cards without gallery photos (`product_images`, not `files`).
3. Any catalog slice: `propose_plan`, then `POST /marketplace/connections/{id}/products/export-batch` with `{ "where": "…" }`. Prefer a template: `@withGallery`, `@withoutGallery`, `@withFiles`, `@withoutFiles`, `@inStock`, `@outOfStock`, `@noPrice`, `@noBarcode`, `@noCategory`. Combine with `AND` (`@withGallery AND price >= 1000 AND categoryId = 3`). A characteristic is `id IN (SELECT productId FROM store_product_attribute_values WHERE storeAttributeId = N AND value = 'text')`. The same `where` filters `GET /products` and `PATCH /products/bulk`. HTTP 200 pending is success; cards export in Operations.
4. Named subsets: collect `productIds`, then the same POST with `{ productIds }`. Tiny sets may use `POST …/products/export-bulk`.

Do **not** call `import-unlinked`, Catalog Connect, `GET /tools`, adapters, or mappings for this. Do not tell the operator to start export on the shop page. Import is the opposite direction (shop → this catalog). Linking existing cards to another marketplace is `match/preview` then `match/apply`.

## Finish a field mapping

A mapping binds one local category to one marketplace category. The canvas sends local values into marketplace fields. Attribute groups («Основные параметры», «Вложенные параметры») are groups of local characteristics. They are not shop collection titles. MAPPING EXPERIENCE is exports of this category on this connection since the canvas was last saved, with the adapter text on current failures. Fix only a failure in that list. Nothing since the save means earlier failures are not open.

1. Read `GET /marketplace/connections/{connectionId}/mappings/{mappingId}/graph` once. Use CURRENT PAGE ids.
2. A field is unwired when `source` is empty, `omit` is false, `aliasOf` is empty, and no edge `targetHandle` is `f:{fieldKey}`.
3. `localCategoryId` is on `GET …/connections/{connectionId}/mappings`. Characteristics are `GET /categories/{localCategoryId}` (`id`, `name`, `group`, `key`).
4. Read personal settings before inventing a constant. Cabinet chat already has them. Operator MCP: `GET /api/ai/store-settings` (store description, style, brevity, emoji). `PUT` of that path is denied. Extended price settings are in CURRENT CONTEXT. When they are off, do not wire `discountedPrice` or `sale_price` — that row is not on the price block.
5. `propose_plan` with one `POST …/graph/connect` per field you can match, plus a modifier step when the units differ:
   - characteristic: `{ "adapterFieldKey", "sourceType": "attribute", "sourceAttributeId" }`
   - card field: `{ "adapterFieldKey", "sourceType": "productField", "sourceField" }` (`name`, `sku`, `barcode`, `description`, `price`, `storageQuantity`, `images`)
   - constant you can justify from the store description or catalog: `{ "sourceType": "static", "staticValue" }`. When the shop field has a directory, that static value is the standard-value block: pick an entry from the field’s own options. Do not invent a code. The source of that block is fixed after it is chosen.
   - price block: same body plus `"sink": "price"`
   - millimeters into centimeters: `POST …/graph/modifiers` `{ "modifierType": "divide", "params": { "divisor": 10 } }`
   - grams into kilograms: divisor `1000` into the card field `weight`. Do not wire the characteristic «Вес товара с упаковкой» — Wildberries rejects it and reads `dimensions.weightBrutto` in kilograms. Weight without packaging may stay a characteristic.
   - a per-product divisor (scale from figurine height): `paramSources: [{ "param": "divisor", "sourceType": "attribute", "sourceAttributeId" }]`. Pin is `p:divisor`. Chain static `1700` → divide → round `decimals` 0 → template `1:` plus the value placeholder, and say the assumption.
   - strikethrough 20–40% above the real price: multiply into `oldPrice` and leave `discount` unwired. Discount `0` charges the higher base.
6. A question is one script, then the reply. Do not keep searching. If the shop lists `docs.platform`, that same script calls `client.marketplace_doc` once and then one path the excerpt shows. Do not fetch a documentation URL yourself. Do not invent a documentation URL when `docs` is absent. A shop field that must match that shop (a directory, a brand, a code, a format) uses a value the shop actually has. Prefer the standard-value block fed by that field’s dictionary. If the canvas options already list it, choose the entry that matches the goods and say which one. If they do not, and the calculation helper is on, one read-only script calls that shop (`shops` is already loaded: credentials, hosts, auth; urllib to that host) and the constant is a value from the response. Do not put that lookup in the same tool call as `propose_plan`. Do not invent a code outside the list and do not leave the field blank. A category attribute stores the dictionary key; the block’s key/value toggle emits the key or the label. A number where the shop expects a name is a key: write the shop’s string on the mapping. `regexReplace` rewrites text by a pattern; `regexExtract` returns a match. If nothing decides it, `ask_form` that turn and do not `propose_plan` until they answer. Two fields for the same fact (Brand and the brand characteristic) are one wire plus `aliasOf`, not two empty rows.
7. Attribute blocks use the characteristic name as the label. Blocks you add start collapsed. The sink cards stay open.
8. Do not `PUT` the whole graph. Do not call `graph/group-titles` for this. Skip alias fields.

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
