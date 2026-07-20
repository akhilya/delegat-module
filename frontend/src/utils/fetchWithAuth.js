import { getLocale } from "@/i18n";

let refreshPromise = null;

function withLocaleHeaders(options = {}) {
  return {
    ...options,
    credentials: "include",
    headers: {
      ...(options.headers || {}),
      "X-UI-Locale": getLocale(),
    },
  };
}

export async function tryRefresh() {
  if (refreshPromise) return refreshPromise;
  refreshPromise = fetch("/auth/refresh", {
    method: "POST",
    credentials: "include",
    headers: { "X-CSRF": "1", "X-UI-Locale": getLocale() },
  })
    .then((r) => { refreshPromise = null; return r.ok; })
    .catch(() => { refreshPromise = null; return false; });
  return refreshPromise;
}

export async function fetchWithAuth(url, options = {}) {
  const opts = withLocaleHeaders(options);
  const res = await fetch(url, opts);
  if (res.status !== 401) return res;

  const refreshed = await tryRefresh();
  if (refreshed) return fetch(url, withLocaleHeaders(options));
  throw new Error("Session expired");
}
