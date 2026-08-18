import { http } from "@/main";
import {
  parseIncompleteStore,
  parseOnboardingIncomplete,
  parseStoreCount,
} from "@/lib/setupGate";

/** @type {{ count: number, onboardingIncomplete: boolean, incompleteStore: { id: number, name: string } | null, at: number } | null} */
let cache = null;
const TTL_MS = 15_000;

export function clearSetupStoreCountCache() {
  cache = null;
}

/**
 * Platform store count + onboarding flag for first-run gate.
 * @returns {Promise<{ count: number|null, onboardingIncomplete: boolean, incompleteStore: { id: number, name: string } | null }>}
 */
export async function fetchSetupState({ force = false } = {}) {
  if (!force && cache && Date.now() - cache.at < TTL_MS) {
    return {
      count: cache.count,
      onboardingIncomplete: cache.onboardingIncomplete,
      incompleteStore: cache.incompleteStore,
    };
  }
  try {
    const response = await http.get("/stores/", { cache: false });
    const count = parseStoreCount(response.data);
    const onboardingIncomplete = parseOnboardingIncomplete(response.data) === true;
    const incompleteStore = parseIncompleteStore(response.data);
    if (count != null) {
      cache = {
        count,
        onboardingIncomplete,
        incompleteStore,
        at: Date.now(),
      };
    }
    return { count, onboardingIncomplete, incompleteStore };
  } catch {
    return { count: null, onboardingIncomplete: false, incompleteStore: null };
  }
}

/**
 * Platform store count for first-run gate (admin / stores.create).
 * @returns {Promise<number|null>}
 */
export async function fetchSetupStoreCount({ force = false } = {}) {
  const state = await fetchSetupState({ force });
  return state.count;
}
