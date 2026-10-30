import test from "node:test";
import assert from "node:assert/strict";
import { getApiErrorCode, getApiErrorText } from "./apiErrorText.js";

const translations = {
  "apiErrors.connection_not_found": "Connection not found.",
  "apiErrors.default": "Something went wrong.",
};
const translate = (key) => translations[key] ?? key;

test("localizes public API and standard API error envelopes by code", () => {
  assert.equal(
    getApiErrorText(
      {
        response: {
          data: {
            error: {
              code: "connection_not_found",
              message: "Подключение не найдено",
            },
          },
        },
      },
      translate,
    ),
    "Connection not found.",
  );
  assert.equal(
    getApiErrorText(
      {
        response: {
          data: {
            code: "connection_not_found",
            title: "Подключение не найдено",
          },
        },
      },
      translate,
    ),
    "Connection not found.",
  );
});

test("extracts stable API codes without exposing server-authored error text", () => {
  assert.equal(
    getApiErrorCode({
      response: { data: { code: "plan_not_pending_or_mismatched" } },
    }),
    "plan_not_pending_or_mismatched",
  );
  assert.equal(
    getApiErrorCode({ error: "plan_not_pending_or_mismatched" }),
    "plan_not_pending_or_mismatched",
  );
  assert.equal(getApiErrorCode({ error: "Something went wrong" }), "");
});

test("uses a generic localized message for unknown or missing codes", () => {
  assert.equal(
    getApiErrorText(
      { code: "future_error", message: "Случайный серверный текст" },
      translate,
    ),
    "Something went wrong.",
  );
  assert.equal(
    getApiErrorText(new Error("network failure"), translate),
    "Something went wrong.",
  );
});

test("supports a caller-specific localized fallback", () => {
  const translateWithFallback = (key) =>
    key === "errors.resourceLimit" ? "Resource limit reached." : translate(key);
  assert.equal(
    getApiErrorText(
      { code: "future_error" },
      translateWithFallback,
      "errors.resourceLimit",
    ),
    "Resource limit reached.",
  );
});
