export const CATALOG_CONNECT_DISMISS_KEY = "catalog-connect-dismissed-preview";

export function isCatalogConnectJobActive(job) {
  const status = job?.status;
  return status === "pending" || status === "running";
}

export function readDismissedPreviewOpId() {
  try {
    return sessionStorage.getItem(CATALOG_CONNECT_DISMISS_KEY) || "";
  } catch {
    return "";
  }
}

export function writeDismissedPreviewOpId(operationId) {
  try {
    if (operationId) sessionStorage.setItem(CATALOG_CONNECT_DISMISS_KEY, operationId);
    else sessionStorage.removeItem(CATALOG_CONNECT_DISMISS_KEY);
  } catch {
    /* ignore quota / private mode */
  }
}

export function shouldRestorePreview(job, dismissedOpId) {
  if (!job || job.kind !== "match" || job.status !== "completed" || !job.preview) return false;
  if (dismissedOpId && dismissedOpId === job.operationId) return false;
  return true;
}
