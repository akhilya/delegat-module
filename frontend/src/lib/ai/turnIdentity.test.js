import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  assignSavedMessageIds,
  isCanonicalEvent,
  isDuplicateTurnEvent,
  normalizeCopiedPlans,
  selectTurnMessage,
} from "./turnIdentity.js";
import { mapStoredMessage, serializeToolCallsJson } from "./persist.js";

const canon = (over = {}) => ({
  type: "text",
  runId: "run-1",
  messageId: "srv-1",
  sequence: 1,
  ...over,
});

describe("isCanonicalEvent", () => {
  it("requires a nonempty runId, messageId, and a safe positive sequence", () => {
    assert.equal(isCanonicalEvent(canon()), true);
    for (const bad of [
      null,
      {},
      canon({ runId: "" }),
      canon({ runId: 7 }),
      canon({ messageId: null }),
      canon({ sequence: 0 }),
      canon({ sequence: -2 }),
      canon({ sequence: 1.5 }),
      canon({ sequence: "3" }),
    ]) {
      assert.equal(isCanonicalEvent(bad), false);
    }
  });
});

describe("isDuplicateTurnEvent", () => {
  it("skips events already persisted for the same run and message", () => {
    const msg = { messageId: "srv-1", runId: "run-1", lastEventSequence: 4 };
    assert.equal(isDuplicateTurnEvent(msg, canon({ sequence: 4 })), true);
    assert.equal(isDuplicateTurnEvent(msg, canon({ sequence: 2 })), true);
    assert.equal(isDuplicateTurnEvent(msg, canon({ sequence: 5 })), false);
    assert.equal(
      isDuplicateTurnEvent(msg, canon({ runId: "run-2" })),
      false,
    );
    assert.equal(
      isDuplicateTurnEvent(
        { messageId: "srv-2", runId: "run-1", lastEventSequence: 4 },
        canon({ sequence: 1 }),
      ),
      false,
    );
    // A loaded row without sequence metadata is never skipped by the guard.
    assert.equal(
      isDuplicateTurnEvent(
        { messageId: "srv-1", runId: "run-1" },
        canon({ sequence: 1 }),
      ),
      false,
    );
  });
});

