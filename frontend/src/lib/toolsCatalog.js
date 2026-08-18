/**
 * Tools catalog helpers. Entries come from GET /tools (platform pages + adapter jobs).
 * @param {object|null|undefined} entry
 * @param {(key: string) => string} t
 */
export function toolTitle(entry, t) {
  if (!entry) return "";
  if (entry.titleKey && typeof t === "function") return t(entry.titleKey);
  return entry.title || entry.id || "";
}

export function toolDescription(entry, t) {
  if (!entry) return "";
  if (entry.descriptionKey && typeof t === "function") return t(entry.descriptionKey);
  return entry.description || "";
}

export function toolHref(entry) {
  if (!entry) return "/tools";
  return entry.path || `/tools/${entry.id}`;
}

export function isJobTool(entry) {
  return entry?.mode === "job";
}

export function isPageTool(entry) {
  return entry?.mode === "page";
}
