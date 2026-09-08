import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { readFileSync } from "node:fs";
import {
  groupProcessItems,
  parseJsonObject,
  processTools,
  pythonRunParts,
  runningProcessTool,
  sqlResultParts,
  stripDisplayEmoji,
} from "./display.js";

describe("stripDisplayEmoji", () => {
  it("removes pictographs so Don't use is visible in the chat", () => {
    assert.equal(stripDisplayEmoji("Ready ✅ 3 products"), "Ready 3 products");
    assert.equal(stripDisplayEmoji("Warhammer 🎉"), "Warhammer");
    assert.equal(stripDisplayEmoji("plain SQL"), "plain SQL");
  });
});

describe("pythonRunParts", () => {
  it("reads code from the JSON input envelope", () => {
    const parts = pythonRunParts({
      input: JSON.stringify({ code: "print('ping')" }),
      result: { stdout: "ping\n", stderr: "", exitCode: 0 },
    });
    assert.equal(parts.code, "print('ping')");
    assert.equal(parts.stdout, "ping\n");
    assert.equal(parts.exitCode, 0);
  });
});

describe("sqlResultParts", () => {
  it("uses columns when present and caps rows", () => {
    const parts = sqlResultParts({
      result: {
        columns: ["id", "name"],
        rows: [
          { id: 1, name: "A" },
          { id: 2, name: "B" },
        ],
      },
    });
    assert.deepEqual(parts.columns, ["id", "name"]);
    assert.equal(parts.rows.length, 2);
  });
});

describe("groupProcessItems", () => {
  it("keeps python as its own card and batches other tools", () => {
    const groups = groupProcessItems([
      { type: "text", text: "Checking stock." },
      { type: "tool", tool: { name: "sql_readonly" } },
      { type: "tool", tool: { name: "search_catalog" } },
      { type: "tool", tool: { name: "run_python" } },
      { type: "tool", tool: { name: "platform_request" } },
    ]);
    assert.equal(groups.length, 4);
    assert.equal(groups[0].type, "text");
    assert.equal(groups[1].type, "tools");
    assert.equal(groups[1].tools.length, 2);
    assert.equal(groups[2].type, "python");
    assert.equal(groups[3].type, "tools");
    assert.equal(groups[3].tools[0].name, "platform_request");
  });
});

describe("processTools", () => {
  it("counts tools and finds the running one", () => {
    const process = [
      { type: "text", text: "Looking." },
      { type: "tool", tool: { name: "search_catalog", status: "success" } },
      { type: "tool", tool: { name: "sql_readonly", status: "running" } },
    ];
    assert.equal(processTools(process).length, 2);
    assert.equal(runningProcessTool(process).name, "sql_readonly");
    assert.equal(runningProcessTool([{ type: "text", text: "Hi" }]), null);
  });
});

describe("parseJsonObject", () => {
  it("returns null for non-objects", () => {
    assert.equal(parseJsonObject("[]"), null);
    assert.equal(parseJsonObject("not-json"), null);
  });
});

describe("AiChatPanel chrome", () => {
  it("uses dedicated python and tool blocks instead of a process rail", () => {
    const panel = readFileSync(
      new URL("../../components/ai/AiChatPanel.vue", import.meta.url),
      "utf8",
    );
    const process = readFileSync(
      new URL("../../components/ai/AiChatProcess.vue", import.meta.url),
      "utf8",
    );
    assert.match(panel, /AiChatProcess/);
    assert.match(panel, /AiChatActions/);
    assert.match(panel, /AiChatAttachmentList/);
    assert.match(panel, /:done="!!msg.done"/);
    assert.match(process, /processUsed/);
    assert.match(process, /v-model:open="detailsOpen"/);
    assert.doesNotMatch(process, /InsetGroup/);
    assert.doesNotMatch(panel, /ai-process/);
    assert.doesNotMatch(panel, /border-l/);
  });
});
