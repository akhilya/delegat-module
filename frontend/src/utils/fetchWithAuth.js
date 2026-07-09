let refreshPromise = null;

export async function tryRefresh() {
  if (refreshPromise) return refreshPromise;
  refreshPromise = fetch("/auth/refresh", {
    method: "POST",
    credentials: "include",
    headers: { "X-CSRF": "1" },
  })
    .then((r) => { refreshPromise = null; return r.ok; })
    .catch(() => { refreshPromise = null; return false; });
  return refreshPromise;
}

export async function fetchWithAuth(url, options = {}) {
  const res = await fetch(url, { ...options, credentials: "include" });
  if (res.status !== 401) return res;

  const refreshed = await tryRefresh();
  if (refreshed) return fetch(url, { ...options, credentials: "include" });
  throw new Error("Session expired");
}