describe("selectTurnMessage", () => {
  it("replaying stable events twice targets one message without duplicating", () => {
    const messages = [];
    const start = canon({
      type: "message_start",
      phase: "chat",
      sequence: 1,
      dbId: 10,
      data: {},
    });
    const first = selectTurnMessage(messages, start);
    assert.equal(messages.length, 1);
    assert.equal(first.messageId, "srv-1");
    assert.equal(first.runId, "run-1");
    assert.equal(first.dbId, 10);
    assert.equal(first.lastEventSequence, 1);

    const again = selectTurnMessage(messages, start);
    assert.equal(messages.length, 1);
    assert.equal(again, first);

    const later = selectTurnMessage(messages, canon({ sequence: 9 }));
    assert.equal(messages.length, 1);
    assert.equal(later, first);
    assert.equal(first.lastEventSequence, 9);
  });

  it("the first execution message_start binds the confirmed proposal row", () => {
    const owner = {
      id: 5,
      role: "assistant",
      content: "Ready.",
      toolCalls: [],
      timeline: [{ type: "text", text: "Ready." }],
      plan: { proposalId: "A", summary: "Calc" },
      planConfirmed: false,
    };
    const messages = [owner];
    const event = canon({
      type: "message_start",
      phase: "execution",
      proposalId: "A",
      sequence: 2,
      dbId: 20,
      messageId: "srv-exec",
      data: {},
    });
    const selected = selectTurnMessage(messages, event);
    assert.equal(selected, owner);
    assert.equal(messages.length, 1);
    assert.equal(owner.messageId, "srv-exec");
    assert.equal(owner.dbId, 20);
    assert.equal(owner.planConfirmed, true);
    // planExecStart freezes the proposal timeline before execution tools land.
    assert.equal(owner.planExecStart, 1);
  });

  it("a review assistant_start creates a distinct message with a parent link", () => {
    const parent = {
      id: 1,
      role: "assistant",
      content: "Done.",
      messageId: "srv-a",
      runId: "run-1",
      plan: { proposalId: "A" },
      planConfirmed: true,
      planExecuted: true,
    };
    const unrelated = { id: 2, role: "assistant", content: "later" };
    // The parent is not the last row: selection must not require it.
    const messages = [parent, unrelated];
    const review = selectTurnMessage(
      messages,
      canon({
        type: "assistant_start",
        messageId: "srv-b",
        sequence: 6,
        data: { executionProposalId: "A" },
      }),
    );
    assert.equal(messages.length, 3);
    assert.notEqual(review, parent);
    assert.notEqual(review, unrelated);
    assert.equal(review.messageId, "srv-b");
    assert.equal(review.executionProposalId, "A");
    // The review does not inherit the parent's plan or approval flags.
    assert.equal(review.plan, null);
    assert.equal(review.planExecuted, undefined);
  });

  it("two reviews sharing a parent keep distinct canonical identities", () => {
    const parent = {
      id: 1,
      role: "assistant",
      messageId: "srv-a",
      plan: { proposalId: "A" },
      planExecuted: true,
    };
    const messages = [parent];
    const b1 = selectTurnMessage(
      messages,
      canon({
        type: "assistant_start",
        messageId: "srv-b1",
        data: { executionProposalId: "A" },
      }),
    );
    const b2 = selectTurnMessage(
      messages,
      canon({
        type: "assistant_start",
        messageId: "srv-b2",
        sequence: 2,
        data: { executionProposalId: "A" },
      }),
    );
    assert.equal(messages.length, 3);
    assert.notEqual(b1, b2);
    // Data events target their own messageId, not the trailing message.
    const targeted = selectTurnMessage(
      messages,
      canon({ messageId: "srv-b1", sequence: 3 }),
    );
    assert.equal(targeted, b1);
  });

  it("a canonical dbId adopts an uncanonical legacy row", () => {
    const legacy = { id: 9, dbId: 42, role: "assistant", content: "hi" };
    const messages = [legacy];
    const selected = selectTurnMessage(
      messages,
      canon({ dbId: 42, type: "message_start", phase: "chat", data: {} }),
    );
    assert.equal(selected, legacy);
    assert.equal(legacy.messageId, "srv-1");
    assert.equal(legacy.runId, "run-1");
  });

  it("adopts an empty unowned trailing placeholder instead of appending", () => {
    const placeholder = {
      id: 7,
      role: "assistant",
      content: "",
      toolCalls: [],
      timeline: [],
      plan: null,
    };
    const messages = [
      { id: 1, role: "assistant", content: "full" },
      placeholder,
    ];
    const selected = selectTurnMessage(
      messages,
      canon({ type: "message_start", phase: "chat", data: {} }),
    );
    assert.equal(selected, placeholder);
    assert.equal(messages.length, 2);
    assert.equal(placeholder.messageId, "srv-1");
  });

  it("never merges forged unequal canonical identities", () => {
    const real = { id: 1, role: "assistant", messageId: "srv-a", runId: "r" };
    const messages = [real];
    const selected = selectTurnMessage(
      messages,
      canon({ messageId: "srv-forged", runId: "r", sequence: 1 }),
    );
    assert.equal(messages.length, 2);
    assert.notEqual(selected, real);
    assert.equal(real.messageId, "srv-a");
  });

  it("an execution run transition resets the sequence window on the owner", () => {
    // The proposal run ended at seq 40; the execution run restarts at seq 1.
    // Events 2..4 must be accepted, not dropped as stale duplicates, and the
    // execution binding initializes on the already-canonical owner.
    const owner = {
      id: 1,
      role: "assistant",
      messageId: "srv-a",
      runId: "run-propose",
      lastEventSequence: 40,
      done: true,
      messageState: "completed",
      plan: { proposalId: "A", summary: "Calc" },
      timeline: [{ type: "text", text: "Ready." }],
      toolCalls: [],
      content: "Ready.",
    };
    const messages = [owner];
    const start = selectTurnMessage(
      messages,
      canon({
        type: "message_start",
        phase: "execution",
        proposalId: "A",
        runId: "run-exec",
        messageId: "srv-a",
        sequence: 1,
        dbId: 2,
        data: {},
      }),
    );
    assert.equal(start, owner);
    assert.equal(messages.length, 1);
    assert.equal(owner.runId, "run-exec");
    assert.equal(owner.lastEventSequence, 1);
    assert.equal(owner.done, false);
    assert.equal(owner.messageState, "running");
    assert.equal(owner.planConfirmed, true);
    assert.equal(owner.planExecStart, 1);
    assert.equal(owner.timeline.length, 1); // proposal timeline preserved
    for (const seq of [2, 3, 4]) {
      assert.equal(
        isDuplicateTurnEvent(
          owner,
          canon({ runId: "run-exec", sequence: seq }),
        ),
        false,
        `sequence ${seq} must be accepted after the run transition`,
      );
    }
  });

  it("a dbId fallback never overwrites a different canonical identity", () => {
    const canonical = { id: 1, role: "assistant", messageId: "srv-a", dbId: 5 };
    const messages = [canonical];
    const selected = selectTurnMessage(
      messages,
      canon({ messageId: "srv-b", dbId: 5 }),
    );
    assert.notEqual(selected, canonical);
    assert.equal(canonical.messageId, "srv-a");
    assert.equal(messages.length, 2);
  });

  it("a proposal fallback never binds a row with a different canonical identity", () => {
    const owner = {
      id: 1,
      role: "assistant",
      messageId: "srv-other",
      plan: { proposalId: "A" },
      timeline: [],
    };
    const messages = [owner];
    const selected = selectTurnMessage(
      messages,
      canon({
        type: "message_start",
        phase: "execution",
        proposalId: "A",
        messageId: "srv-exec",
      }),
    );
    assert.notEqual(selected, owner);
    assert.equal(owner.messageId, "srv-other");
  });

  it("a same-run replay of a persisted final snapshot stays unchanged", () => {
    const persisted = {
      id: 1,
      role: "assistant",
      messageId: "srv-a",
      runId: "run-1",
      lastEventSequence: 6,
      done: true,
      messageState: "completed",
      content: "Final",
    };
    const messages = [persisted];
    selectTurnMessage(
      messages,
      canon({ messageId: "srv-a", sequence: 3 }),
    );
    assert.equal(messages.length, 1);
    assert.equal(persisted.done, true);
    assert.equal(persisted.messageState, "completed");
    assert.equal(persisted.lastEventSequence, 6);
  });
});

