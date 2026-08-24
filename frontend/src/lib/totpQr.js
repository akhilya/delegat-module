import QRCode from "qrcode";

/**
 * Render an otpauth:// URI as SVG. Local generation only — never Google Charts.
 * @param {string} otpauthUri
 * @returns {Promise<string>}
 */
export async function otpauthQrSvg(otpauthUri) {
  if (typeof otpauthUri !== "string" || !otpauthUri.startsWith("otpauth://")) {
    throw new Error("otpauth URI required");
  }
  return QRCode.toString(otpauthUri, {
    type: "svg",
    margin: 1,
    errorCorrectionLevel: "M",
    color: { dark: "#171717", light: "#ffffff" },
  });
}
