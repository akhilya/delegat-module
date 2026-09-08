import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resolveAiChromeEnabled } from "./chrome.js";

describe("resolveAiChromeEnabled", () => {
  it("keeps chat chrome off on public and setup shells even when the plan has AI", () => {
    assert.equal(
      resolveAiChromeEnabled({
        isBareLayout: true,
        me: { aiEnabled: true },
      }),
      false,
    );
  });

  it("mounts chat chrome after leaving sign-in when /auth/me reports AI on", () => {
    assert.equal(
      resolveAiChromeEnabled({
        isBareLayout: false,
        me: { aiEnabled: true },
      }),
      true,
    );
  });

  it("does not mount chrome when the instance flag is off or /auth/me is missing", () => {
    assert.equal(
      resolveAiChromeEnabled({ isBareLayout: false, me: { aiEnabled: false } }),
      false,
    );
    assert.equal(
      resolveAiChromeEnabled({ isBareLayout: false, me: {} }),
      false,
    );
    assert.equal(
      resolveAiChromeEnabled({ isBareLayout: false, me: null }),
      false,
    );
  });
});
