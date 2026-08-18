import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { baseCompile } from "@intlify/message-compiler";
import en from "../locales/en.json" with { type: "json" };
import ru from "../locales/ru.json" with { type: "json" };

function walkStrings(value, prefix = "") {
  /** @type {Array<[string, string]>} */
  const out = [];
  if (typeof value === "string") {
    out.push([prefix, value]);
    return out;
  }
  if (Array.isArray(value)) {
    value.forEach((item, i) =>
      out.push(...walkStrings(item, `${prefix}[${i}]`)),
    );
    return out;
  }
  if (value && typeof value === "object") {
    for (const [k, v] of Object.entries(value)) {
      const key = prefix ? `${prefix}.${k}` : k;
      out.push(...walkStrings(v, key));
    }
  }
  return out;
}

function compileErrors(messages, locale) {
  const errors = [];
  for (const [key, source] of walkStrings(messages)) {
    const collected = [];
    baseCompile(source, {
      onError(err) {
        collected.push(err.message || String(err));
      },
    });
    for (const message of collected) {
      errors.push(`${locale} ${key}: ${message}\n  ${source}`);
    }
  }
  return errors;
}

function mentionsControlPlane(messages, locale) {
  return walkStrings(messages)
    .filter(([, source]) => /control plane/i.test(source))
    .map(([key, source]) => `${locale} ${key}: ${source}`);
}

describe("locale message catalogs", () => {
  it("compile without vue-i18n syntax errors", () => {
    const errors = [...compileErrors(ru, "ru"), ...compileErrors(en, "en")];
    assert.deepEqual(errors, []);
  });

  it("does not mention Control Plane in user-facing copy", () => {
    assert.deepEqual(
      [...mentionsControlPlane(ru, "ru"), ...mentionsControlPlane(en, "en")],
      [],
    );
  });
});
