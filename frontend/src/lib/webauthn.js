import { startAuthentication, startRegistration } from "@simplewebauthn/browser";

/** @param {unknown} err */
export function mapWebAuthnError(err) {
  const name = err && typeof err === "object" && "name" in err ? String(err.name) : "";
  if (name === "NotAllowedError" || name === "AbortError") return "canceled";
  if (name === "InvalidStateError") return "alreadyRegistered";
  return "failed";
}

/** @param {Record<string, unknown>} optionsJSON */
export function createPasskey(optionsJSON) {
  return startRegistration({ optionsJSON });
}

/** @param {Record<string, unknown>} optionsJSON
 *  @param {{ useBrowserAutofill?: boolean }} [opts] */
export function assertPasskey(optionsJSON, { useBrowserAutofill = false } = {}) {
  return startAuthentication({ optionsJSON, useBrowserAutofill });
}
