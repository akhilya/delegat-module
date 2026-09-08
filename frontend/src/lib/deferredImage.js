/**
 * In-thumb photo state. Lists (catalog, orders, scrape) must paint as soon as
 * row JSON is in; the <img> owns loading="lazy" / decoding="async" and a Spinner.
 * Never prefetch with `new Image()` — that starts every thumb on the page at once.
 */

export function imagePaintState(src, img) {
  if (!src) return { isLoading: false, failed: false };
  if (!img || !img.complete) return { isLoading: true, failed: false };
  if (img.naturalWidth > 0) return { isLoading: false, failed: false };
  return { isLoading: false, failed: true };
}
