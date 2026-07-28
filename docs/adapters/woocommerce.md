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

Requires **bridge ≥ 1.2.0** for archive image transfer (`images.import_archive`).

## Connection credentials

- `apiBaseUrl`: WordPress REST base for the bridge, e.g. `https://printo.studio/wp-json/delegat/v1`
- `apiKey`: secret from **Settings → Delegat Bridge** in WP admin
- `bulkChunkSize`: optional (default 20, max 200). Smaller chunks reduce WordPress/proxy timeouts on large upserts.

## Image transfer (no mapping changes)

Mappings still export image URLs (presigned MinIO). The Woo adapter:

1. Upserts products **without** `image_urls` (avoids slow WP→MinIO URL drain).
2. Downloads images itself (parallel), packs ZIP chunks (**≤ 48 MB** each; store PHP upload limit is 64M).
3. `POST /images/import-archive` — bridge attaches files and schedules thumbnail regen via Action Scheduler (GD resize is **not** on the export hot path).

ZIP layout: `manifest.json` + `products/{local_product_id}/{index}.ext`.

If the bridge returns 404/405 for `import-archive`, the adapter falls back to the legacy URL-queue + `images/drain` path.

For diagnosing failed bulk exports, enable **Adapter HTTP logs** in Settings. Mapping/validation failures blocked before the adapter HTTP call do not appear there — check the export batch `lastError` instead.

Headers: `X-Delegat-Api-Key` and `Authorization: Bearer <key>`.

## Capabilities (MVP)

- Categories list / create
- Category fields
- Export product (simple + variable) and bulk
- Export prices / stocks
- Archive image import (`images.import_archive`)

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
- `POST /images/drain` (legacy URL queue)
- `POST /images/import-archive` (multipart field `archive`)

Idempotency meta on Woo products: `_delegat_product_id`, `_delegat_variant_id`.
