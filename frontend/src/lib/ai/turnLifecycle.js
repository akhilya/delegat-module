import { collectChatForms, timelineFromLegacy } from "./timeline.js";

/**
 * Turn lifecycle policy shared by hydration, live terminal handling, and the
 * chat UI. Plan outcome flags are meaningful only on a real execution segment:
 * a failed read or rejected draft form inside a chat/review turn is tool
 * evidence, never a plan outcome.
 */

export function hasExecutionContext(message) {
  if (message?.phase === "execution") return true;
  if (!message?.plan) return false;
  return !!(
    message.planConfirmed ||
    (Number.isInteger(message.planExecStart) && message.planExecStart >= 0) ||
    message.executionOutcome ||
    (!message.phase && message.planExecuted)
  );
}

// A failed read or invalid draft form is not an executed plan. Keep ambiguous
// unphased legacy plan failures conservative; never clear their retry guard.
export function normalizeTurnLifecycle(message) {
  if (message?.role !== "assistant" || hasExecutionContext(message))
    return message;
  if (!message.plan || message.phase === "chat" || message.phase === "review") {
    return { ...message, planFailed: false, planExecuted: false };
  }
  return message;
}

export function terminalTurnPatch(message, data = {}) {
  const patch = { done: true };
  if (data.modelName) patch.modelName = data.modelName;
  if (data.interrupted === true) patch.messageState = "interrupted";
  else if (message.messageState === "running") patch.messageState = "completed";
  if (hasExecutionContext(message) && typeof data.ok === "boolean") {
    patch.planExecuted = true;
    patch.planFailed = message.planFailed === true || !data.ok;
  }
  return patch;
}

export function isCurrentFormOwner(messages, message) {
  return !!(
    message?.role === "assistant" &&
    message.done &&
    !message.turnStopped &&
    message.messageState !== "stopped" &&
    messages.at(-1)?.id === message.id
  );
}

export function visibleChatForms(message) {
  const timeline = message?.timeline?.length
    ? message.timeline
    : timelineFromLegacy(message?.content, message?.toolCalls);
  const boundary = message?.planExecStart;
  const hasBoundary =
    Number.isInteger(boundary) && boundary >= 0 && boundary <= timeline.length;
  const proposal = hasBoundary ? timeline.slice(0, boundary) : timeline;
  const execution = hasBoundary ? timeline.slice(boundary) : [];
  const forms = [
    ...collectChatForms(proposal).filter(
      (form) =>
        !hasExecutionContext(message) ||
        !!message.formReplies?.[String(form.id)],
    ),
    ...collectChatForms(execution),
  ];
  const seen = new Set();
  return forms.filter((form) => {
    const key = `${form.kind}|${form.adapterKey || ""}|${form.id || ""}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
