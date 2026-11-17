import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { canConfirmPlan, reconcilePlanMessages } from "./planState.js";
import { readSseEvents } from "./sse.js";
import {
  apiHistoryFromBlocks,
  appendAssistantApiBlock,
} from "./chatHistory.js";
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

describe("stopped assistant turns", () => {
  it("round-trips an empty stopped reply and its tool status", () => {
    const message = {
      role: "assistant",
      content: "",
      turnStopped: true,
      done: true,
      timeline: [{ type: "tool", id: "t1" }],
      toolCalls: [{ id: "t1", name: "run_python", status: "running" }],
    };
    finalizeTurnTools(message, { stopped: true });
    const restored = mapStoredMessage({
      dbId: 1,
      role: "assistant",
      content: "",
      toolCallsJson: serializeToolCallsJson(message),
    });
    assert.equal(restored.turnStopped, true);
    assert.equal(restored.done, true);
    assert.equal(restored.toolCalls[0].status, "stopped");
    assert.equal(restored.timeline[0].tool.status, "stopped");
  });
  it("does not lose a local stopped reply to a pre-persistence history read", () => {
    const user = { dbId: 1, role: "user", content: "Check" };
    const stopped = {
      role: "assistant",
      content: "Partial",
      turnStopped: true,
    };
    for (const rows of [
      [user],
      [user, { dbId: 2, role: "assistant", content: "Older partial" }],
    ]) {
      const restored = reconcilePlanMessages([user, stopped], rows);
      assert.equal(restored.at(-1).turnStopped, true);
      assert.equal(restored.at(-1).content, "Partial");
    }
  });
  it("preserves a persisted stop without marking a later reply stopped", () => {
    const stopped = {
      dbId: 2,
      role: "assistant",
      turnStopped: true,
      content: "Partial",
    };
    const later = { dbId: 4, role: "assistant", content: "Next reply" };
    const restored = reconcilePlanMessages(
      [stopped],
      [{ ...stopped, turnStopped: false }, later],
    );
    assert.equal(restored[0].turnStopped, true);
    assert.equal(restored[1].turnStopped, undefined);
    assert.equal(
      canConfirmPlan({
        plan: { proposalId: "p1" },
        done: true,
        turnStopped: true,
      }),
      false,
    );
  });
});