describe("assignSavedMessageIds", () => {
  it("correlates saved rows to exactly the submitted messages", () => {
    const hidden = { id: 100, role: "user", content: "[control]" };
    const user = { id: 101, role: "user", content: "hi" };
    const assistant = { id: 102, role: "assistant", content: "answer" };
    // A new local message arrived while the request was in flight; it is not
    // part of `submitted` and must never receive a dbId from this response.
    const live = {
      messages: [hidden, user, assistant, { id: 103, role: "user", content: "new" }],
    };
    const submitted = [hidden, user, assistant];
    assignSavedMessageIds(live.messages, [
      { role: "user", dbId: 11, clientMessageId: "101" },
      { role: "assistant", dbId: 12, clientMessageId: "102", messageId: "srv-m" },
    ], submitted);
    assert.equal(hidden.dbId, undefined);
    assert.equal(user.dbId, 11);
    assert.equal(assistant.dbId, 12);
    assert.equal(assistant.messageId, "srv-m");
    assert.equal(live.messages[3].dbId, undefined);
  });

  it("older servers without correlation fall back only to submitted rows", () => {
    const placeholder = { id: 1, role: "user", content: "x" };
    const submitted = [placeholder];
    const messages = [
      placeholder,
      { id: 2, role: "user", content: "appended later" },
    ];
    assignSavedMessageIds(messages, [{ role: "user", dbId: 5 }], submitted);
    assert.equal(placeholder.dbId, 5);
    assert.equal(messages[1].dbId, undefined);
  });

  it("assigns each uncorrelated saved row to a distinct submitted message", () => {
    // Immutable identity snapshots (as syncHistory captures) with cloned
    // live state: the legacy positional fallback must not hand both saved
    // ids to the same first row.
    const submitted = [
      { id: 10, role: "user" },
      { id: 11, role: "user" },
    ];
    const messages = [
      { id: 10, role: "user", content: "clone 1" },
      { id: 11, role: "user", content: "clone 2" },
      { id: 12, role: "user", content: "appended after submit" },
    ];
    assignSavedMessageIds(
      messages,
      [
        { role: "user", dbId: 21 },
        { role: "user", dbId: 22 },
      ],
      submitted,
    );
    assert.equal(messages[0].dbId, 21);
    assert.equal(messages[1].dbId, 22);
    assert.equal(messages[2].dbId, undefined);
  });

  it("correlates through cloned current rows, not stale submitted refs", () => {
    const submitted = [
      { id: 1, role: "user", content: "hi" },
      { id: 2, role: "assistant", content: "ans" },
    ];
    // State was replaced by clones while the request was in flight; the saved
    // dbIds must still reach the visible rows.
    const current = submitted.map((m) => ({ ...m }));
    assignSavedMessageIds(
      current,
      [
        { role: "user", dbId: 5, clientMessageId: "1" },
        { role: "assistant", dbId: 6, clientMessageId: "2", messageId: "srv-m" },
      ],
      submitted,
    );
    assert.equal(current[0].dbId, 5);
    assert.equal(current[1].dbId, 6);
    assert.equal(current[1].messageId, "srv-m");
  });

  it("skips an unknown supplied clientMessageId without positional fallback", () => {
    const submitted = [
      { id: 1, role: "user", content: "hi" },
      { id: 2, role: "assistant", content: "ans" },
    ];
    assignSavedMessageIds(
      submitted,
      [{ role: "user", dbId: 9, clientMessageId: "999" }],
      submitted,
    );
    assert.equal(submitted[0].dbId, undefined);
    assert.equal(submitted[1].dbId, undefined);
  });

  it("never replaces a different canonical id or an existing different dbId", () => {
    const submitted = [
      { id: 2, role: "assistant", content: "ans", messageId: "srv-b" },
      { id: 3, role: "assistant", content: "older", dbId: 7 },
    ];
    assignSavedMessageIds(
      submitted,
      [
        { role: "assistant", dbId: 9, clientMessageId: "2", messageId: "srv-x" },
        { role: "assistant", dbId: 8, clientMessageId: "3" },
      ],
      submitted,
    );
    assert.equal(submitted[0].messageId, "srv-b");
    assert.equal(submitted[0].dbId, undefined);
    assert.equal(submitted[1].dbId, 7);
  });
});

