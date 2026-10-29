import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const settingsPage = readFileSync(
  new URL("../pages/settings/AiModelsPage.vue", import.meta.url),
  "utf8",
);

describe("connected MCP settings visibility", () => {
  it("keeps connected MCP settings out of the AI settings page", () => {
    assert.doesNotMatch(settingsPage, /AiMcpServersPanel/);
  });
});
