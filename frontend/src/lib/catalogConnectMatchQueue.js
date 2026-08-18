/**
 * Connections Catalog Connect should match after choosing a source catalog
 * (or when the store already has products). Never includes the import source.
 * When local products exist, skip connections that already link every product
 * (typical after importing that marketplace as the source of truth).
 *
 * @param {Array<{ id: number|string, productLinksCount?: number }>} connections
 * @param {{ sourceId?: number|string|null, localProductCount?: number }} [options]
 * @returns {Array<number|string>}
 */
export function catalogConnectMatchQueue(
  connections,
  { sourceId = null, localProductCount = 0 } = {},
) {
  if (!Array.isArray(connections)) return [];
  const source =
    sourceId == null || sourceId === "" ? null : Number(sourceId);
  const count = Number(localProductCount) || 0;
  return connections
    .filter((connection) => {
      const id = Number(connection.id);
      if (source != null && Number.isFinite(source) && id === source) {
        return false;
      }
      if (count > 0 && Number(connection.productLinksCount || 0) >= count) {
        return false;
      }
      return true;
    })
    .map((connection) => connection.id);
}