describe("normalizeCopiedPlans", () => {
  const ownerRow = () => ({
    id: 1,
    role: "assistant",
    dbId: 7,
    messageId: "srv-a",
    planOwnerMessageId: "srv-a",
    plan: { proposalId: "A" },
    planConfirmed: true,
    planExecuted: true,
    planFailed: false,
    planExecStart: 2,
  });

  it("suppresses a proven copied plan from the projection only", () => {
    const owner = ownerRow();
    const copied = {
      id: 2,
      role: "assistant",
      dbId: 8,
      messageId: "srv-b",
      planOwnerMessageId: "srv-a",
      executionProposalId: "A",
      plan: { proposalId: "A" },
      planConfirmed: true,
      planExecuted: true,
      planFailed: false,
      planExecStart: 2,
    };
    normalizeCopiedPlans([owner, copied]);
    assert.equal(copied.plan, null);
    assert.equal(copied.planExecuted, false);
    assert.equal(owner.plan.proposalId, "A");
  });

  it("keeps a legitimate review whose owner is itself", () => {
    const owner = ownerRow();
    const legit = {
      id: 2,
      role: "assistant",
      dbId: 8,
      messageId: "srv-b",
      planOwnerMessageId: "srv-b",
      executionProposalId: "A",
      plan: { proposalId: "B" },
      planConfirmed: false,
    };
    normalizeCopiedPlans([owner, legit]);
    assert.equal(legit.plan.proposalId, "B");
  });

  it("requires positive dbIds, self-ownership, and the parent link", () => {
    const owner = ownerRow();
    const base = {
      role: "assistant",
      messageId: "srv-b",
      planOwnerMessageId: "srv-a",
      executionProposalId: "A",
      plan: { proposalId: "A" },
      planConfirmed: true,
      planExecuted: true,
      planFailed: false,
      planExecStart: 2,
    };
    const cases = [
      { ...base, id: 2, dbId: 8 }, // sanity: full evidence still suppresses
      { ...base, id: 3 }, // no dbId on the copied row
      { ...base, id: 4, dbId: 7 }, // same dbId as the owner
      { ...base, id: 5, dbId: 8, executionProposalId: "B" }, // wrong parent
      { ...base, id: 6, dbId: 8, executionProposalId: undefined },
    ];
    normalizeCopiedPlans([owner, ...cases]);
    assert.equal(cases[0].plan, null);
    for (const m of cases.slice(1)) {
      assert.ok(m.plan, `row ${m.id} kept its plan`);
    }
    // An owner that does not own itself cannot prove the copy either.
    const nonSelfOwner = { ...ownerRow(), planOwnerMessageId: "srv-x" };
    const copy = {
      ...base,
      id: 7,
      dbId: 9,
    };
    normalizeCopiedPlans([nonSelfOwner, copy]);
    assert.ok(copy.plan);
  });

  it("leaves ambiguous or missing ownership untouched", () => {
    const owner = ownerRow();
    const cases = [
      // No owner row with that messageId.
      {
        id: 2,
        role: "assistant",
        messageId: "srv-b",
        planOwnerMessageId: "srv-missing",
        plan: { proposalId: "A" },
        planConfirmed: true,
        planExecuted: true,
        planFailed: false,
        planExecStart: 2,
      },
      // Different proposal than the owner: a real distinct plan, not a copy.
      {
        id: 3,
        role: "assistant",
        dbId: 9,
        messageId: "srv-c",
        planOwnerMessageId: "srv-a",
        executionProposalId: "A",
        plan: { proposalId: "C" },
        planConfirmed: false,
        planExecuted: false,
        planFailed: false,
        planExecStart: undefined,
      },
      // Flags disagree with the owner: cannot prove a copy.
      {
        id: 4,
        role: "assistant",
        dbId: 10,
        messageId: "srv-d",
        planOwnerMessageId: "srv-a",
        executionProposalId: "A",
        plan: { proposalId: "A" },
        planConfirmed: true,
        planExecuted: false,
        planFailed: false,
        planExecStart: 2,
      },
      // No canonical identity at all: anonymous legacy rows are never hidden.
      {
        id: 5,
        role: "assistant",
        plan: { proposalId: "A" },
        planConfirmed: true,
        planExecuted: true,
        planFailed: false,
        planExecStart: 2,
      },
    ];
    normalizeCopiedPlans([owner, ...cases]);
    for (const m of cases) assert.ok(m.plan, `row ${m.id} kept its plan`);
  });
});

