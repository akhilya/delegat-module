import { toast } from "vue-sonner";

/**
 * Displays an error toast notification from an API error response.
 * @param {Object} error - The error object, typically from an Axios response.
 */
export function displaySonnerError(error) {
  if (error?.response?.data) toast.error(error.response.data.title);
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
