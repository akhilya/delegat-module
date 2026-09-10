import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  AI_CONFIRM_PLAN,
  applyFormReplies,
  applyPlanDecisions,
  foldPlanTurns,
  hydrateConversation,
  isAiControlMessage,
  isHiddenUserMessage,
  mapStoredMessage,
  serializeToolCallsJson,
  toolResultFailed,
  finalizeTurnTools,
} from "./persist.js";

describe("serializeToolCallsJson", () => {
  it("keeps the plan card so a reload can still confirm it", () => {
    const json = serializeToolCallsJson({
      plan: { summary: "Upload photos", steps: [{ description: "Match files" }] },
      toolCalls: [],
      timeline: [{ type: "text", text: "Ready when you are." }],
    });
    const parsed = JSON.parse(json);
    assert.equal(parsed._ui, "assistantTurn");
    assert.equal(parsed.plan.summary, "Upload photos");
  });
});

describe("mapStoredMessage", () => {
  it("restores plan and timeline from an assistantTurn envelope", () => {
    const m = mapStoredMessage({
      dbId: 9,
      role: "assistant",
      content: "Ready.",
      toolCallsJson: JSON.stringify({
        _ui: "assistantTurn",
        tools: [{ id: "t1", name: "search_catalog", summary: "3" }],
        timeline: [
          { type: "text", text: "Looking." },
          { type: "tool", id: "t1" },
        ],
        plan: { summary: "Next" },
      }),
    });
    assert.equal(m.done, true);
    assert.equal(m.plan.summary, "Next");
    assert.equal(m.timeline.length, 2);
    assert.equal(m.timeline[1].tool.name, "search_catalog");
  });
});

describe("user attachments", () => {
  it("round-trips file names on a user message", () => {
    const json = serializeToolCallsJson({
      attachments: [
        {
          id: 9,
          relativePath: "photos/SKU-HELLO.jpg",
          fileName: "SKU-HELLO.jpg",
          size: 120,
        },
      ],
    });
    const parsed = JSON.parse(json);
    assert.equal(parsed._ui, "userAttachments");
    assert.equal(parsed.attachments[0].id, 9);
    const m = mapStoredMessage({
      dbId: 4,
      role: "user",
      content: "Put these on products",
      toolCallsJson: json,
    });
    assert.equal(m.attachments.length, 1);
    assert.equal(m.attachments[0].fileName, "SKU-HELLO.jpg");
    assert.equal(m.attachments[0].id, 9);
  });

  it("keeps operation context and attachments together", () => {
    const json = serializeToolCallsJson({
      contextCard: { title: "Import" },
      attachments: [{ relativePath: "log.txt", fileName: "log.txt", size: 2 }],
    });
    const parsed = JSON.parse(json);
    assert.equal(parsed._ui, "operationContext");
    const m = mapStoredMessage({
      dbId: 5,
      role: "user",
      content: "Why did this fail?",
      toolCallsJson: json,
    });
    assert.equal(m.contextCard.title, "Import");
    assert.equal(m.attachments[0].fileName, "log.txt");
  });
});

describe("form replies", () => {
  it("round-trips answers on the assistant envelope", () => {
    const json = serializeToolCallsJson({
      toolCalls: [{ id: "ask1", name: "ask_form", result: { form: true } }],
      timeline: [{ type: "tool", id: "ask1" }],
      formReplies: { ask1: { answers: { adapter: "ozon" } } },
    });
    const parsed = JSON.parse(json);
    assert.equal(parsed._ui, "assistantTurn");
    assert.equal(parsed.formReplies.ask1.answers.adapter, "ozon");
    const m = mapStoredMessage({
      dbId: 3,
      role: "assistant",
      content: "Which marketplace?",
      toolCallsJson: json,
    });
    assert.equal(m.formReplies.ask1.answers.adapter, "ozon");
  });

  it("hides the follow-up user turn and stamps the previous assistant card", () => {
    const json = serializeToolCallsJson({
      formReply: { formId: "ask1", answers: { adapter: "ozon" } },
    });
    const parsed = JSON.parse(json);
    assert.equal(parsed._ui, "formReply");
    const user = mapStoredMessage({
      dbId: 4,
      role: "user",
      content: "Use ozon",
      toolCallsJson: json,
    });
    assert.equal(user.formReply.formId, "ask1");
    assert.equal(isHiddenUserMessage(user), true);
    const messages = applyFormReplies([
      { role: "assistant", formReplies: {} },
      user,
    ]);
    assert.equal(messages[0].formReplies.ask1.answers.adapter, "ozon");
  });

  it("hydrates a submitted questionnaire after reload", () => {
    const messages = hydrateConversation([
      {
        dbId: 1,
        role: "assistant",
        content: "Which marketplace?",
        toolCallsJson: JSON.stringify({
          _ui: "assistantTurn",
          tools: [{ id: "ask1", name: "ask_form" }],
          timeline: [{ type: "tool", id: "ask1" }],
        }),
      },
      {
        dbId: 2,
        role: "user",
        content: "Use ozon",
        toolCallsJson: JSON.stringify({
          _ui: "formReply",
          formId: "ask1",
          answers: { adapter: "ozon" },
        }),
      },
    ]);
    assert.equal(messages[0].formReplies.ask1.answers.adapter, "ozon");
    assert.equal(messages[1].formReply.formId, "ask1");
  });
});

describe("applyPlanDecisions", () => {
  it("treats Confirmed, proceed as confirming the previous plan", () => {
    const messages = applyPlanDecisions([
      { role: "assistant", plan: { summary: "Do it" }, planConfirmed: false },
      { role: "user", content: AI_CONFIRM_PLAN },
    ]);
    assert.equal(messages[0].planConfirmed, true);
  });
});

