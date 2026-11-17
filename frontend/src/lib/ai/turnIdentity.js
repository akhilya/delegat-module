import { randomUuid } from "../../utils/uuid.js";
import { emptyAssistantMessage } from "./persist.js";

/**
 * Canonical turn identity helpers. The live turn stamps every event with
 * runId/messageId/sequence; ownership is NEVER inferred from role, content,
 * position, or the review's parent execution link.
 */

export function isCanonicalEvent(event) {
  return !!(
    event &&
    typeof event.runId === "string" &&
    event.runId &&
    typeof event.messageId === "string" &&
    event.messageId &&
    Number.isSafeInteger(event.sequence) &&
    event.sequence > 0
  );
}

/** Same canonical message+run, event already persisted/seen. */
export function isDuplicateTurnEvent(message, event) {
  return !!(
    message &&
    message.messageId === event.messageId &&
    message.runId === event.runId &&
    Number.isSafeInteger(message.lastEventSequence) &&
    event.sequence <= message.lastEventSequence
  );
}

function adoptIdentity(msg, event) {
  const changedRun = msg.runId !== event.runId;
  if (changedRun) {
    // A new run restarts the sequence window and the lifecycle: stale
    // sequences from the previous run must not drop the new run's events.
    msg.lastEventSequence = 0;
    msg.done = false;
    msg.messageState = "running";
  }
  msg.messageId = event.messageId;
  msg.runId = event.runId;
  msg.phase = event.phase || msg.phase;
  if (Number.isInteger(event.dbId)) msg.dbId = event.dbId;
  if (
    Number.isSafeInteger(event.sequence) &&
    event.sequence > (msg.lastEventSequence || 0)
  ) {
    msg.lastEventSequence = event.sequence;
  }
  return msg;
}

/**
 * The first execution message_start binds the confirmed proposal row: set
 * planConfirmed and planExecStart before any tool event, never a new card.
 * Applies to any selected owner - including an already-canonical row starting
 * a new run - and never erases the proposal timeline.
 */
function initExecutionOwner(msg, event) {
  if (event.type !== "message_start" || event.phase !== "execution") return;
  if (!msg.plan || msg.plan.proposalId !== event.proposalId) return;
  msg.planConfirmed = true;
  if (!Number.isInteger(msg.planExecStart)) {
    msg.planExecStart = (msg.timeline || []).length;
  }
}

/**
 * Select the message a canonical event targets. Called inside the
 * selectStreamMessage mutation; mutates `messages` only to adopt/append the
 * message the event identifies. Returns the message or null.
 */
export function selectTurnMessage(messages, event) {
  const byIdentity = messages.find((m) => m.messageId === event.messageId);
  if (byIdentity) {
    const selected = adoptIdentity(byIdentity, event);
    initExecutionOwner(selected, event);
    return selected;
  }

  // A canonical DbId may adopt an uncanonical legacy row, but never overwrite
  // a different nonempty canonical identity.
  if (Number.isInteger(event.dbId)) {
    const byDbId = messages.find((m) => m.dbId === event.dbId);
    if (byDbId && (!byDbId.messageId || byDbId.messageId === event.messageId)) {
      const selected = adoptIdentity(byDbId, event);
      initExecutionOwner(selected, event);
      return selected;
    }
  }

  if (event.type === "message_start" && event.phase === "execution") {
    const owner = messages.find(
      (m) =>
        m.plan &&
        m.plan.proposalId === event.proposalId &&
        (!m.messageId || m.messageId === event.messageId),
    );
    if (owner) {
      const selected = adoptIdentity(owner, event);
      initExecutionOwner(selected, event);
      return selected;
    }
  }

  if (event.type === "assistant_start") {
    // A review message owns a distinct messageId; the parent link lives in data,
    // never in ownership. Do not require the parent to be the last row.
    const msg = emptyAssistantMessage();
    msg.id = randomUuid();
    const parent = event.data?.executionProposalId;
    if (parent) msg.executionProposalId = parent;
    messages.push(adoptIdentity(msg, event));
    return msg;
  }

  // message_start (chat) or mid-run data events: adopt only an empty unowned
  // trailing placeholder; otherwise append a fresh canonical message.
  const last = messages.at(-1);
  if (
    last?.role === "assistant" &&
    !last.messageId &&
    !last.dbId &&
    !last.content &&
    !last.toolCalls?.length &&
    !last.plan
  ) {
    return adoptIdentity(last, event);
  }

  const msg = emptyAssistantMessage();
  msg.id = randomUuid();
  messages.push(adoptIdentity(msg, event));
  return msg;
}

