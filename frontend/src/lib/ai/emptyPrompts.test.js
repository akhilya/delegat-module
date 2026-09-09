import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { readFileSync } from "node:fs";
import {
  EMPTY_PROMPT_VISIBLE,
  GENERAL_EMPTY_PROMPT_KEYS,
  PAGE_EMPTY_PROMPT_KEYS,
  emptyPromptScope,
  hashString,
  pickEmptyPromptKeys,
} from "./emptyPrompts.js";

describe("pickEmptyPromptKeys", () => {
  it("always returns three unique keys from the pool", () => {
    assert.equal(EMPTY_PROMPT_VISIBLE, 3);
    assert.ok(GENERAL_EMPTY_PROMPT_KEYS.length >= 6);
    const keys = pickEmptyPromptKeys({ seed: 1 });
    assert.equal(keys.length, 3);
    assert.equal(new Set(keys).size, 3);
    for (const key of keys) {
      assert.ok(GENERAL_EMPTY_PROMPT_KEYS.includes(key));
    }
  });

  it("is stable for the same seed and changes across conversations", () => {
    const a = pickEmptyPromptKeys({ seed: "session-a:none" });
    const again = pickEmptyPromptKeys({ seed: "session-a:none" });
    assert.deepEqual(again, a);
    const other = [
      pickEmptyPromptKeys({ seed: "session-b:none" }),
      pickEmptyPromptKeys({ seed: "session-c:none" }),
      pickEmptyPromptKeys({ seed: "session-d:none" }),
    ];
    assert.ok(other.some((keys) => keys.join() !== a.join()));
  });

  it("puts a page-specific prompt first when the companion has page context", () => {
    const keys = pickEmptyPromptKeys({ page: "connection", seed: 4 });
    assert.equal(keys.length, 3);
    assert.ok(PAGE_EMPTY_PROMPT_KEYS.connection.includes(keys[0]));
    assert.equal(new Set(keys).size, 3);
  });

  it("scopes rotation to the live page, not object identity", () => {
    assert.equal(emptyPromptScope(null), "none");
    assert.equal(
      emptyPromptScope({ page: "product", productId: 12 }),
      "product:12:::",
    );
    assert.equal(hashString("a"), hashString("a"));
    assert.notEqual(hashString("a"), hashString("b"));
  });

  it("has a locale string for every prompt key", async () => {
    const en = (await import("../../locales/en.json", { with: { type: "json" } }))
      .default;
    const ru = (await import("../../locales/ru.json", { with: { type: "json" } }))
      .default;
    const keys = [
      ...GENERAL_EMPTY_PROMPT_KEYS,
      ...Object.values(PAGE_EMPTY_PROMPT_KEYS).flat(),
    ];
    for (const key of keys) {
      const leaf = key.slice("ai.".length);
      assert.equal(typeof en.ai[leaf], "string", `en ${key}`);
      assert.equal(typeof ru.ai[leaf], "string", `ru ${key}`);
      assert.ok(en.ai[leaf].length > 0);
      assert.ok(ru.ai[leaf].length > 0);
    }
  });
});

describe("empty chat chrome", () => {
  it("docks rotating starters in the empty thread, at most three", () => {
    const panel = readFileSync(
      new URL("../../components/ai/AiChatPanel.vue", import.meta.url),
      "utf8",
    );
    const empty = readFileSync(
      new URL("../../components/ai/AiChatEmpty.vue", import.meta.url),
      "utf8",
    );
    assert.match(panel, /AiChatEmpty/);
    assert.match(panel, /pickEmptyPromptKeys/);
    assert.match(empty, /EMPTY_PROMPT_VISIBLE/);
    assert.match(empty, /min-h-full/);
    assert.match(empty, /show-chevron/);
    assert.doesNotMatch(empty, /max-w-\[280px\]/);
  });
});
