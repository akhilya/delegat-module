import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  defaultCredentialValues,
  normalizeCredentialFields,
} from "./credentialFields.js";

describe("normalizeCredentialFields", () => {
  it("maps select options from PascalCase plugin schema", () => {
    const fields = normalizeCredentialFields({
      Fields: [
        {
          Key: "sandbox",
          DisplayName: "API environment",
          Description: "Use Sandbox for developer test tokens",
          Type: "select",
          IsRequired: false,
          IsSecret: false,
          DefaultValue: "auto",
          Options: [
            { Value: "auto", Label: "Auto" },
            { Value: "production", Label: "Production" },
            { Value: "sandbox", Label: "Sandbox" },
          ],
        },
      ],
    });

    assert.equal(fields.length, 1);
    assert.equal(fields[0].key, "sandbox");
    assert.equal(fields[0].type, "select");
    assert.equal(fields[0].defaultValue, "auto");
    assert.deepEqual(
      fields[0].options.map((o) => o.value),
      ["auto", "production", "sandbox"],
    );
  });

  it("keeps password secrets as password type", () => {
    const fields = normalizeCredentialFields({
      fields: [
        {
          key: "apiKey",
          displayName: "API key",
          type: "password",
          isRequired: true,
          isSecret: true,
        },
      ],
    });

    assert.equal(fields[0].isSecret, true);
    assert.equal(fields[0].type, "password");
  });
});

describe("defaultCredentialValues", () => {
  it("prefills select defaults", () => {
    const values = defaultCredentialValues([
      { key: "apiKey", defaultValue: "" },
      { key: "sandbox", defaultValue: "auto" },
    ]);
    assert.deepEqual(values, { sandbox: "auto" });
  });
});