describe("plan result reviews", () => {
  it("round-trips the parent execution identity without confirming a correction", () => {
    const restored = mapStoredMessage({
      dbId: 3,
      role: "assistant",
      content: "",
      toolCallsJson: serializeToolCallsJson({
        role: "assistant",
        content: "",
        executionProposalId: "failed-plan",
        plan: { proposalId: "corrected-plan" },
        toolCalls: [],
        timeline: [],
      }),
    });
    assert.equal(restored.executionProposalId, "failed-plan");
    assert.equal(restored.plan.proposalId, "corrected-plan");
    assert.equal(!!restored.planConfirmed, false);
    assert.equal(canConfirmPlan(restored), true);
  });
  it("retains an unpersisted review on stale history without duplicating a saved review", () => {
    const execution = {
      dbId: 2,
      role: "assistant",
      plan: { proposalId: "p1" },
      planConfirmed: true,
      planExecuted: true,
    };
    const review = {
      role: "assistant",
      executionProposalId: "p1",
      content: "Partial review",
      done: false,
    };
    const current = [execution, review];
    assert.deepEqual(
      reconcilePlanMessages(current, [execution]).at(-1),
      review,
    );
    const saved = { ...review, dbId: 3, done: true, content: "Actual result" };
    const reconciled = reconcilePlanMessages(current, [execution, saved]);
    assert.equal(reconciled.length, 2);
    assert.equal(reconciled[1].content, "Actual result");
    assert.equal(reconciled[1].done, true);
  });
  it("keeps distinct review identities on a stale read sharing the parent execution", () => {
    const execution = {
      id: "execution", dbId: 2, role: "assistant",
      plan: { proposalId: "A" }, planConfirmed: true, planExecuted: true,
    };
    const reviewOne = {
      id: "review-one", dbId: 3, role: "assistant",
      executionProposalId: "A", content: "First review", done: true,
    };
    const reviewTwo = {
      id: "review-two", role: "assistant", executionProposalId: "A",
      content: "", plan: { proposalId: "B" }, done: true,
    };
    const result = reconcilePlanMessages(
      [execution, reviewOne, reviewTwo],
      [execution, reviewOne],
    );
    assert.deepEqual(
      result.map((m) => m.id),
      ["execution", "review-one", "review-two"],
    );
    const restoredB = result.at(-1);
    assert.equal(restoredB.plan.proposalId, "B");
    assert.equal(canConfirmPlan(restoredB), true);
    assert.equal(restoredB.planConfirmed, undefined);
    assert.equal(restoredB.planExecuted, undefined);
  });
  it("keeps two persisted no-plan reviews sharing the parent in dbId order", () => {
    const execution = {
      id: "execution", dbId: 2, role: "assistant",
      plan: { proposalId: "A" }, planConfirmed: true, planExecuted: true,
    };
    const reviewThree = {
      id: "r3", dbId: 3, role: "assistant",
      executionProposalId: "A", content: "First", done: true,
    };
    const reviewFour = {
      id: "r4", dbId: 4, role: "assistant",
      executionProposalId: "A", content: "Second", done: true,
    };
    const result = reconcilePlanMessages(
      [execution, reviewThree, reviewFour],
      [execution, reviewThree],
    );
    assert.deepEqual(result.map((m) => m.dbId), [2, 3, 4]);
    assert.equal(result[1].content, "First");
    assert.equal(result[2].content, "Second");
  });
  it("adopts the persisted row for the same own proposal without duplicating it", () => {
    const execution = {
      id: "execution", dbId: 2, role: "assistant",
      plan: { proposalId: "A" }, planConfirmed: true, planExecuted: true,
    };
    const localB = {
      id: "local-b", role: "assistant", executionProposalId: "A",
      plan: { proposalId: "B" }, content: "", done: true,
    };
    const serverB = {
      id: "server-b", dbId: 4, role: "assistant", executionProposalId: "A",
      plan: { proposalId: "B" }, content: "Done", done: true,
    };
    const result = reconcilePlanMessages(
      [execution, localB],
      [execution, serverB],
    );
    assert.equal(result.length, 2);
    assert.equal(result[1].dbId, 4);
    assert.equal(result[1].plan.proposalId, "B");
    assert.equal(canConfirmPlan(result[1]), true);
  });
  it("retains ambiguous anonymous reviews instead of collapsing them onto one row", () => {
    const execution = {
      id: "execution", dbId: 2, role: "assistant",
      plan: { proposalId: "A" }, planConfirmed: true, planExecuted: true,
    };
    const reviewOne = {
      id: "r1", role: "assistant", executionProposalId: "A",
      content: "One", done: true,
    };
    const reviewTwo = {
      id: "r2", role: "assistant", executionProposalId: "A",
      content: "Two", done: true,
    };
    const serverReview = {
      id: "srv", dbId: 5, role: "assistant", executionProposalId: "A",
      content: "Server", done: true,
    };
    const result = reconcilePlanMessages(
      [execution, reviewOne, reviewTwo],
      [execution, serverReview],
    );
    const ids = result.map((m) => m.id);
    assert.ok(ids.includes("r1") && ids.includes("r2"));
    assert.equal(ids.filter((id) => id === "srv").length, 1);
  });
  it("adopts an evolved own proposal on the same persisted review row", () => {
    const execution = {
      id: "execution", dbId: 2, role: "assistant",
      plan: { proposalId: "A" }, planConfirmed: true, planExecuted: true,
    };
    const localReview = {
      id: "r3", dbId: 3, role: "assistant",
      executionProposalId: "A", content: "Text", done: true,
    };
    const updated = {
      id: "r3-updated", dbId: 3, role: "assistant",
      executionProposalId: "A", plan: { proposalId: "B" },
      content: "Text", done: true,
    };
    const result = reconcilePlanMessages(
      [execution, localReview],
      [execution, updated],
    );
    assert.equal(result.length, 2);
    assert.equal(result[1].plan.proposalId, "B");
    assert.equal(canConfirmPlan(result[1]), true);
  });
});

