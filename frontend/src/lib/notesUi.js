const IMPORTANCE_I18N = {
  Low: "components.note.importanceLow",
  Medium: "components.note.importanceMedium",
  High: "components.note.importanceHigh",
  Critical: "components.note.importanceCritical",
  Emergency: "components.note.importanceEmergency",
};

export function noteId(row) {
  return row?.id ?? row?.Id ?? null;
}

export function notePreviewTitle(row, emptyLabel) {
  const raw = String(row?.text ?? "")
    .replace(/\r\n/g, "\n")
    .trim();
  if (!raw) return emptyLabel;
  return raw.split("\n")[0].trim() || emptyLabel;
}

export function noteImportanceLabel(value, t) {
  const key = IMPORTANCE_I18N[value];
  if (key) return t(key);
  return value || t("common.emDash");
}

export function noteImportanceClass(value) {
  if (value === "Critical" || value === "Emergency") {
    return "text-destructive";
  }
  if (value === "High") return "text-warning";
  return "text-muted-foreground";
}

export function noteListCaption(
  row,
  { formatDateTime, t, includeProduct = true },
) {
  const parts = [];
  if (includeProduct) {
    if (row?.productName) parts.push(row.productName);
    if (row?.productSku) parts.push(row.productSku);
    parts.push(row?.authorUserName || t("pages.notesPage.authorUnknown"));
  } else if (row?.authorUserName) {
    parts.push(row.authorUserName);
  }
  const when = formatDateTime?.(row?.updatedAt || row?.createdAt);
  if (when && when !== "-") parts.push(when);
  return parts.join(" · ");
}

export function sortNotesNewestFirst(list) {
  return [...(list || [])].sort((a, b) => {
    const ta = new Date(a?.updatedAt || a?.createdAt || 0).getTime();
    const tb = new Date(b?.updatedAt || b?.createdAt || 0).getTime();
    return tb - ta;
  });
}
