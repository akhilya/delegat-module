import { toast } from "vue-sonner";
import { t } from "@/i18n";

/**
 * Resolve a localized API error title from response payload.
 * Prefers stable `code` → errors.* i18n key; falls back to English title/message.
 */
export function resolveApiErrorMessage(payload, fallbackKey = "errors.generic") {
  if (!payload) return t(fallbackKey);

  const code = payload.code || payload.error?.code;
  const data = payload.data || payload.error?.details || payload.details;
  const params =
    data && typeof data === "object" && !Array.isArray(data) ? data : {};

  if (code) {
    const key = `errors.${code}`;
    const translated = t(key, params);
    if (translated !== key) return translated;
  }

  return (
    payload.title ||
    payload.message ||
    payload.error?.message ||
    t(fallbackKey)
  );
}

/**
 * Displays an error toast notification from an API error response.
 * @param {Object} error - The error object, typically from an Axios response.
 *
 * Skips 409 / 429: the axios response interceptor already toasts those globally
 * (see main.js). Callers still pass the error for form.setErrors etc.
 */
export function displaySonnerError(error) {
  if (typeof error === "string") {
    toast.error(error);
    return;
  }

  const status = error?.response?.status;
  if (status === 409 || status === 429) return;

  const payload = error?.response?.data;
  if (!payload) {
    toast.error(error?.message || t("errors.generic"));
    return;
  }

  const title = resolveApiErrorMessage(payload);
  const description = formatErrorDetails(payload);
  toast.error(title, description ? { description } : undefined);
}

function formatErrorDetails(payload) {
  const details =
    payload.detail ||
    payload.details ||
    payload.data ||
    payload.errors ||
    payload.error?.details;

  if (!details) return null;

  if (typeof details === "string") return truncateDetails(details);

  if (Array.isArray(details)) {
    return truncateDetails(details.map(String).join("\n"));
  }

  if (typeof details === "object") {
    // Prefer not to dump code params as description when they are i18n params
    if (payload.code && !payload.detail && !payload.errors) {
      const keys = Object.keys(details);
      const looksLikeParams = keys.every(
        (k) => typeof details[k] === "string" || typeof details[k] === "number",
      );
      if (looksLikeParams && keys.length <= 4) return null;
    }
    try {
      return truncateDetails(JSON.stringify(details, null, 2));
    } catch {
      return t("errors.detailsUnreadable");
    }
  }

  return truncateDetails(String(details));
}

function truncateDetails(value) {
  const text = value.trim();
  if (text.length <= 1200) return text;
  return `${text.slice(0, 1200)}...`;
}

/**
 * Displays a success toast notification with an optional action button.
 * @param {string} title - The success message to display.
 * @param {Object} [action] - Optional action configuration for the toast (e.g. undo button).
 */
export function displaySonnerSuccess(title, action) {
  toast.success(title, {
    action,
  });
}

/**
 * Extracts data from a useAxios response.
 * useAxios returns data as a Vue ref, so we need to access .value.
 * @param {Object} response - The response from useAxios.
 * @param {*} [defaultValue=null] - Default value if no data is present.
 * @returns {*} The extracted data.
 */
export function extractResponseData(response, defaultValue = null) {
  const data = response?.data;
  if (data === undefined || data === null) return defaultValue;
  // If data is a Vue ref (has a value property and __v_isRef)
  if (data.__v_isRef && data.value !== undefined) {
    return data.value ?? defaultValue;
  }
  return data ?? defaultValue;
}
