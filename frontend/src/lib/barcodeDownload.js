import JsBarcode from "jsbarcode";

/**
 * @param {string} code
 * @returns {"EAN8"|"EAN13"}
 */
export function barcodeFormatFor(code) {
  return String(code || "").length === 8 ? "EAN8" : "EAN13";
}

/**
 * Renders an EAN barcode to a PNG Blob.
 * @param {string} code
 * @param {"EAN8"|"EAN13"} [format]
 * @returns {Promise<Blob|null>}
 */
export function barcodeToPngBlob(code, format = barcodeFormatFor(code)) {
  return new Promise((resolve) => {
    const tempSvg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    try {
      JsBarcode(tempSvg, code, {
        format,
        height: 150,
        displayValue: true,
        margin: 10,
        fontSize: 28,
        width: 3,
      });
    } catch {
      resolve(null);
      return;
    }

    const svgData = new XMLSerializer().serializeToString(tempSvg);
    const svgBlob = new Blob([svgData], { type: "image/svg+xml;charset=utf-8" });
    const url = URL.createObjectURL(svgBlob);

    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement("canvas");
      canvas.width = img.width * 2;
      canvas.height = img.height * 2;
      const ctx = canvas.getContext("2d");
      ctx.fillStyle = "#ffffff";
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(img, 0, 0, canvas.width, canvas.height);

      canvas.toBlob((blob) => {
        URL.revokeObjectURL(url);
        resolve(blob);
      }, "image/png");
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      resolve(null);
    };
    img.src = url;
  });
}

/**
 * Renders an EAN barcode to a PNG file and triggers a browser download.
 * @param {string} code
 * @param {"EAN8"|"EAN13"} [format]
 */
export async function downloadBarcodePng(code, format = barcodeFormatFor(code)) {
  const blob = await barcodeToPngBlob(code, format);
  if (!blob) return;
  const anchor = document.createElement("a");
  anchor.href = URL.createObjectURL(blob);
  anchor.download = `barcode-${code}.png`;
  anchor.click();
  URL.revokeObjectURL(anchor.href);
}

/**
 * @param {string} code
 * @param {{ id?: number|string, sku?: string }} [meta]
 * @param {Set<string>} usedNames
 */
function zipEntryName(code, meta, usedNames) {
  let base = `barcode-${code}.png`;
  if (!usedNames.has(base)) {
    usedNames.add(base);
    return base;
  }
  const suffix = meta?.sku || meta?.id || usedNames.size;
  base = `barcode-${code}-${suffix}.png`;
  let n = 1;
  while (usedNames.has(base)) {
    base = `barcode-${code}-${suffix}-${n}.png`;
    n += 1;
  }
  usedNames.add(base);
  return base;
}

/**
 * Builds a ZIP of PNG barcodes and triggers a browser download.
 * @param {Array<{ barcode: string, id?: number|string, sku?: string, name?: string }>} entries
 * @param {string} [zipName]
 * @returns {Promise<number>} Number of files added to the ZIP
 */
export async function downloadBarcodesZip(entries, zipName = "barcodes.zip") {
  const list = (entries || []).filter((e) => e?.barcode);
  if (list.length === 0) return 0;

  const { default: JSZip } = await import("jszip");
  const zip = new JSZip();
  const usedNames = new Set();
  let added = 0;

  for (const entry of list) {
    const code = String(entry.barcode);
    const blob = await barcodeToPngBlob(code, barcodeFormatFor(code));
    if (!blob) continue;
    zip.file(zipEntryName(code, entry, usedNames), blob);
    added += 1;
  }

  if (added === 0) return 0;

  const zipBlob = await zip.generateAsync({ type: "blob" });
  const anchor = document.createElement("a");
  anchor.href = URL.createObjectURL(zipBlob);
  anchor.download = zipName;
  anchor.click();
  URL.revokeObjectURL(anchor.href);
  return added;
}
