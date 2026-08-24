import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  newPasswordFieldsIssue,
  passwordSchema,
  translateApiFieldErrors,
  usernameSchema,
} from "./authValidation.js";

const t = (key) => key;

describe("usernameSchema", () => {
  const schema = usernameSchema(t);

  it("accepts Latin usernames with allowed symbols", () => {
    assert.equal(schema.safeParse("user.name").success, true);
    assert.equal(schema.safeParse("user_name+1").success, true);
  });

  it("rejects Cyrillic and other disallowed characters", () => {
    assert.equal(schema.safeParse("Иван").success, false);
    assert.equal(schema.safeParse("user name").success, false);
  });

  it("enforces length", () => {
    assert.equal(schema.safeParse("abc").success, false);
    assert.equal(schema.safeParse("a".repeat(46)).success, false);
  });
});

describe("passwordSchema", () => {
  it("requires digit and lowercase when policy is on", () => {
    const schema = passwordSchema(t, { requirePolicy: true });
    assert.equal(schema.safeParse("password").success, false);
    assert.equal(schema.safeParse("PASSWORD1").success, false);
    assert.equal(schema.safeParse("password1").success, true);
  });

  it("only checks length without policy", () => {
    const schema = passwordSchema(t);
    assert.equal(schema.safeParse("password").success, true);
    assert.equal(schema.safeParse("short").success, false);
  });

  it("can lower the minimum length for sign-in", () => {
    const schema = passwordSchema(t, { minLength: 1 });
    assert.equal(schema.safeParse("user123").success, true);
  });
});

describe("newPasswordFieldsIssue", () => {
  it("accepts matching passwords that pass policy", () => {
    assert.equal(
      newPasswordFieldsIssue("password1", "password1", t),
      null,
    );
  });

  it("reports mismatch on confirm", () => {
    const issue = newPasswordFieldsIssue("password1", "password2", t);
    assert.equal(issue?.confirm, "auth.passwordsMismatch");
    assert.equal(issue?.password, undefined);
  });

  it("reports policy on the password field", () => {
    const issue = newPasswordFieldsIssue("password", "password", t);
    assert.equal(issue?.password, "auth.passwordNeedsDigit");
  });

  it("allows both empty when optional", () => {
    assert.equal(
      newPasswordFieldsIssue("", "", t, { optional: true }),
      null,
    );
  });

  it("still requires confirm when optional password is filled", () => {
    const issue = newPasswordFieldsIssue("password1", "", t, {
      optional: true,
    });
    assert.equal(issue?.confirm, "auth.passwordsMismatch");
  });
});

describe("translateApiFieldErrors", () => {
  it("translates known error codes", () => {
    const out = translateApiFieldErrors(
      { userName: ["username_invalid"] },
      (key) => (key === "errors.username_invalid" ? "Invalid username" : key),
    );
    assert.equal(out.userName, "Invalid username");
  });

  it("leaves unknown messages unchanged", () => {
    const out = translateApiFieldErrors(
      { password: ["Something else"] },
      (key) => key,
    );
    assert.equal(out.password, "Something else");
  });
});
