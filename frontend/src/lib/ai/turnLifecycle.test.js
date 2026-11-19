import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  hasExecutionContext,
  isCurrentFormOwner,
  normalizeTurnLifecycle,
  terminalTurnPatch,
  visibleChatForms,
} from "./turnLifecycle.js";
import {
  canConfirmPlan,
  planHasClarificationConflict,
  planHasExecutionStarted,
} from "./planState.js";

const formTool = (id = "ask1") => ({
  id,
  name: "ask_form",
  status: "done",
  result: {
    form: true,
    kind: "questions",
    title: "New product",
    questions: [
      {
        key: "category",
        title: "Category",
        type: "single",
        choices: [
          { value: "figure", label: "Figure" },
          { value: "accessory", label: "Accessory" },
        ],
      },
      { key: "name", title: "Product name", type: "text" },
    ],
  },
});

const incidentAssistant = () => {
  const tools = [
    {
      id: "sql-1",
      name: "sql_readonly",
      status: "done",
      result: { error: "ORDER BY column is not allowlisted" },
    },
    {
      id: "form-1",
      name: "ask_form",
      status: "done",
      result: {
        error: "unknown_follow_up_fields",
        form: false,
        unknownFields: ["sku", "price", "quantity"],
      },
    },
    formTool("form-2"),
  ];
  return {
    id: 2,
    role: "assistant",
    content: "",
    done: true,
    phase: "chat",
    plan: null,
    planFailed: true,
    planExecuted: false,
    messageState: "completed",
    toolCalls: tools,
    timeline: tools.map((tool) => ({ type: "tool", id: tool.id, tool })),
  };
};

describe("hasExecutionContext", () => {
  it("treats a plan outcome flag alone as no execution evidence", () => {
    const msg = incidentAssistant();
    assert.equal(hasExecutionContext(msg), false);
    assert.equal(planHasExecutionStarted(msg), false);
    for (const candidate of [
      { plan: { proposalId: "p" }, planFailed: true },
      { role: "assistant", phase: "review", planFailed: true },
      { role: "assistant", phase: "chat", planExecuted: true },
    ]) {
      assert.equal(hasExecutionContext(candidate), false);
    }
  });

  it("accepts real execution evidence", () => {
    assert.equal(
      hasExecutionContext({ role: "assistant", phase: "execution" }),
      true,
    );
    assert.equal(
      hasExecutionContext({ plan: {}, planConfirmed: true }),
      true,
    );
    assert.equal(
      hasExecutionContext({ plan: {}, planExecStart: 2 }),
      true,
    );
    assert.equal(
      hasExecutionContext({ plan: {}, executionOutcome: { status: "ok" } }),
      true,
    );
    assert.equal(
      hasExecutionContext({ plan: {}, planExecuted: true }),
      true,
    );
    // A canonical plan row with a phase never relies on a bare flag.
    assert.equal(
      hasExecutionContext({ plan: {}, planExecuted: true, phase: "chat" }),
      false,
    );
  });
});

describe("normalizeTurnLifecycle", () => {
  it("clears invented plan flags on a planless chat turn, keeping tools", () => {
    const restored = normalizeTurnLifecycle(incidentAssistant());
    assert.equal(restored.planFailed, false);
    assert.equal(restored.planExecuted, false);
    assert.equal(restored.toolCalls.length, 3);
    // A successful repaired form stays visible on the normalized turn.
    assert.equal(visibleChatForms(restored).length, 1);
    assert.equal(visibleChatForms(restored)[0].id, "form-2");
  });

  it("clears flags on chat/review plan rows and leaves ambiguous legacy rows", () => {
    const chatPlan = normalizeTurnLifecycle({
      role: "assistant",
      phase: "chat",
      plan: { proposalId: "p" },
      planFailed: true,
    });
    assert.equal(chatPlan.planFailed, false);
    const reviewPlan = normalizeTurnLifecycle({
      role: "assistant",
      phase: "review",
      plan: { proposalId: "p" },
      planExecuted: true,
    });
    assert.equal(reviewPlan.planExecuted, false);
    // An unphased legacy plan failure is ambiguous: keep the conservative
    // retry guard rather than silently clearing history.
    const legacy = normalizeTurnLifecycle({
      role: "assistant",
      plan: { proposalId: "p" },
      planFailed: true,
    });
    assert.equal(legacy.planFailed, true);
    const executed = normalizeTurnLifecycle({
      role: "assistant",
      plan: { proposalId: "p" },
      planConfirmed: true,
      planExecuted: true,
    });
    assert.equal(executed.planExecuted, true);
  });
});

