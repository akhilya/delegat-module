const MIME_SUBTYPE = /^[a-z0-9][a-z0-9.+-]*$/;

/** Classifies media using normalized MIME metadata only; filenames are never a fallback. */
export function getMediaKind(contentType) {
  const mime = String(contentType || "")
    .split(";", 1)[0]
    .trim()
    .toLowerCase();
  const slash = mime.indexOf("/");
  if (slash < 1) return "file";

  const [type, subtype] = [mime.slice(0, slash), mime.slice(slash + 1)];
  if (!MIME_SUBTYPE.test(subtype)) return "file";
  if (type === "image") return "image";
  if (type === "video") return "video";
  return "file";
}

export function isGalleryMedia(contentType) {
  const kind = getMediaKind(contentType);
  return kind === "image" || kind === "video";
}
