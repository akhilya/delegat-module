import { http } from "@/main";
import { parseStoreCount } from "@/lib/setupGate";

/** @type {{ count: number, at: number } | null} */
let cache = null;
const TTL_MS = 15_000;

export function clearSetupStoreCountCache() {
  cache = null;
}

/**
 * Platform store count for first-run gate (admin / stores.create).
 * @returns {Promise<number|null>}
 */
export async function fetchSetupStoreCount({ force = false } = {}) {
  if (!force && cache && Date.now() - cache.at < TTL_MS) {
    return cache.count;
  }
  try {
    const response = await http.get("/stores/", { cache: false });
    const count = parseStoreCount(response.data);
    if (count != null) {
      cache = { count, at: Date.now() };
    }
    return count;
  } catch {
    return null;
  }
}
