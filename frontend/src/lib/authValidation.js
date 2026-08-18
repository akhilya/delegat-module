import { z } from "zod";
import { FIELD_LIMITS } from "./fieldLimits.js";

/** ASP.NET Identity default AllowedUserNameCharacters */
export const USERNAME_ALLOWED_PATTERN = /^[a-zA-Z0-9\-._@+]+$/;

export function usernameSchema(t) {
  return z
    .string()
    .min(4, { message: t("auth.userNameTooShort") })
    .max(FIELD_LIMITS.userName, { message: t("auth.userNameTooLong") })
    .regex(USERNAME_ALLOWED_PATTERN, {
      message: t("auth.userNameInvalidChars"),
    });
}

/**
 * @param {Function} t
 * @param {{ requirePolicy?: boolean }} [options] — digit + lowercase (sign-up / password change)
 */
export function passwordSchema(t, { requirePolicy = false } = {}) {
  let schema = z
    .string()
    .min(8, { message: t("auth.passwordTooShort") })
    .max(FIELD_LIMITS.password, { message: t("auth.passwordTooLong") });

  if (requirePolicy) {
    schema = schema
      .regex(/[0-9]/, { message: t("auth.passwordNeedsDigit") })
      .regex(/[a-z]/, { message: t("auth.passwordNeedsLowercase") });
  }

  return schema;
}

/**
 * Map API field errors that are stable codes (e.g. username_invalid) to i18n strings.
 * Leaves unknown messages unchanged.
 */
export function translateApiFieldErrors(errors, t) {
  if (!errors || typeof errors !== "object") return errors;

  const out = {};
  for (const [field, value] of Object.entries(errors)) {
    const list = Array.isArray(value) ? value : [value];
    const translated = list.map((msg) => {
      if (typeof msg !== "string") return msg;
      const key = `errors.${msg}`;
      const text = t(key);
      return text !== key ? text : msg;
    });
    out[field] = translated.length === 1 ? translated[0] : translated;
  }
  return out;
}
