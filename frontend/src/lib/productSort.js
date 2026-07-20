/** @typedef {{ id: string, desc: boolean }} SortColumn */

function parseSortPart(part) {
  const trimmed = part?.trim();
  if (!trimmed) return null;
  const colon = trimmed.indexOf(":");
  if (colon <= 0) return null;
  const id = trimmed.slice(0, colon).trim();
  const dir = trimmed.slice(colon + 1).trim().toLowerCase();
  if (!id) return null;
  return { id, desc: dir !== "asc" };
}

/** @param {string | string[] | undefined} raw */
function parseSortParts(raw) {
  if (!raw) return [];
  const items = Array.isArray(raw) ? raw : [raw];
  const parts = [];
  for (const item of items) {
    if (typeof item !== "string" || !item.trim()) continue;
    for (const segment of item.split(/[,;|]/)) {
      const parsed = parseSortPart(segment);
      if (parsed) parts.push(parsed);
    }
  }
  return parts.slice(0, 5);
}

/** @returns {SortColumn[]} */
export function parseSortFromQuery(query = {}) {
  const fromSort = parseSortParts(query.sort);
  if (fromSort.length) return fromSort;

  if (query.sortBy) {
    return [{
      id: String(query.sortBy),
      desc: query.sortDesc !== "false",
    }];
  }

  return [];
}

/** @param {SortColumn[]} columns @returns {string | undefined} */
export function serializeSortToQuery(columns) {
  if (!columns?.length) return undefined;
  return columns.map((c) => `${c.id}:${c.desc ? "desc" : "asc"}`).join("|");
}

/** @param {SortColumn[]} columns */
export function serializeSortKey(columns) {
  return serializeSortToQuery(columns) ?? "";
}

/**
 * @param {SortColumn[]} columns
 * @param {string} columnId
 * @param {boolean} shiftKey
 * @returns {SortColumn[]}
 */
export function toggleSortColumn(columns, columnId, shiftKey = false) {
  const list = [...(columns || [])];
  const index = list.findIndex((c) => c.id === columnId);

  if (shiftKey) {
    if (index >= 0) {
      list[index] = { ...list[index], desc: !list[index].desc };
    } else {
      // Default to ASC — it more often produces a visible tiebreaker effect (DESC frequently matches Id order).
      list.push({ id: columnId, desc: false });
    }
    return list;
  }

  if (index === 0 && list.length === 1) {
    if (list[0].desc) {
      return [{ id: columnId, desc: false }];
    }
    return [];
  }

  return [{ id: columnId, desc: true }];
}

/** @param {SortColumn[]} columns @param {string} columnId */
export function getSortIndex(columns, columnId) {
  return (columns || []).findIndex((c) => c.id === columnId);
}

