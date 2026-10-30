/**
 * Resolve API errors by their stable machine code, never by their server-authored text.
 * Accepts Axios errors, API payloads and plain error response bodies.
 */
export function getApiErrorText(
  error,
  translate,
  fallbackKey = "apiErrors.default",
) {
  const localize = typeof translate === "function" ? translate : (key) => key;
  const code = getApiErrorCode(error);

  if (code) {
    const key = `apiErrors.${code}`;
    const translated = localize(key);
    if (translated !== key) return translated;
  }

  const fallback = localize(fallbackKey);
  return fallback || "Something went wrong. Please try again.";
}

/** Extract a stable code from standard API envelopes, including legacy string errors. */
export function getApiErrorCode(error) {
  const body = error?.response?.data ?? error?.data ?? error;
  const payload = body?.error ?? body;
  if (typeof payload?.code === "string") return payload.code;
  if (typeof payload === "string" && /^[a-z][a-z0-9_]*$/i.test(payload)) {
    return payload;
  }
  return "";
}
