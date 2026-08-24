import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { mapWebAuthnError } from "./webauthn.js";

describe("mapWebAuthnError", () => {
  it("maps user abort to canceled", () => {
    assert.equal(mapWebAuthnError({ name: "NotAllowedError" }), "canceled");
    assert.equal(mapWebAuthnError({ name: "AbortError" }), "canceled");
  });

  it("maps duplicate credential to alreadyRegistered", () => {
    assert.equal(mapWebAuthnError({ name: "InvalidStateError" }), "alreadyRegistered");
  });

  it("maps unknown errors to failed", () => {
    assert.equal(mapWebAuthnError({ name: "UnknownError" }), "failed");
    assert.equal(mapWebAuthnError(null), "failed");
  });
});
