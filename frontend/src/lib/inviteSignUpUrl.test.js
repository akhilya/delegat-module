import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  buildInviteSignUpUrl,
  readInviteTokenQuery,
  stripInviteTokenFromQuery,
} from "./inviteSignUpUrl.js";

describe("buildInviteSignUpUrl", () => {
  it("puts the token on /signUp query", () => {
    assert.equal(
      buildInviteSignUpUrl(
        "https://acme.module.delegat.pro",
        "invite-guid",
      ),
      "https://acme.module.delegat.pro/signUp?token=invite-guid",
    );
  });

  it("returns empty when the token is missing", () => {
    assert.equal(buildInviteSignUpUrl("https://acme.module.delegat.pro", ""), "");
    assert.equal(buildInviteSignUpUrl("https://acme.module.delegat.pro", "  "), "");
  });
});

describe("readInviteTokenQuery", () => {
  it("reads token from the query", () => {
    assert.equal(readInviteTokenQuery({ token: "abc" }), "abc");
  });

  it("accepts invitationToken as an alias and trims", () => {
    assert.equal(
      readInviteTokenQuery({ invitationToken: "  abc  " }),
      "abc",
    );
  });

  it("returns null when the token is missing", () => {
    assert.equal(readInviteTokenQuery({}), null);
    assert.equal(readInviteTokenQuery({ token: "  " }), null);
  });
});

describe("stripInviteTokenFromQuery", () => {
  it("drops token keys and keeps other params", () => {
    assert.deepEqual(
      stripInviteTokenFromQuery({
        token: "abc",
        invitationToken: "abc",
        next: "/products",
      }),
      { next: "/products" },
    );
  });
});
