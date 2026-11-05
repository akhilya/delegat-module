import { toast } from "vue-sonner";
import { getLocale, t } from "@/i18n";
import { getApiErrorText } from "@/lib/apiErrorText";

/** Stable API error code from an Axios error or payload. */
export function apiErrorCode(errorOrPayload) {
  const payload = errorOrPayload?.response?.data ?? errorOrPayload;
  if (!payload || typeof payload !== "object") return "";
  return payload.code || payload.error?.code || "";
}

/**
 * Resolve a localized API error title from response payload.
 * Prefers stable `code` → localized error catalog; never displays server-authored
 * messages, which may be in a different language from the active UI.
 */
export function resolveApiErrorMessage(
  payload,
  fallbackKey = "errors.generic",
) {
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

  return getApiErrorText(
    payload,
    (key, values) => t(key, values ?? params),
    fallbackKey,
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
  if (status === 403 && apiErrorCode(error) === "cabinet_view_only") return;

  if (status >= 500) {
    toast.error(t("notifications.serverError"), {
      id: "api-server-error",
      description: t("notifications.tryLater"),
    });
    return;
  }

  // Codes carry user-facing meaning; raw details belong in logs or field errors.
  toast.error(
    resolveApiErrorMessage(error?.response?.data, "notifications.failed"),
  );
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

function fieldUnitForCount(count) {
  const n = Number(count) || 0;
  if (getLocale() !== "ru") {
    return t(
      n === 1
        ? "productMarketplaceIntegrations.fieldUnitOne"
        : "productMarketplaceIntegrations.fieldUnitMany",
    );
  }
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod100 >= 11 && mod100 <= 14) {
    return t("productMarketplaceIntegrations.fieldUnitMany");
  }
  if (mod10 === 1) return t("productMarketplaceIntegrations.fieldUnitOne");
  if (mod10 >= 2 && mod10 <= 4) {
    return t("productMarketplaceIntegrations.fieldUnitFew");
  }
  return t("productMarketplaceIntegrations.fieldUnitMany");
}

/**
 * Compact success after marketplace field import. Preview already listed the fields.
 * @param {unknown} updatedFields
 */
export function displayImportFieldsUpdated(updatedFields) {
  const count = Array.isArray(updatedFields) ? updatedFields.length : 0;
  if (count <= 0) {
    displaySonnerSuccess(t("productMarketplaceIntegrations.noChangesFound"));
    return;
  }
  toast.success(t("productMarketplaceIntegrations.importApplied"), {
    description: t("productMarketplaceIntegrations.updatedFieldsCount", {
      count,
      unit: fieldUnitForCount(count),
    }),
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
