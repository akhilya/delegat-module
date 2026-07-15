/** @typedef {{ id: string, desc: boolean }} SortColumn */

/** @returns {SortColumn[]} */
export function parseSortFromQuery(query = {}) {
  const raw = query.sort;
  if (typeof raw === "string" && raw.trim()) {
    return raw
      .split(",")
      .map((part) => {
        const [id, dir] = part.split(":");
        const columnId = id?.trim();
        if (!columnId) return null;
        return { id: columnId, desc: (dir?.trim() || "desc") !== "asc" };
      })
      .filter(Boolean);
  }

  if (query.sortBy) {
    return [{
      id: String(query.sortBy),
      desc: query.sortDesc !== "false",
    }];
  }

  return [];
}

/** @param {SortColumn[]} columns */
export function serializeSortToQuery(columns) {
  if (!columns?.length) return undefined;
  return columns.map((c) => `${c.id}:${c.desc ? "desc" : "asc"}`).join(",");
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
      if (list[index].desc) {
        list[index] = { ...list[index], desc: false };
      } else {
        list.splice(index, 1);
      }
    } else {
      list.push({ id: columnId, desc: true });
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