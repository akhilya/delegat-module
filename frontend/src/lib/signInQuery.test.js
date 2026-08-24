import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  readSignInQuery,
  stripSignInSecretsFromQuery,
} from "./signInQuery.js";

describe("readSignInQuery", () => {
  it("reads username and password from the query", () => {
    assert.deepEqual(
      readSignInQuery({ username: "user", password: "user123" }),
      { username: "user", password: "user123" },
    );
  });

  it("accepts userName as an alias and trims the username", () => {
    assert.deepEqual(
      readSignInQuery({ userName: "  user  ", password: "user123" }),
      { username: "user", password: "user123" },
    );
  });

  it("returns null when either credential is missing", () => {
    assert.equal(readSignInQuery({ username: "user" }), null);
    assert.equal(readSignInQuery({ password: "user123" }), null);
    assert.equal(readSignInQuery({}), null);
  });
});

describe("stripSignInSecretsFromQuery", () => {
  it("drops username and password and keeps other keys", () => {
    assert.deepEqual(
      stripSignInSecretsFromQuery({
        username: "user",
        password: "user123",
        redirect: "/products",
      }),
      { redirect: "/products" },
    );
  });
});
