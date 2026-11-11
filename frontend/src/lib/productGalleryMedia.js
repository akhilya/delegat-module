import { isGalleryMedia } from "./mediaKind.js";

/** Storage media not yet selected in the gallery; preserve MIME metadata for video previews. */
export function availableGalleryMedia(files = [], selected = []) {
  const selectedIds = new Set(selected.map((item) => Number(item.fileId)));
  return files
    .filter(
      (file) =>
        isGalleryMedia(file.contentType) && !selectedIds.has(Number(file.id)),
    )
    .map((file) => ({
      fileId: Number(file.id),
      contentType: file.contentType,
      displayName: file.fileName || file.displayName || file.name,
      previewUrl: file.previewUrl,
    }));
}

/** Hit-test the painted image, not the full object-contain element's letterbox. */
export function isOutsideContainedImage(
  point,
  bounds,
  naturalWidth,
  naturalHeight,
) {
  if (!(
    naturalWidth > 0 &&
    naturalHeight > 0 &&
    bounds.width > 0 &&
    bounds.height > 0
  ))
    return true;
  const scale = Math.min(
    bounds.width / naturalWidth,
    bounds.height / naturalHeight,
  );
  const width = naturalWidth * scale;
  const height = naturalHeight * scale;
  const left = bounds.left + (bounds.width - width) / 2;
  const top = bounds.top + (bounds.height - height) / 2;
  return (
    point.clientX < left ||
    point.clientX > left + width ||
    point.clientY < top ||
    point.clientY > top + height
  );
}