describe("plan reconciliation", () => {
  const pending = {
    id: 2,
    dbId: 2,
    role: "assistant",
    done: true,
    plan: { proposalId: "p1" },
  };
  it("never reopens an accepted or completed proposal after a stale read", () => {
    for (const planExecuted of [false, true]) {
      const local = {
        ...pending,
        planConfirmed: true,
        planExecuted,
        content: "Execution",
      };
      const [merged] = reconcilePlanMessages([local], [pending]);
      assert.equal(merged.planConfirmed, true);
      assert.equal(merged.planExecuted, planExecuted);
      assert.equal(merged.content, "Execution");
      assert.equal(canConfirmPlan(merged), false);
    }
  });
  it("accepts an authoritative completed result and clears a transient error", () => {
    const [merged] = reconcilePlanMessages(
      [
        {
          ...pending,
          planConfirmed: true,
          planConfirmationError: { stage: "execution" },
        },
      ],
      [{ ...pending, planConfirmed: true, planExecuted: true }],
    );
    assert.equal(merged.planExecuted, true);
    assert.equal(merged.planConfirmationError, null);
  });
  it("preserves unknown confirmation and cancelled/superseded decisions", () => {
    for (const patch of [
      { planConfirmationError: { stage: "confirmation" } },
      { planCancelled: true },
      { planSuperseded: true },
    ]) {
      const [merged] = reconcilePlanMessages(
        [{ ...pending, ...patch }],
        [pending],
      );
      assert.equal(canConfirmPlan(merged), false);
    }
  });
  it("does not transfer execution state to a different proposal", () => {
    const next = { ...pending, plan: { proposalId: "p2" } };
    const [merged] = reconcilePlanMessages(
      [{ ...pending, planConfirmed: true }],
      [next],
    );
    assert.equal(canConfirmPlan(merged), true);
    assert.equal(merged.planConfirmed, undefined);
  });
  it("requires a completed proposal and rejects failed/executed/confirming plans", () => {
    assert.equal(canConfirmPlan(pending), true);
    for (const patch of [
      { done: false },
      { planExecuted: true },
      { planFailed: true },
      { planConfirming: true },
    ])
      assert.equal(canConfirmPlan({ ...pending, ...patch }), false);
  });
});

describe("AI SSE decoding", () => {
  async function decode(text, chunkSize) {
    const bytes = new TextEncoder().encode(text);
    const response = new Response(
      new ReadableStream({
        start(controller) {
          for (let i = 0; i < bytes.length; i += chunkSize)
            controller.enqueue(bytes.slice(i, i + chunkSize));
          controller.close();
        },
      }),
    );
    const events = [];
    for await (const event of readSseEvents(response)) events.push(event);
    return events;
  }
  it("keeps UTF-8 and CRLF records across single-byte chunks", async () => {
    const event = { type: "text", data: { delta: "Готово" } };
    assert.deepEqual(
      await decode(
        `: keepalive\r\n\r\ndata: ${JSON.stringify(event)}\r\n\r\n`,
        1,
      ),
      [event],
    );
  });
  it("handles multiline data, malformed frames, and a final record without a delimiter", async () => {
    assert.deepEqual(
      await decode(
        'data: invalid\n\ndata: {"type":"done",\ndata: "data":{"ok":false}}',
        3,
      ),
      [{ type: "done", data: { ok: false } }],
    );
  });
  it("does not invent a result from a truncated record", async () => {
    assert.deepEqual(await decode('data: {"type":"done","data":', 2), []);
  });
});

