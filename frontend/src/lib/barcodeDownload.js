import JsBarcode from "jsbarcode";

/**
 * Renders an EAN barcode to a PNG file and triggers a browser download.
 * @param {string} code
 * @param {"EAN8"|"EAN13"} format
 */
export function downloadBarcodePng(code, format) {
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
      const anchor = document.createElement("a");
      anchor.href = URL.createObjectURL(blob);
      anchor.download = `barcode-${code}.png`;
      anchor.click();
      URL.revokeObjectURL(anchor.href);
    }, "image/png");

    URL.revokeObjectURL(url);
  };
  img.src = url;
}