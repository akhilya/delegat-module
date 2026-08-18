/**
 * Folder-archive ZIP download URL.
 * Use a native browser navigation (anchor click), not axios: the shared
 * client times out at 30s and would buffer the whole file in memory.
 */
export function artifactDownloadUrl(apiBaseUrl, operationId) {
  const base = String(apiBaseUrl ?? "/api").replace(/\/+$/, "") || "/api";
  const id = encodeURIComponent(String(operationId ?? ""));
  return `${base}/operations/${id}/artifact`;
}

export function triggerBrowserDownload(url, filename) {
  const a = document.createElement("a");
  a.href = url;
  if (filename) a.setAttribute("download", filename);
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
}