describe("canonical serialize/hydrate round-trip", () => {
  it("preserves identity fields, messageState, and executionOutcome", () => {
    const message = {
      role: "assistant",
      content: "summary",
      messageId: "srv-1",
      runId: "run-1",
      phase: "execution",
      lastEventSequence: 8,
      messageState: "completed",
      planOwnerMessageId: "srv-1",
      executionProposalId: null,
      executionOutcome: { status: "partial", succeeded: 1, total: 2 },
      plan: { proposalId: "A" },
      planExecuted: true,
      toolCalls: [],
      timeline: [],
    };
    const restored = mapStoredMessage({
      dbId: 3,
      role: "assistant",
      content: "summary",
      toolCallsJson: serializeToolCallsJson(message),
    });
    assert.equal(restored.messageId, "srv-1");
    assert.equal(restored.runId, "run-1");
    assert.equal(restored.phase, "execution");
    assert.equal(restored.lastEventSequence, 8);
    assert.equal(restored.messageState, "completed");
    assert.equal(restored.planOwnerMessageId, "srv-1");
    assert.deepEqual(restored.executionOutcome, {
      status: "partial",
      succeeded: 1,
      total: 2,
    });
    // done follows canonical messageState, not just the role.
    assert.equal(restored.done, true);
  });

  it("a running canonical row hydrates as not-done", () => {
    const restored = mapStoredMessage({
      dbId: 4,
      role: "assistant",
      content: "",
      toolCallsJson: JSON.stringify({
        _ui: "assistantTurn",
        messageId: "srv-9",
        runId: "run-9",
        messageState: "running",
        tools: [],
        timeline: [],
      }),
    });
    assert.equal(restored.done, false);
  });
});
