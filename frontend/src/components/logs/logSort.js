/** Default log list sort (must match backend default). */
export const LOG_SORT_DEFAULT = { sortBy: "createdAt", sortDir: "desc" };

/**
 * Cycle sort for a column: inactive → desc → asc → back to default (createdAt desc).
 * Actual ordering is always applied on the server via sortBy/sortDir query params.
 * @param {string} currentBy
 * @param {string} currentDir
 * @param {string} column
 * @returns {{ sortBy: string, sortDir: string }}
 */
export function cycleLogSort(currentBy, currentDir, column) {
  if (currentBy !== column) {
    return { sortBy: column, sortDir: "desc" };
  }
  if (currentDir === "desc") {
    return { sortBy: column, sortDir: "asc" };
  }
  return { ...LOG_SORT_DEFAULT };
}
