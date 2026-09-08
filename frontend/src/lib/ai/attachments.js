export const MAX_FOLDER_FILES = 500;
export const ATTACHMENT_PREVIEW_LIMIT = 6;

/**
 * @param {FileList | File[] | null | undefined} fileList
 * @returns {Array<{ relativePath: string, fileName: string, size: number, file: File }>}
 */
export function filesFromDirectoryInput(fileList) {
  const list = Array.from(fileList || []);
  const out = [];
  for (const file of list) {
    const relativePath = String(file.webkitRelativePath || file.name || "")
      .replaceAll("\\", "/")
      .replace(/^\/+/, "");
    if (!relativePath || relativePath.split("/").includes("..")) continue;
    out.push({
      relativePath,
      fileName: file.name,
      size: file.size,
      file,
    });
  }
  out.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
  return out.slice(0, MAX_FOLDER_FILES);
}

/**
 * @param {Array<{ relativePath: string }>} current
 * @param {Array<{ relativePath: string }>} extra
 */
export function mergeAttachedFiles(current, extra) {
  const byPath = new Map();
  for (const item of [...(current || []), ...(extra || [])]) {
    if (!item?.relativePath) continue;
    byPath.set(item.relativePath, item);
  }
  return [...byPath.values()].slice(0, MAX_FOLDER_FILES);
}

/** Names/sizes only — never the File blob (history JSON, sent bubbles). */
export function attachmentMetaList(files) {
  return (files || [])
    .filter((f) => f?.relativePath || f?.fileName)
    .map((f) => {
      const relativePath = String(f.relativePath || f.fileName || "");
      const row = {
        relativePath,
        fileName: String(f.fileName || relativePath.split("/").pop() || relativePath),
        size: Number(f.size) || 0,
      };
      const id = Number(f.id) || 0;
      if (id > 0) row.id = id;
      return row;
    });
}

/** Merge server staging ids onto the names already shown in the thread. */
export function applyStagedIds(meta, staged) {
  const byPath = new Map();
  for (const item of staged || []) {
    const relativePath = String(item?.relativePath || item?.RelativePath || "");
    const id = Number(item?.id ?? item?.Id) || 0;
    if (relativePath && id > 0) byPath.set(relativePath, id);
  }
  return (meta || []).map((file) => {
    const id = byPath.get(file.relativePath) || Number(file.id) || 0;
    if (id > 0) return { ...file, id };
    return {
      relativePath: file.relativePath,
      fileName: file.fileName,
      size: file.size,
    };
  });
}

export function attachmentPreview(files, limit = ATTACHMENT_PREVIEW_LIMIT) {
  const list = files || [];
  const cap = Math.max(1, limit);
  return {
    shown: list.slice(0, cap),
    hidden: Math.max(0, list.length - cap),
    total: list.length,
  };
}
