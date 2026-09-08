import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  composeAiProfiles,
  dockerComposeArgs,
  isAiEnabled,
  parseDotEnv,
} from "./ai-compose.mjs";

describe("isAiEnabled", () => {
  it("enables self-host when AI_ENABLED is unset", () => {
    assert.equal(isAiEnabled({}), true);
    assert.equal(isAiEnabled({ AI_ENABLED: "" }), true);
  });

  it("fails closed when Control Plane is linked and AI_ENABLED is unset", () => {
    assert.equal(
      isAiEnabled({ CONTROL_PLANE_URL: "https://module.delegat.pro" }),
      false,
    );
  });

  it("lets an explicit true/false win", () => {
    assert.equal(
      isAiEnabled({
        AI_ENABLED: "true",
        CONTROL_PLANE_URL: "https://module.delegat.pro",
      }),
      true,
    );
    assert.equal(isAiEnabled({ AI_ENABLED: "false" }), false);
    assert.equal(isAiEnabled({ AI_ENABLED: "0" }), false);
    assert.equal(isAiEnabled({ AI_ENABLED: "1" }), true);
  });
});

describe("composeAiProfiles", () => {
  it("starts the ai profile only when AI is on", () => {
    assert.deepEqual(composeAiProfiles({ AI_ENABLED: "true" }), ["ai"]);
    assert.deepEqual(composeAiProfiles({ AI_ENABLED: "false" }), []);
  });
});

describe("dockerComposeArgs", () => {
  it("passes --profile ai on up when AI is on", () => {
    assert.deepEqual(
      dockerComposeArgs({ env: { AI_ENABLED: "true" } }),
      ["compose", "--env-file", ".env.dev", "--profile", "ai", "up"],
    );
  });

  it("omits the ai profile on up when AI is off", () => {
    assert.deepEqual(
      dockerComposeArgs({ env: { AI_ENABLED: "false" } }),
      ["compose", "--env-file", ".env.dev", "up"],
    );
  });

  it("always tears down the ai profile so a leftover sandbox is removed", () => {
    assert.deepEqual(
      dockerComposeArgs({ action: "down", env: { AI_ENABLED: "false" } }),
      ["compose", "--env-file", ".env.dev", "--profile", "ai", "down"],
    );
  });
});

describe("parseDotEnv", () => {
  it("reads keys, export, and quotes", () => {
    const env = parseDotEnv(`
# comment
AI_ENABLED=false
export CONTROL_PLANE_URL="https://cp.example"
`);
    assert.equal(env.AI_ENABLED, "false");
    assert.equal(env.CONTROL_PLANE_URL, "https://cp.example");
  });
});
