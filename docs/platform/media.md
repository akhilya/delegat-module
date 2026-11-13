# Product files and marketplace media

File attributes store ordered references to product-owned files. A File attribute accepts one file by default; its definition must explicitly enable multiple files to accept more. Product galleries may contain images and videos. `ImageUrls`, `_imageUrls`, and `_imageStoragePaths` are image-only projections.

Product forms interpret `fileIds` only for File definitions. Scalar attribute DTOs expose no attachment selection. Cabinet product details include only the selected dictionary entries and mark them `optionsPartial`; selectors fetch the complete category attribute definition on opening, without changing stored option IDs. This compact cabinet representation does not change the Public API v1 product contract.

Category import preview, duplicate matching, and creation share attachment type/name inference. Explicit `file`/`files`/`attachment`/`attachments` types become File attributes. String fields with attachment names or media/document link names also become File: for example, `Озон.Видео: ссылка` becomes `Видео`, and `Озон.Видеообложка: ссылка` becomes `Видеообложка`. Ozon prefixes and trailing link/file qualifiers are removed only from local File labels; remote keys, names, and wire types remain unchanged. Ordinary page links, attachment metadata (such as video titles), dictionaries, numbers, and booleans are not retyped by name. Remote string limits, regexes, options, and defaults are not copied to File definitions. A declared `MaxValues > 1` enables multiple files; otherwise the definition accepts one. Existing definitions and values are not automatically converted or mutated on reuse. This classification does not download files or add new adapter export support.

Video imports accept directly downloadable MP4, WebM, or AVI containers with a matching binary signature, validate each redirect URL, and enforce the import size limit. HTML players and HLS playlists are not downloadable video files. Failed attachments clean up their uploaded storage objects. Container signatures do not validate codecs or prove marketplace playback compatibility.

Product gallery video thumbnails lazily load a paused initial frame through the existing authorized, range-capable file download endpoint (or a local upload preview URL). They do not autoplay or expose a second interactive player inside the preview button. Unsupported codecs, load failures, and timeouts fall back to the play icon; opening the full preview remains available. Thumbnail requests are released when the source changes or the component unmounts. No server-side poster/transcoding pipeline is required.

The product editor's Media tab reuses the same gallery tiles and state as the properties-page gallery, including photos, videos, main-image badges, upload, removal, reordering, and full previews. Its larger responsive grid uses filled square photo crops; full previews fit the available viewport without cropping or retaining a small source's intrinsic display size. Photos and videos can be dragged between In gallery and Not in gallery, including when either section is empty; each editor has its own drag group. Dropping into the gallery preserves the insertion position. Available photos and videos from product storage can also be added with the tile's + action; removing media leaves its file in storage. Selection and order are saved with the product from either Properties or Media, rather than through the former image-only tab's separate autosave. The product-delete action appears only on Properties. Upload retains the existing immediate attachment flow. The `tab=images` query value is retained for existing links; its visible label is Media.

MySQL DDL may commit before an interrupted migration is recorded. Back up the database and reconcile the full generated migration before restarting; feature migration history is never automatically stamped from object names alone.

## URL-grant exports

Ozon and Wildberries video exports use the same connection-scoped, revocable HTTPS grants described below for Yandex Market. Only video files selected in the ordered product gallery are exported. Arbitrary graph-provided video URLs are removed. Grants are created immediately before actual single or bulk adapter export; readiness checks and previews do not create them.

Ozon maps gallery URLs and names to the Seller API product-import `complex_attributes` fields: complex attribute `100001`, URL attribute `21841`, and name attribute `21837`. Its existing product-import task flow handles the regular asynchronous `/v3/product/import` response. On import, each `complex_attributes` element is read as a separate video (gallery values found in the top-level `attributes` list are accepted too); video and cover URLs become typed media and are not exposed as plain attribute values for graph mapping. The adapter does not impose video count, byte-size, or MIME limits that are not established by the contract. Existing video complex attributes are retained when the local gallery has no selected video, so an empty local selection is not a remote deletion request.

Wildberries sends the selected video URL alongside the photo URLs to `POST /content/v3/media/save`, after the card `nmId` is known. The URL is a permanent grant URL ending in `/video.mp4` or `/video.mov`, as required for a direct media-file URL. The documented limits used by the adapter are one video, at most 50 MB, MP4 or MOV, and at most 30 photos. On updates the adapter fetches the existing card and constructs the full media list, retaining current photos and video unless a selected local video replaces the current video. It does not call media-save for an empty local gallery.

Contract references checked on 2026-11-11:

- [Wildberries API — Upload media files via links](https://dev.wildberries.ru/en/docs/openapi/item-management#tag/mediaFiles/operation/postV3MediaSave) — direct-file URL requirements, including a filename and extension.
- [Wildberries API — Working with items](https://dev.wildberries.ru/en/knowledge-base/articles/019d49a4-1320-71bb-9dac-8ba07e7177ce/working-with-items) — include existing media links when submitting media updates.
- [Ozon Seller API documentation](https://docs.ozon.ru/api/seller/) — `/v3/product/import` and `/v4/product/info/attributes`; the video-gallery complex-attribute mapping (`100001`, `21841`, `21837`) is documented through the Ozon Developer Forum rather than a complete video-specific API schema. Video-specific maximums and processing/replacement semantics are not fully stated in the public API schema.

The endpoint payloads, media selection, URL suffix, size/type checks, failure handling, and gallery preservation have fake/unit-test coverage. No live Ozon or Wildberries seller API call has been made. In particular, Ozon video processing and replacement behavior and Wildberries media-save replacement behavior still require an authorized real-account verification before being described as live-verified.

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

Wildberries also imports typed video when its API supplies a downloadable asset. Source review confirms that OpenCart maps only `ImageUrls`, WooCommerce's archive path accepts only allowlisted image extensions, and Medusa exposes an image-URL field only. Their upstream/bridge video contracts have not been confirmed, so no video support is inferred from image fields or arbitrary metadata. Avito listing publication remains outside the adapter's supported contract.
