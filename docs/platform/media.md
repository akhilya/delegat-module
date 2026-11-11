# Product files and marketplace media

File attributes store ordered references to product-owned files. A File attribute accepts one file by default; its definition must explicitly enable multiple files to accept more. Product galleries may contain images and videos. `ImageUrls`, `_imageUrls`, and `_imageStoragePaths` are image-only projections.

Product forms interpret `fileIds` only for File definitions. Scalar attribute DTOs expose no attachment selection. Cabinet product details include only the selected dictionary entries and mark them `optionsPartial`; selectors fetch the complete category attribute definition on opening, without changing stored option IDs. This compact cabinet representation does not change the Public API v1 product contract.

Video imports accept directly downloadable MP4, WebM, or AVI containers with a matching binary signature, validate each redirect URL, and enforce the import size limit. HTML players and HLS playlists are not downloadable video files. Failed attachments clean up their uploaded storage objects.

MySQL DDL may commit before an interrupted migration is recorded. Back up the database and reconcile the full generated migration before restarting; feature migration history is never automatically stamped from object names alone.

## Yandex Market video export

The Yandex Market adapter imports typed video media and exports gallery videos through the official offer `videos` field. The official OpenAPI contract allows up to six permanent HTTP(S) video URLs; short-lived object-storage presigned URLs are not suitable. References checked on 2026-11-10:

- [BaseOfferDTO.yaml](https://github.com/yandex-market/yandex-market-partner-api/blob/main/openapi/components/schemas/BaseOfferDTO.yaml) — `videos`, maximum six, permanent URL requirement.
- [OfferMediaFileDTO.yaml](https://github.com/yandex-market/yandex-market-partner-api/blob/main/openapi/components/schemas/OfferMediaFileDTO.yaml) — media URL/title/upload state.

Before an actual single or bulk Yandex export, the host creates or reuses a durable grant for each video file in the product gallery. The grant is scoped to that file and connection, stores a 256-bit random bearer token, and serves the existing immutable object at `GET`/`HEAD /api/pub/media-export/{token}`. The route streams byte ranges and checks revocation on each request. It does not require a cabinet login and never sets `File.isPublic`.

Grant creation is deliberately absent from readiness checks, mapping previews, and file reads. Yandex URLs are not written to product values or mapping JSON. The outbound HTTP log, operation log, application error logs, and nginx access log redact or suppress grant tokens.

Configure `PUBLIC_BASE_URL` as the stable external HTTPS origin of the instance (for example, `https://catalog.example.com`, with no path, query, or credentials); Docker Compose deployments must pass it through to the API container. Export is blocked with a setup error when this is missing, invalid, unresolvable, or resolves to local/private addresses. The instance must be reachable by Yandex over HTTPS. Keep the origin stable while remote offers may fetch the media.

To revoke every video URL issued for a connection, an authorized operator can call:

```http
POST /api/marketplace/connections/{connectionId}/media-grants/revoke
```

This requires `marketplace.manage` and store membership. Revocation can break future marketplace re-fetches. Deleting a connection or permanently deleting a file removes its grants; renaming a file and removing it from the local gallery do not invalidate grants. A later authorized export after revocation issues a new token, leaving the previous URL revoked.

## Other adapters

Wildberries imports typed video when its API supplies a downloadable asset, but video export remains disabled until the media-save contract and replacement semantics are verified. Ozon does not currently advertise video media capability. Source review also confirms that OpenCart maps only `ImageUrls`, WooCommerce's archive path accepts only allowlisted image extensions, and Medusa exposes an image-URL field only. Their upstream/bridge video contracts have not been confirmed, so no video support is inferred from image fields or arbitrary metadata. Avito listing publication remains outside the adapter's supported contract.
