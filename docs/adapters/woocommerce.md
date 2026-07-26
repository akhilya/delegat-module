# WooCommerce adapter

Delegat loads WooCommerce as a marketplace SDK plugin: `Delegat.Marketplace.WooCommerce`.

The plugin talks to a store-side WordPress bridge (PHP plugin shipped in the adapter repo under `wordpress-plugin/delegat-bridge`).

Bridge source:

```text
plugins/Delegat.Marketplace.WooCommerce/wordpress-plugin/delegat-bridge
```

Also vendored in the PRINTO store repo for Dokploy image builds:

```text
../printo-store/wp-content/plugins/delegat-bridge
```

## Connection credentials

- `apiBaseUrl`: WordPress REST base for the bridge, e.g. `https://printo.studio/wp-json/delegat/v1`
- `apiKey`: secret from **Settings → Delegat Bridge** in WP admin
- `bulkChunkSize`: optional (default 50, max 200)

Headers: `X-Delegat-Api-Key` and `Authorization: Bearer <key>`.

## Capabilities (MVP)

- Categories list / create
- Category fields
- Export product (simple + variable) and bulk
- Export prices / stocks

No import from Woo, no orders.

## Bridge endpoints

- `GET /health`
- `GET /categories`
- `POST /categories/upsert`
- `GET /categories/{id}/fields`
- `POST /products/upsert`
- `POST /products/bulk`
- `POST /prices/bulk`
- `POST /stocks/bulk`

Idempotency meta on Woo products: `_delegat_product_id`, `_delegat_variant_id`.
