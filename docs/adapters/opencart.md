# OpenCart adapter infrastructure

Delegat loads OpenCart as a regular marketplace SDK plugin: `Delegat.Marketplace.OpenCart`.

OpenCart installations differ heavily by version and extensions, so the plugin talks to a small OpenCart-side bridge endpoint instead of assuming one universal native API. The bridge can expose either REST-style paths or an OpenCart query-route endpoint.

The OpenCart 4.x bridge source lives in a separate repository:

```text
../delegat-opencart-bridge
```

After installing that extension in OpenCart, use:

```text
https://your-shop.example/index.php?route=extension/delegat_bridge/api/delegat
```

## Connection credentials

- `apiBaseUrl`: full bridge URL.
  - REST style: `https://shop.example.com/delegat-api`
  - OpenCart route style: `https://shop.example.com/index.php?route=extension/delegat_bridge/api/delegat`
- `apiKey`: bridge secret.
- `storeId`: optional OpenCart `store_id`.
- `languageId`: optional OpenCart `language_id`.
- `bulkChunkSize`: optional product batch size, default `100`, max `500`.

The plugin sends both `X-Delegat-Api-Key: <key>` and `Authorization: Bearer <key>`.

## Bridge actions

REST-style URLs:

- `GET /health`
- `GET /categories?parent_id=&search=`
- `POST /categories/upsert`
- `GET /categories/{category_id}/fields`
- `POST /products/upsert`
- `POST /products/bulk`
- `POST /prices/bulk`
- `POST /stocks/bulk`

OpenCart query-route URLs receive equivalent `action` names:

- `health`
- `categories`
- `categories.upsert`
- `category.fields`
- `products.upsert`
- `products.bulk`
- `prices.bulk`
- `stocks.bulk`

## Product payload

`products/upsert` receives one normalized product:

```json
{
  "product_id": "123",
  "sku": "SKU-1",
  "model": "SKU-1",
  "name": "Product name",
  "description": "HTML or plain text",
  "meta_title": "Product name",
  "price": 1000,
  "quantity": 5,
  "category_ids": ["20"],
  "image_urls": ["https://.../image.jpg"],
  "attributes": { "Color": "Black" },
  "options": { "Size": "L" },
  "raw_fields": { "custom_field": "value" },
  "store_id": 0,
  "language_id": 1
}
```

`products/bulk` receives:

```json
{
  "items": [
    {
      "local_product_id": 1,
      "existing_external_id": "123",
      "product": {}
    }
  ]
}
```

Each result should include `local_product_id`, `success`, and `product_id` or `error_message`.

## Field mapping conventions

- `_categoryId` is injected by Delegat category mappings and becomes `category_ids`.
- Product Sizes tab rows are injected as `_variants` (`id`, `techSize`, barcode, price, stock) and become `variants` on the payload. They are not mapping-graph nodes. Clothing size as an OpenCart option is still a mapped `option:` field if the category schema has one.
- `images` should be mapped from the product field `images`; Delegat supplies public image URLs in order.
- Keys starting with `attr:` or `attribute:` are sent to `attributes`.
- Keys starting with `option:` are sent to `options`.
- Other unknown mapped keys are preserved in `raw_fields`, so the bridge can support custom OpenCart extensions without changing the plugin.
