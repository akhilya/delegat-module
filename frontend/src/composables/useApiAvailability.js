import { ref, readonly, computed } from "vue";
import axios from "axios";

/** @typedef {"unknown" | "online" | "offline"} ApiAvailabilityStatus */

const HEALTH_TIMEOUT_MS = 5000;
const POLL_DELAYS_MS = [3000, 5000, 8000, 12000, 15000];

const status = ref(/** @type {ApiAvailabilityStatus} */ ("unknown"));
const isChecking = ref(false);
const lastCheckedAt = ref(/** @type {number|null} */ (null));

/** Path to restore after the API comes back. */
let returnTo = null;
let pollTimer = null;
let pollAttempt = 0;
let inFlightProbe = null;
let pollingActive = false;
let navigatingOffline = false;

/** @type {null | (() => import("vue-router").Router)} */
let getRouter = null;
/** @type {null | (() => void)} */
let onRecovered = null;

/**
 * Wire navigation + recovery hooks from main.js (avoids circular imports).
 * @param {{ getRouter: () => import("vue-router").Router, onRecovered?: () => void }} opts
 */
export function configureApiAvailability(opts) {
  getRouter = opts.getRouter;
  onRecovered = opts.onRecovered ?? null;
}

export function isApiUnavailableError(error) {
  if (!error) return false;
  if (error.code === "ERR_CANCELED" || axios.isCancel?.(error)) return false;

  const httpStatus = error.response?.status;
  if (httpStatus === 502 || httpStatus === 503 || httpStatus === 504) {
    return true;
  }
  // Any other HTTP response means the API process (or a proxy with a body) answered.
  if (error.response) return false;

  return (
    error.code === "ERR_NETWORK" ||
    error.code === "ECONNABORTED" ||
    error.message === "Network Error"
  );
}

function healthUrl() {
  const base = import.meta.env.VITE_API_SERVER || "/api";
  return `${String(base).replace(/\/$/, "")}/health`;
}

/**
 * Probe /health with a short timeout (raw axios — no app interceptors/cache).
 * @returns {Promise<boolean>}
 */
export function checkHealth() {
  if (inFlightProbe) return inFlightProbe;

  isChecking.value = true;
  inFlightProbe = axios
    .get(healthUrl(), {
      timeout: HEALTH_TIMEOUT_MS,
      withCredentials: true,
      headers: {
        "Cache-Control": "no-cache",
        "X-CSRF": "1",
      },
      validateStatus: (s) => s >= 200 && s < 300,
    })
    .then(() => {
      status.value = "online";
      lastCheckedAt.value = Date.now();
      return true;
    })
    .catch(() => {
      status.value = "offline";
      lastCheckedAt.value = Date.now();
      return false;
    })
    .finally(() => {
      isChecking.value = false;
      inFlightProbe = null;
    });

  return inFlightProbe;
}

function resolveReturnTo(candidate) {
  if (!candidate || typeof candidate !== "string") return null;
  if (candidate.startsWith("/unavailable")) return null;
  if (!candidate.startsWith("/")) return null;
  return candidate;
}

/**
 * Mark API offline and navigate to the unavailable page (idempotent).
 * @param {string} [fromPath]
 */
export function markOffline(fromPath) {
  status.value = "offline";
  lastCheckedAt.value = Date.now();

  const nextReturn = resolveReturnTo(fromPath);
  if (nextReturn) returnTo = nextReturn;

  const router = getRouter?.();
  if (!router) return;

  const current = router.currentRoute.value;
  if (current.path === "/unavailable" || navigatingOffline) return;

  if (!returnTo) {
    returnTo = resolveReturnTo(current.fullPath);
  }

  navigatingOffline = true;
  router
    .push({
      path: "/unavailable",
      query: returnTo ? { from: returnTo } : {},
    })
    .catch(() => {})
    .finally(() => {
      navigatingOffline = false;
    });
}

/**
 * Called when health succeeds while on the unavailable page.
 * @param {string} [fromQuery]
 */
export async function markOnlineAndRecover(fromQuery) {
  stopPolling();
  status.value = "online";
  lastCheckedAt.value = Date.now();

  const target =
    resolveReturnTo(fromQuery) ||
    resolveReturnTo(returnTo) ||
    "/";
  returnTo = null;

  onRecovered?.();

  const router = getRouter?.();
  if (!router) return;

  if (router.currentRoute.value.path === "/unavailable") {
    await router.replace(target).catch(() => {});
  }
}

export function getReturnTo() {
  return returnTo;
}

export function setReturnTo(path) {
  returnTo = resolveReturnTo(path) ?? returnTo;
}

function scheduleNextPoll(run) {
  const delay =
    POLL_DELAYS_MS[Math.min(pollAttempt, POLL_DELAYS_MS.length - 1)];
  pollAttempt += 1;
  pollTimer = setTimeout(run, delay);
}

/**
 * Start backoff polling until health succeeds. Safe to call repeatedly.
 * @param {{ fromQuery?: string }} [opts]
 */
export function startPolling(opts = {}) {
  if (opts.fromQuery) setReturnTo(opts.fromQuery);

  if (pollingActive) return;

  pollingActive = true;
  pollAttempt = 0;

  const tick = async () => {
    pollTimer = null;
    if (!pollingActive) return;
    const ok = await checkHealth();
    if (!pollingActive) return;
    if (ok) {
      await markOnlineAndRecover(opts.fromQuery);
      return;
    }
    scheduleNextPoll(tick);
  };

  // Immediate first check, then backoff on failure.
  tick();
}

export function stopPolling() {
  pollingActive = false;
  if (pollTimer) {
    clearTimeout(pollTimer);
    pollTimer = null;
  }
  pollAttempt = 0;
}

/** Manual CTA on the unavailable page. */
export async function checkNow(fromQuery) {
  if (fromQuery) setReturnTo(fromQuery);
  const ok = await checkHealth();
  if (ok) {
    await markOnlineAndRecover(fromQuery);
    return true;
  }
  // Ensure polling continues after a manual miss.
  if (!pollTimer && status.value === "offline") {
    startPolling({ fromQuery });
  }
  return false;
}

export function getAvailabilityStatus() {
  return status.value;
}

export function useApiAvailability() {
  return {
    status: readonly(status),
    isChecking: readonly(isChecking),
    lastCheckedAt: readonly(lastCheckedAt),
    isOffline: computed(() => status.value === "offline"),
    checkHealth,
    checkNow,
    markOffline,
    startPolling,
    stopPolling,
  };
}
