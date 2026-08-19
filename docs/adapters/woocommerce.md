# WooCommerce adapter

Delegat loads WooCommerce as a marketplace SDK plugin: `Delegat.Marketplace.WooCommerce`.

The plugin talks to a store-side WordPress bridge (PHP plugin shipped in the adapter repo under `wordpress-plugin/delegat-bridge`).

Bridge source:

```text
plugins/Delegat.Marketplace.WooCommerce/wordpress-plugin/delegat-bridge
```

Requires **bridge ≥ 1.2.1** for archive image transfer (`images.import_archive`) with zip-bomb guards.

## Connection credentials

- `apiBaseUrl`: WordPress REST base for the bridge, e.g. `https://shop.example.com/wp-json/delegat/v1`
- `apiKey`: secret from **Settings → Delegat Bridge** in WP admin
- `bulkChunkSize`: optional (default 20, max 200). Smaller chunks reduce WordPress/proxy timeouts on large upserts.

## Image transfer (no mapping changes)

Mappings still export image URLs (presigned MinIO) for adapters that need them. For Woo ZIP import the host also passes `_imageStoragePaths` (MinIO object keys). The Woo adapter:

1. Upserts products **without** `image_urls` (avoids slow WP→MinIO URL drain).
2. Reads image bytes via host `IMediaBlobReader` (internal MinIO) — **not** by re-downloading public URLs. Falls back to HTTP only when storage paths / reader are missing.
3. Packs ZIP chunks (**≤ 48 MB** each; per-file **≤ 16 MB**; max **30** images/product; image extensions only). Store PHP upload limit is 64M.
4. Reads from storage **in parallel** with `products/bulk`.
5. `POST /images/import-archive` — bridge attaches files and schedules thumbnail regen via Action Scheduler (GD resize is **not** on the export hot path).

ZIP layout: `manifest.json` + `products/{local_product_id}/{index}.ext`.

Bridge `import-archive` rejects zip bombs: max upload 64 MiB, ≤500 entries, ≤16 MiB/entry, ≤64 MiB total uncompressed, path traversal / nested `.zip` blocked.

If the bridge returns 404/405 for `import-archive`, the adapter falls back to the legacy URL-queue + `images/drain` path.

For diagnosing failed bulk exports, enable **Adapter HTTP logs** in Settings. Mapping/validation failures blocked before the adapter HTTP call do not appear there — check the export batch `lastError` instead.

Headers: `X-Delegat-Api-Key` and `Authorization: Bearer <key>`.

## Capabilities (MVP)

- Categories list / create
- Category fields
- Export product (simple + variable) and bulk
- Export prices / stocks
- Archive image import (`images.import_archive`)

Variable products come from the host `_variants` bag (Sizes tab), not from mapping-graph fields. Each local size is keyed by Delegat variant id (`_delegat_variant_id`).

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
