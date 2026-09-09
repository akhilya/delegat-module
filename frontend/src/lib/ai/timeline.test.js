import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  appendTextDelta,
  appendTool,
  executionView,
  splitPlanExecution,
  timelineFromLegacy,
} from "./timeline.js";

describe("ai timeline", () => {
  it("keeps successive text deltas in one block and starts a new block after a tool", () => {
    let timeline = [];
    timeline = appendTextDelta(timeline, "Looking at categories.");
    timeline = appendTextDelta(timeline, " Found shirts.");
    timeline = appendTool(timeline, { id: "t1", name: "platform_request" });
    timeline = appendTextDelta(timeline, "Category id=5 is a match.");

    assert.equal(timeline.length, 3);
    assert.equal(timeline[0].text, "Looking at categories. Found shirts.");
    assert.equal(timeline[1].type, "tool");
    assert.equal(timeline[2].text, "Category id=5 is a match.");
  });

  it("treats streaming text before any tool as process, then as the reply when done", () => {
    const streaming = executionView(
      [{ type: "text", text: "Checking the catalog." }],
      { done: false },
    );
    assert.equal(streaming.process.length, 1);
    assert.equal(streaming.reply, "");
    assert.deepEqual(streaming.actions, []);

    const finished = executionView(
      [{ type: "text", text: "Here is the answer." }],
      { done: true },
    );
    assert.equal(finished.process.length, 0);
    assert.equal(finished.reply, "Here is the answer.");
  });

  it("puts tools and prior thoughts in the process rail, later text in the reply", () => {
    const view = executionView(
      [
        { type: "text", text: "First I will list categories." },
        { type: "tool", id: "t1", tool: { name: "platform_request" } },
        { type: "text", text: "Shirts already exist." },
        { type: "tool", id: "t2", tool: { name: "sql_readonly" } },
        { type: "text", text: "Created product 16." },
      ],
      { done: true },
    );
    assert.equal(view.process.length, 4);
    assert.equal(view.reply, "Created product 16.");
    assert.deepEqual(view.actions, []);
  });

  it("keeps thinking in the process and lifts navigate_to into reply buttons", () => {
    const view = executionView(
      [
        { type: "text", text: "Found Hello soap." },
        { type: "tool", id: "t1", tool: { name: "search_catalog" } },
        {
          type: "tool",
          id: "t2",
          tool: {
            name: "navigate_to",
            result: {
              navigate: true,
              url: "/products/12/edit",
              label: "Hello soap",
              actions: [
                { url: "/products/12/edit", label: "Hello soap" },
                {
                  prompt: "Put these photos on Hello soap",
                  label: "Put photos",
                },
              ],
            },
          },
        },
        { type: "text", text: "Open the card when you want." },
      ],
      { done: true },
    );
    assert.equal(view.process.length, 2);
    assert.equal(view.process[0].type, "text");
    assert.equal(view.process[1].tool.name, "search_catalog");
    assert.equal(view.reply, "Open the card when you want.");
    assert.equal(view.actions.length, 2);
    assert.equal(view.actions[0].url, "/products/12/edit");
    assert.equal(view.actions[1].prompt, "Put these photos on Hello soap");
    assert.equal(view.actions[1].kind, "continue");
    assert.deepEqual(view.forms, []);
  });

  it("rebuilds a prompt-only navigate_to button from a legacy result", () => {
    const view = executionView(
      [
        {
          type: "tool",
          id: "t1",
          tool: {
            name: "navigate_to",
            result: {
              navigate: true,
              prompt: "Put these photos on Hello soap",
              label: "Put photos",
            },
          },
        },
      ],
      { done: true },
    );
    assert.deepEqual(view.process, []);
    assert.equal(view.actions.length, 1);
    assert.equal(view.actions[0].prompt, "Put these photos on Hello soap");
    assert.equal(view.actions[0].url, "");
    assert.equal(view.actions[0].kind, "continue");
  });

  it("lifts ask_form onto the reply and out of the process", () => {
    const view = executionView(
      [
        { type: "text", text: "Need Ozon keys." },
        {
          type: "tool",
          id: "t1",
          tool: {
            name: "ask_form",
            result: {
              form: true,
              kind: "connection",
              adapterKey: "ozon",
              title: "Connect Ozon",
            },
          },
        },
        { type: "text", text: "Fill the form in this chat." },
      ],
      { done: true },
    );
    assert.equal(view.process.length, 1);
    assert.equal(view.process[0].type, "text");
    assert.equal(view.reply, "Fill the form in this chat.");
    assert.equal(view.forms.length, 1);
    assert.equal(view.forms[0].adapterKey, "ozon");
  });

  it("lifts a questions questionnaire onto the reply", () => {
    const view = executionView(
      [
        {
          type: "tool",
          id: "t1",
          tool: {
            name: "ask_form",
            result: {
              form: true,
              kind: "questions",
              questions: [
                {
                  key: "adapter",
                  title: "Which marketplace?",
                  type: "single",
                  choices: [{ value: "ozon", label: "Ozon" }],
                },
              ],
            },
          },
        },
        { type: "text", text: "Pick one." },
      ],
      { done: true },
    );
    assert.deepEqual(view.process, []);
    assert.equal(view.reply, "Pick one.");
    assert.equal(view.forms.length, 1);
    assert.equal(view.forms[0].kind, "questions");
    assert.equal(view.forms[0].questions[0].key, "adapter");
  });

  it("rebuilds a legacy tools-then-content message without dropping either", () => {
    const timeline = timelineFromLegacy("Done.", [
      { id: "a", name: "sql_readonly" },
    ]);
    const view = executionView(timeline, { done: true });
    assert.equal(view.process.length, 1);
    assert.equal(view.reply, "Done.");
  });

  it("splits proposal thoughts from tools that run after Execute", () => {
    const timeline = [
      { type: "text", text: "I can put these photos on Hello soap." },
      { type: "tool", id: "t1", tool: { name: "platform_request" } },
      { type: "text", text: "Plan executed: 1 of 1 steps." },
    ];
    const split = splitPlanExecution(timeline, 1, { done: true });
    assert.equal(split.proposal.reply, "I can put these photos on Hello soap.");
    assert.equal(split.proposal.process.length, 0);
    assert.equal(split.execution.process.length, 1);
    assert.equal(split.execution.reply, "Plan executed: 1 of 1 steps.");
  });

  it("keeps a single execution view when planExecStart is missing", () => {
    const split = splitPlanExecution(
      [{ type: "text", text: "Ready when you are." }],
      undefined,
      { done: true },
    );
    assert.equal(split.proposal.reply, "Ready when you are.");
    assert.deepEqual(split.execution.process, []);
    assert.equal(split.execution.reply, "");
  });
});
