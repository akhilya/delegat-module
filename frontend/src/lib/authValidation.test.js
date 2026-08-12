import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
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