describe("AI provider chat history", () => {
  it("keeps each assistant tool round immediately followed by its results", () => {
    const apiBlocks = [];
    appendAssistantApiBlock(apiBlocks, { type: "text", text: "Looking up." });
    appendAssistantApiBlock(apiBlocks, {
      type: "tool_use",
      id: "call_search",
      name: "search_catalog",
      input: { query: "FI10" },
    });
    apiBlocks.push({
      role: "tool_result",
      tool_use_id: "call_search",
      content: "One match.",
    });
    appendAssistantApiBlock(apiBlocks, {
      type: "text",
      text: " Reading its price.",
    });
    appendAssistantApiBlock(apiBlocks, {
      type: "tool_use",
      id: "call_price",
      name: "sql_readonly",
      input: { query: "SELECT price FROM products" },
    });
    apiBlocks.push({
      role: "tool_result",
      tool_use_id: "call_price",
      content: "1500.00",
    });
    appendAssistantApiBlock(apiBlocks, {
      type: "text",
      text: "The price is 1500.00.",
    });

    assert.deepEqual(apiHistoryFromBlocks(apiBlocks), [
      {
        role: "assistant",
        content: [
          { type: "text", text: "Looking up." },
          {
            type: "tool_use",
            id: "call_search",
            name: "search_catalog",
            input: { query: "FI10" },
          },
        ],
      },
      {
        role: "user",
        content: [
          {
            type: "tool_result",
            tool_use_id: "call_search",
            content: "One match.",
          },
        ],
      },
      {
        role: "assistant",
        content: [
          { type: "text", text: " Reading its price." },
          {
            type: "tool_use",
            id: "call_price",
            name: "sql_readonly",
            input: { query: "SELECT price FROM products" },
          },
        ],
      },
      {
        role: "user",
        content: [
          {
            type: "tool_result",
            tool_use_id: "call_price",
            content: "1500.00",
          },
        ],
      },
      {
        role: "assistant",
        content: [{ type: "text", text: "The price is 1500.00." }],
      },
    ]);
  });
});

describe("serializeToolCallsJson", () => {
  it("keeps the plan card so a reload can still confirm it", () => {
    const json = serializeToolCallsJson({
      plan: {
        summary: "Upload photos",
        steps: [{ description: "Match files" }],
      },
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

  it("restores a localized API error code on a saved assistant turn", () => {
    const json = serializeToolCallsJson({
      toolCalls: [],
      timeline: [],
      requestError: { code: "hosted_ai_quota_exceeded", status: 429 },
    });
    const parsed = JSON.parse(json);
    assert.equal(parsed._ui, "assistantTurn");
    assert.equal(parsed.requestError.code, "hosted_ai_quota_exceeded");

    const message = mapStoredMessage({
      dbId: 10,
      role: "assistant",
      content: "",
      toolCallsJson: json,
    });
    assert.deepEqual(message.requestError, {
      code: "hosted_ai_quota_exceeded",
      status: 429,
    });
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
    assert.ok(!messages[0].planSuperseded);
  });

  it("marks an open plan outdated when the next message is not confirm or cancel", () => {
    const messages = applyPlanDecisions([
      { role: "assistant", plan: { summary: "Clean descriptions" } },
      { role: "user", content: "do not export again" },
    ]);
    assert.equal(messages[0].planSuperseded, true);
    assert.ok(!messages[0].planConfirmed);
    assert.ok(!messages[0].planCancelled);
  });

  it("leaves a confirmed plan in place when the user writes again", () => {
    const messages = applyPlanDecisions([
      { role: "assistant", plan: { summary: "Clean" }, planConfirmed: true },
      { role: "user", content: "one more thing" },
    ]);
    assert.ok(!messages[0].planSuperseded);
  });

  it("round-trips planSuperseded", () => {
    const json = serializeToolCallsJson({
      plan: { summary: "Clean descriptions" },
      planSuperseded: true,
      toolCalls: [],
      timeline: [{ type: "text", text: "Ready." }],
    });
    const m = mapStoredMessage({
      dbId: 9,
      role: "assistant",
      content: "Ready.",
      toolCallsJson: json,
    });
    assert.equal(m.planSuperseded, true);
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
  it("does not count a nonzero Python exit as a successful partial step", () => {
    assert.equal(toolResultFailed({ exitCode: 1 }), true);
    assert.equal(toolResultFailed({ exitCode: 0 }), false);
  });
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