describe("plan execution persist", () => {
  it("round-trips planExecuted and planExecStart on an assistantTurn envelope", () => {
    const json = serializeToolCallsJson({
      plan: { summary: "Upload photos" },
      planConfirmed: true,
      planExecuted: true,
      planExecStart: 1,
      toolCalls: [{ id: "t1", name: "platform_request", summary: "ok" }],
      timeline: [
        { type: "text", text: "Ready." },
        { type: "tool", id: "t1" },
      ],
    });
    const parsed = JSON.parse(json);
    assert.equal(parsed.planExecuted, true);
    assert.equal(parsed.planExecStart, 1);
    const m = mapStoredMessage({
      dbId: 8,
      role: "assistant",
      content: "Ready.\n\nPlan executed.",
      toolCallsJson: json,
    });
    assert.equal(m.planExecuted, true);
    assert.equal(m.planExecStart, 1);
  });

  it("round-trips planFailed so a reload does not show Done", () => {
    const json = serializeToolCallsJson({
      plan: { summary: "Create a product" },
      planConfirmed: true,
      planExecuted: true,
      planFailed: true,
      toolCalls: [
        {
          id: "t1",
          name: "platform_request",
          summary: "Product limit reached — nothing was created.",
        },
      ],
      timeline: [
        { type: "text", text: "Ready." },
        { type: "tool", id: "t1" },
      ],
    });
    const parsed = JSON.parse(json);
    assert.equal(parsed.planFailed, true);
    const m = mapStoredMessage({
      dbId: 9,
      role: "assistant",
      content: "Nothing was changed (0 of 1 steps succeeded).",
      toolCallsJson: json,
    });
    assert.equal(m.planExecuted, true);
    assert.equal(m.planFailed, true);
  });

  it("folds a Confirmed, proceed triple into one executed plan message", () => {
    const folded = foldPlanTurns([
      {
        role: "assistant",
        content: "Ready.",
        plan: { summary: "Do it" },
        timeline: [{ type: "text", text: "Ready." }],
        toolCalls: [],
      },
      { role: "user", content: AI_CONFIRM_PLAN },
      {
        role: "assistant",
        content: "Plan executed.",
        timeline: [
          { type: "tool", id: "t1", tool: { name: "platform_request" } },
          { type: "text", text: "Plan executed." },
        ],
        toolCalls: [{ id: "t1", name: "platform_request" }],
      },
    ]);
    assert.equal(folded.length, 1);
    assert.equal(folded[0].planExecuted, true);
    assert.equal(folded[0].planConfirmed, true);
    assert.equal(folded[0].planExecStart, 1);
    assert.equal(folded[0].content, "Ready.\n\nPlan executed.");
    assert.equal(folded[0].timeline.length, 3);
  });

  it("hides a stray confirm without marking the plan executed", () => {
    const folded = foldPlanTurns([
      {
        role: "assistant",
        content: "Ready.",
        plan: { summary: "Do it" },
        done: true,
        timeline: [{ type: "text", text: "Ready." }],
      },
      { role: "user", content: AI_CONFIRM_PLAN },
    ]);
    assert.equal(folded.length, 1);
    assert.equal(folded[0].planConfirmed, true);
    assert.ok(!folded[0].planExecuted);
  });

  it("treats Confirmed, proceed as a hidden control line", () => {
    assert.equal(isAiControlMessage({ content: AI_CONFIRM_PLAN }), true);
    assert.equal(isAiControlMessage({ content: "go ahead" }), false);
  });

  it("hydrates a stored confirm triple the same way", () => {
    const messages = hydrateConversation([
      {
        dbId: 1,
        role: "assistant",
        content: "Ready.",
        toolCallsJson: JSON.stringify({
          _ui: "assistantTurn",
          tools: [],
          timeline: [{ type: "text", text: "Ready." }],
          plan: { summary: "Do it" },
        }),
      },
      { dbId: 2, role: "user", content: AI_CONFIRM_PLAN },
      {
        dbId: 3,
        role: "assistant",
        content: "Plan executed.",
        toolCallsJson: JSON.stringify({
          _ui: "assistantTurn",
          tools: [{ id: "t1", name: "platform_request" }],
          timeline: [
            { type: "tool", id: "t1" },
            { type: "text", text: "Plan executed." },
          ],
        }),
      },
    ]);
    assert.equal(messages.length, 1);
    assert.equal(messages[0].planExecuted, true);
    assert.equal(messages[0].planExecStart, 1);
  });
});

describe("toolResultFailed", () => {
  it("treats cabinet quota bodies without an error field as failed", () => {
    assert.equal(
      toolResultFailed(
        {
          title: "Product limit reached",
          status: 409,
          code: "product_limit_reached",
        },
        "HTTP 409",
      ),
      true,
    );
    assert.equal(toolResultFailed({ id: 1, sku: "A" }, "HTTP 200"), false);
  });

  it("does not treat a queued Catalog Connect job as a failed write", () => {
    assert.equal(
      toolResultFailed(
        {
          operationId: "abc",
          status: "pending",
          error: "Timed out",
        },
        "HTTP 200 queued",
      ),
      false,
    );
    assert.equal(
      toolResultFailed({ status: "failed", error: "Timed out" }, "HTTP 200"),
      true,
    );
  });
});

describe("finalizeTurnTools", () => {
  it("closes a running tool when the turn errors so the process is not left spinning", () => {
    const msg = {
      toolCalls: [
        {
          id: "t1",
          name: "platform_request",
          status: "running",
          input: "{}",
        },
      ],
    };
    const out = finalizeTurnTools(msg);
    assert.equal(out.toolCalls[0].status, "done");
    assert.equal(out.toolCalls[0].summary, "interrupted");
  });
});
