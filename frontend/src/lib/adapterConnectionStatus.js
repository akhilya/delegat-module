/**
 * Shared status / mapping helpers for product↔adapter UI
 * (Adapters tab + products table Adapters column).
 */
import { t } from "@/i18n";

export const EXPORT_MODE_FULL = "full";
export const EXPORT_MODE_PRICE = "price";
export const EXPORT_MODE_STOCK = "stock";

/** @param {unknown} mode */
export function normalizeExportMode(mode) {
  if (mode === EXPORT_MODE_PRICE || mode === EXPORT_MODE_STOCK) return mode;
  return EXPORT_MODE_FULL;
}

/** @param {string} mode */
export function exportModeLabel(mode) {
  if (mode === EXPORT_MODE_PRICE) {
    return t("productMarketplaceIntegrations.menuExportPrice");
  }
  if (mode === EXPORT_MODE_STOCK) {
    return t("productMarketplaceIntegrations.menuExportStock");
  }
  return t("productMarketplaceIntegrations.menuExportFull");
}

/**
 * @param {object|null|undefined} conn available-connections row or list adapterLink
 */
export function hasMappingIssue(conn) {
  if (!conn) return false;
  if (conn.mappingMissing) return true;
  if (conn.hasMapping === false) return true;
  return false;
}

/**
 * Human-readable status for tooltips / menu subtitle.
 * @param {object|null|undefined} conn
 * @param {{ categoryName?: string|null }} [opts]
 */
export function adapterConnectionStatusLabel(conn, opts = {}) {
  if (!conn) return t("products.adaptersColumn.notLinked");

  if (conn.mappingMissing) {
    return t("productMarketplaceIntegrations.noMapping");
  }
  if (conn.hasMapping === false) {
    return opts.categoryName
      ? t("productMarketplaceIntegrations.mappingNotConfigured", {
          category: opts.categoryName,
        })
      : t("productMarketplaceIntegrations.noMapping");
  }

  const status = conn.linkStatus || conn.status || null;
  if (!status) return t("products.adaptersColumn.notLinked");

  const key = {
    Synced: "productMarketplaceIntegrations.statusSynced",
    NotSynced: "productMarketplaceIntegrations.statusNotSynced",
    SyncError: "productMarketplaceIntegrations.statusError",
    Modified: "productMarketplaceIntegrations.statusModified",
    RemoteMissing: "productMarketplaceIntegrations.statusBroken",
  }[status];
  return key ? t(key) : status;
}

/**
 * Icon button color classes for the products table Adapters cell.
 * @param {object|null|undefined} conn
 */
export function adapterConnectionButtonClass(conn) {
  const base =
    "size-8 shrink-0 rounded-md border bg-background hover:bg-muted/60";
  if (hasMappingIssue(conn)) {
    return `${base} text-destructive border-destructive/40 bg-destructive/10`;
  }
  const status = conn?.linkStatus || conn?.status || null;
  if (!status) {
    return `${base} text-muted-foreground border-border`;
  }
  switch (status) {
    case "Synced":
      return `${base} text-success border-success/40 bg-success-muted/50`;
    case "Modified":
      return `${base} text-warning border-warning/40 bg-warning-muted/50`;
    case "SyncError":
    case "RemoteMissing":
      return `${base} text-destructive border-destructive/40 bg-destructive/10`;
    case "NotSynced":
    default:
      return `${base} text-muted-foreground border-border`;
  }
}

/**
 * Badge metadata for the product Adapters tab.
 * @param {object} conn
 * @param {Record<string, object>} icons { CheckCircle, AlertTriangle, XCircle, Unlink }
 */
export function getConnectionStatusInfo(conn, icons = {}) {
  if (conn.mappingMissing) {
    return {
      variant: "destructive",
      icon: icons.AlertTriangle || null,
      label: t("productMarketplaceIntegrations.noMapping"),
      color: "text-destructive",
    };
  }
  switch (conn.linkStatus) {
    case "Synced":
      return {
        variant: "default",
        icon: icons.CheckCircle || null,
        label: t("productMarketplaceIntegrations.statusSynced"),
        color: "text-success",
      };
    case "NotSynced":
      return {
        variant: "secondary",
        icon: icons.AlertTriangle || null,
        label: t("productMarketplaceIntegrations.statusNotSynced"),
        color: "text-muted-foreground",
      };
    case "SyncError":
      return {
        variant: "destructive",
        icon: icons.XCircle || null,
        label: t("productMarketplaceIntegrations.statusError"),
        color: "text-destructive",
      };
    case "Modified":
      return {
        variant: "outline",
        icon: icons.AlertTriangle || null,
        label: t("productMarketplaceIntegrations.statusModified"),
        color: "text-warning",
      };
    case "RemoteMissing":
      return {
        variant: "destructive",
        icon: icons.Unlink || null,
        label: t("productMarketplaceIntegrations.statusBroken"),
        color: "text-destructive",
      };
    default:
      return {
        variant: "secondary",
        icon: null,
        label: conn.linkStatus || t("products.adaptersColumn.notLinked"),
        color: "text-muted-foreground",
      };
  }
}

/**
 * Menu mode for a connection row.
 * @param {object} conn
 * @returns {'missingMapping'|'linked'|'ready'}
 */
export function adapterConnectionMenuMode(conn) {
  if (!conn) return "missingMapping";
  if (conn.mappingMissing || conn.hasMapping === false) return "missingMapping";
  if (conn.isLinked) return "linked";
  return "ready";
}