/**
 * Correlate saved response rows to exactly the submitted messages (the filtered
 * set captured before await), then to the same live state messages. Hidden
 * control lines, placeholders, and messages appended while the request was in
 * flight can never receive an unrelated dbId.
 */
export function assignSavedMessageIds(messages, saved, submitted) {
  if (
    !Array.isArray(messages) ||
    !Array.isArray(saved) ||
    !Array.isArray(submitted)
  )
    return;
  const submittedByClientId = new Map();
  for (const m of submitted) {
    if (m?.id != null) submittedByClientId.set(String(m.id), m);
  }
  // Submitted rows are immutable identity proof only; dbIds land on the
  // current state row so a list cloned/replaced during the await still
  // receives them.
  const liveRow = (submittedMsg) => {
    if (!submittedMsg) return null;
    const live = messages.includes(submittedMsg)
      ? submittedMsg
      : messages.find((m) => m.id === submittedMsg.id);
    if (!live || live.role !== submittedMsg.role) return null;
    if (submittedMsg.messageId && live.messageId !== submittedMsg.messageId)
      return null;
    if (submittedMsg.dbId != null && live.dbId !== submittedMsg.dbId)
      return null;
    return live;
  };
  // Each submitted row may be assigned at most once: with cloned current
  // state the assignments land on different objects than the captured
  // snapshot, so a used-set (not live dbId presence) prevents re-selection.
  const usedSubmitted = new Set();
  for (const row of saved) {
    if (!row || !Number.isInteger(row.dbId)) continue;
    const clientId =
      row.clientMessageId != null ? String(row.clientMessageId) : null;
    let live = null;
    if (clientId != null) {
      // An unknown supplied correlation key is skipped: never a positional
      // fallback onto an unrelated submitted message.
      const submittedMsg = submittedByClientId.get(clientId);
      if (!submittedMsg) continue;
      usedSubmitted.add(submittedMsg);
      live = liveRow(submittedMsg);
    } else {
      // Older servers return no correlation: fall back only to the captured
      // submitted messages, never to later state mutations, each at most
      // once.
      for (const candidate of submitted) {
        if (
          usedSubmitted.has(candidate) ||
          candidate.dbId != null ||
          candidate.role !== row.role
        )
          continue;
        const found = liveRow(candidate);
        if (found) {
          usedSubmitted.add(candidate);
          live = found;
          break;
        }
      }
    }
    if (!live) continue;
    if (row.role && live.role !== row.role) continue;
    if (live.dbId != null && live.dbId !== row.dbId) continue;
    if (row.messageId && live.messageId && live.messageId !== row.messageId)
      continue;
    live.dbId = row.dbId;
    if (row.messageId) live.messageId = row.messageId;
  }
}

/**
 * Read-side normalization for rows where a legacy writer copied the parent plan
 * into a review message. Suppression requires strict ownership evidence:
 * a different nonempty messageId vs planOwnerMessageId, a real owner row with a
 * stable messageId, the exact same plan.proposalId, and copied approval/execution
 * flags that match the owner. Anything ambiguous is left untouched.
 */
export function normalizeCopiedPlans(messages) {
  for (const m of messages) {
    if (
      !m?.plan ||
      !m.messageId ||
      !m.planOwnerMessageId ||
      !Number.isInteger(m.dbId) ||
      m.dbId <= 0
    )
      continue;
    if (m.messageId === m.planOwnerMessageId) continue; // owner=self is legitimate
    const owner = messages.find(
      (candidate) =>
        candidate !== m &&
        Number.isInteger(candidate.dbId) &&
        candidate.dbId > 0 &&
        candidate.dbId !== m.dbId &&
        candidate.messageId === m.planOwnerMessageId &&
        candidate.messageId === candidate.planOwnerMessageId &&
        candidate.plan?.proposalId &&
        candidate.plan.proposalId === m.plan.proposalId,
    );
    // The copy must also claim the owner's own proposal as its parent: a
    // review without that parent link may legitimately share proposal text.
    if (!owner || m.executionProposalId !== owner.plan.proposalId) continue;
    const copiedFlags = [
      "planConfirmed",
      "planExecuted",
      "planFailed",
      "planExecStart",
    ];
    if (!copiedFlags.every((key) => m[key] === owner[key])) continue;
    // Proven copied plan: strip the duplicate plan/flags from the in-memory
    // projection only; the stored row is never mutated here.
    m.plan = null;
    m.planConfirmed = false;
    m.planExecuted = false;
    m.planFailed = false;
    m.planExecStart = undefined;
  }
  return messages;
}