describe("terminalTurnPatch", () => {
  it("ignores ok on chat/review turns and applies it on execution", () => {
    const chat = {
      role: "assistant",
      phase: "chat",
      messageState: "running",
      toolCalls: [],
    };
    const chatPatch = terminalTurnPatch(chat, { ok: false, modelName: "m" });
    assert.equal(chatPatch.planExecuted, undefined);
    assert.equal(chatPatch.planFailed, undefined);
    assert.equal(chatPatch.messageState, "completed");
    const exec = {
      role: "assistant",
      plan: { proposalId: "p" },
      planConfirmed: true,
      messageState: "running",
      toolCalls: [],
    };
    const execPatch = terminalTurnPatch(exec, { ok: false });
    assert.equal(execPatch.planExecuted, true);
    assert.equal(execPatch.planFailed, true);
    // A fresh review continuation is not an execution segment: a stray
    // terminal ok on its own done must not mark the new proposal executed.
    const review = {
      role: "assistant",
      phase: "review",
      executionProposalId: "parent-plan",
      plan: { proposalId: "recovery-plan" },
      messageState: "running",
      toolCalls: [],
    };
    const reviewPatch = terminalTurnPatch(review, { ok: true });
    assert.equal(reviewPatch.planExecuted, undefined);
    assert.equal(reviewPatch.planFailed, undefined);
    const ended = { ...review, ...reviewPatch };
    assert.equal(ended.planExecuted, undefined);
    assert.equal(canConfirmPlan(ended), true);
  });

  it("marks interrupted turns so a proposal cannot be confirmed", () => {
    const review = {
      role: "assistant",
      phase: "review",
      plan: { proposalId: "p" },
      done: false,
      messageState: "running",
    };
    const patch = terminalTurnPatch(review, { interrupted: true });
    assert.equal(patch.messageState, "interrupted");
    const ended = { ...review, ...patch };
    assert.equal(canConfirmPlan(ended), false);
  });
});

describe("isCurrentFormOwner", () => {
  it("accepts only the completed latest assistant turn", () => {
    const owner = incidentAssistant();
    const other = { id: 3, role: "assistant", done: true };
    assert.equal(isCurrentFormOwner([owner], owner), true);
    assert.equal(isCurrentFormOwner([owner, other], owner), false);
    assert.equal(
      isCurrentFormOwner([owner], { ...owner, done: false }),
      false,
    );
    assert.equal(
      isCurrentFormOwner([owner], { ...owner, turnStopped: true }),
      false,
    );
    assert.equal(
      isCurrentFormOwner([owner], { ...owner, messageState: "stopped" }),
      false,
    );
  });
});

describe("visibleChatForms", () => {
  it("keeps an unanswered form on a normalized chat turn", () => {
    const msg = normalizeTurnLifecycle(incidentAssistant());
    const forms = visibleChatForms(msg);
    assert.equal(forms.length, 1);
    assert.equal(forms[0].questions.length, 2);
  });

  it("hides unanswered proposal forms after execution but keeps recaps", () => {
    const msg = {
      role: "assistant",
      plan: { proposalId: "p" },
      planConfirmed: true,
      planExecuted: true,
      planExecStart: 1,
      timeline: [
        { type: "tool", id: "ask1", tool: formTool("ask1") },
        {
          type: "tool",
          id: "exec-1",
          tool: { id: "exec-1", name: "platform_request", status: "done" },
        },
      ],
      toolCalls: [formTool("ask1")],
    };
    assert.equal(visibleChatForms(msg).length, 0);
    const answered = {
      ...msg,
      formReplies: { ask1: { answers: { name: "Mug" } } },
    };
    const forms = visibleChatForms(answered);
    assert.equal(forms.length, 1);
    assert.equal(String(forms[0].id), "ask1");
  });
});

describe("clarification conflict after recovery", () => {
  it("still blocks an old plan sharing a turn with the repaired form", () => {
    const msg = {
      role: "assistant",
      phase: "chat",
      done: true,
      plan: { proposalId: "old", summary: "Create the product" },
      toolCalls: [formTool("ask1")],
      formReplies: { ask1: { answers: { name: "Mug" } } },
    };
    assert.equal(planHasClarificationConflict(msg), true);
    assert.equal(canConfirmPlan(msg), false);
  });

  it("does not treat a recovered chat proposal as executed or failed", () => {
    const msg = normalizeTurnLifecycle({
      role: "assistant",
      phase: "chat",
      done: true,
      plan: {
        proposalId: "p1",
        summary: "Create the product from the submitted answers.",
        steps: [
          {
            description: "Create Mug",
            tool: "platform_request",
            args: { method: "POST", path: "/products", body: {} },
          },
        ],
      },
      planFailed: true,
      toolCalls: [
        {
          id: "sql-1",
          name: "sql_readonly",
          status: "done",
          result: { error: "ORDER BY column is not allowlisted" },
        },
      ],
    });
    assert.equal(msg.planFailed, false);
    assert.equal(hasExecutionContext(msg), false);
    assert.equal(planHasClarificationConflict(msg), false);
    assert.equal(canConfirmPlan(msg), true);
  });
});
