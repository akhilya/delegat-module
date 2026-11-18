import { hydrateTimeline, timelineFromLegacy } from "./timeline.js";
import { attachmentMetaList } from "./attachments.js";
import { normalizeCopiedPlans } from "./turnIdentity.js";

/** Model-facing control messages — always English (system prompts are English). */
export const AI_CONFIRM_PLAN = "Confirmed, proceed";
export const AI_CANCEL_PLAN =
  "Cancelling the proposed plan - do NOT execute it. The plan was never executed. Confirm the cancellation only.";

export function toolResultFailed(result, summary) {
  if (typeof summary === "string" && /HTTP\s+[45]\d\d/i.test(summary))
    return true;
  if (!result || typeof result !== "object") return false;
  if (Number.isInteger(result.exitCode) && result.exitCode !== 0) return true;
  if (typeof result.status === "string") {
    const s = result.status.toLowerCase();
    return s === "failed" || s === "cancelled";
  }
  if (typeof result.status === "number" && result.status >= 400) return true;
  if (result.error != null && result.error !== "") return true;
  return false;
}

/** Close tools still spinning when the SSE turn ends without a tool-done event. */
export function finalizeTurnTools(message, { stopped = false } = {}) {
  if (!message?.toolCalls) return message;
  for (const tc of message.toolCalls) {
    if (tc.status === "running") {
      tc.status = stopped ? "stopped" : "done";
      if (!tc.summary) tc.summary = "interrupted";
    }
  }
  return message;
}

export function emptyAssistantMessage() {
  return {
    id: Date.now() + 1,
    role: "assistant",
    content: "",
    toolCalls: [],
    timeline: [],
    plan: null,
    apiBlocks: [],
    done: false,
  };
}

function readFormReplies(raw) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return {};
  const next = {};
  for (const [key, value] of Object.entries(raw)) {
    if (!key) continue;
    const answers =
      value && typeof value === "object" && !Array.isArray(value)
        ? value.answers && typeof value.answers === "object"
          ? value.answers
          : value
        : {};
    next[String(key)] = { answers };
  }
  return next;
}

function readAttachments(parsed) {
  if (!Array.isArray(parsed?.attachments)) return [];
  return parsed.attachments
    .filter((a) => a && (a.relativePath || a.fileName))
    .map((a) => {
      const relativePath = String(a.relativePath || a.fileName || "");
      const row = {
        relativePath,
        fileName: String(
          a.fileName || relativePath.split("/").pop() || relativePath,
        ),
        size: Number(a.size) || 0,
      };
      const id = Number(a.id) || 0;
      if (id > 0) row.id = id;
      return row;
    });
}

/** Parse persisted toolCallsJson — tool-call array or UI context / assistant-turn envelope. */
export function parsePersistedMessageMeta(toolCallsJson) {
  if (!toolCallsJson) {
    return {
      toolCalls: [],
      contextCard: null,
      attachments: [],
      timeline: [],
      plan: null,
      requestError: null,
    };
  }
  try {
    const parsed = JSON.parse(toolCallsJson);
    if (parsed && parsed._ui === "formReply") {
      return {
        toolCalls: [],
        contextCard: null,
        attachments: [],
        timeline: [],
        plan: null,
        formReply: {
          formId: parsed.formId == null ? "" : String(parsed.formId),
          answers:
            parsed.answers && typeof parsed.answers === "object"
              ? parsed.answers
              : {},
        },
      };
    }
    if (
      parsed &&
      (parsed._ui === "operationContext" || parsed._ui === "userAttachments")
    ) {
      return {
        toolCalls: [],
        contextCard: parsed.card || null,
        attachments: readAttachments(parsed),
        timeline: [],
        plan: null,
      };
    }
    if (parsed && parsed._ui === "assistantTurn") {
      const toolCalls = (parsed.tools || []).map((tc) => ({
        ...tc,
        status: tc.status || "done",
      }));
      return {
        toolCalls,
        contextCard: null,
        attachments: [],
        timeline: hydrateTimeline(parsed.timeline || [], toolCalls),
        plan: parsed.plan || null,
        turnStopped: !!parsed.turnStopped,
        executionProposalId: parsed.executionProposalId || null,
        requestError: parsed.requestError || null,
        planConfirmed: !!parsed.planConfirmed,
        planCancelled: !!parsed.planCancelled,
        planSuperseded: !!parsed.planSuperseded,
        planExecuted: !!parsed.planExecuted,
        planFailed: !!parsed.planFailed,
        planExecStart: Number.isInteger(parsed.planExecStart)
          ? parsed.planExecStart
          : undefined,
        formReplies: readFormReplies(parsed.formReplies),
        messageId:
          typeof parsed.messageId === "string" && parsed.messageId
            ? parsed.messageId
            : undefined,
        runId:
          typeof parsed.runId === "string" && parsed.runId
            ? parsed.runId
            : undefined,
        phase:
          typeof parsed.phase === "string" && parsed.phase
            ? parsed.phase
            : undefined,
        lastEventSequence: Number.isSafeInteger(parsed.lastEventSequence)
          ? parsed.lastEventSequence
          : undefined,
        planOwnerMessageId:
          typeof parsed.planOwnerMessageId === "string" &&
          parsed.planOwnerMessageId
            ? parsed.planOwnerMessageId
            : undefined,
        messageState:
          typeof parsed.messageState === "string" && parsed.messageState
            ? parsed.messageState
            : undefined,
        executionOutcome:
          parsed.executionOutcome && typeof parsed.executionOutcome === "object"
            ? parsed.executionOutcome
            : undefined,
      };
    }
    if (Array.isArray(parsed)) {
      return {
        toolCalls: parsed.map((tc) => ({ ...tc, status: "done" })),
        contextCard: null,
        attachments: [],
        timeline: [],
        plan: null,
      };
    }
  } catch {
    /* ignore corrupt payload */
  }
  return {
    toolCalls: [],
    contextCard: null,
    attachments: [],
    timeline: [],
    plan: null,
  };
}

export function mapStoredMessage(m) {
  const {
    toolCalls,
    contextCard,
    attachments,
    timeline: storedTimeline,
    plan,
    turnStopped,
    executionProposalId,
    planConfirmed,
    planCancelled,
    planSuperseded,
    planExecuted,
    planFailed,
    planExecStart,
    requestError,
    formReply,
    formReplies,
    messageId,
    runId,
    phase,
    lastEventSequence,
    planOwnerMessageId,
    messageState,
    executionOutcome,
  } = parsePersistedMessageMeta(m.toolCallsJson);
  const content = m.content || "";
  // A structured executionOutcome keeps its summary in content/history only:
  // it must not re-materialize as assistant timeline prose.
  const timeline = storedTimeline?.length
    ? storedTimeline
    : timelineFromLegacy(executionOutcome ? "" : content, toolCalls);
  return {
    id: m.dbId,
    dbId: m.dbId,
    role: m.role,
    content,
    toolCalls,
    timeline,
    contextCard,
    attachments,
    plan: plan || null,
    turnStopped: !!turnStopped,
    executionProposalId,
    planConfirmed: !!planConfirmed,
    planCancelled: !!planCancelled,
    planSuperseded: !!planSuperseded,
    planExecuted: !!planExecuted,
    planFailed: !!planFailed,
    planExecStart,
    requestError: requestError || null,
    formReply: formReply || null,
    formReplies: formReplies || {},
    apiBlocks: [],
    // Canonical rows report their lifecycle; legacy rows keep the old default.
    done:
      messageState !== undefined
        ? messageState !== "running"
        : m.role === "assistant",
    messageId,
    runId,
    phase,
    lastEventSequence,
    planOwnerMessageId,
    messageState,
    executionOutcome,
    feedbackRating: m.feedbackRating ?? null,
  };
}

export function serializeToolCallsJson(m) {
  if (m.formReply) {
    return JSON.stringify({
      _ui: "formReply",
      formId: m.formReply.formId == null ? "" : String(m.formReply.formId),
      answers: m.formReply.answers || {},
    });
  }
  const attachments = attachmentMetaList(m.attachments);
  if (m.contextCard || attachments.length) {
    return JSON.stringify({
      _ui: m.contextCard ? "operationContext" : "userAttachments",
      ...(m.contextCard ? { card: m.contextCard } : {}),
      ...(attachments.length ? { attachments } : {}),
    });
  }
  const tools = (m.toolCalls || []).map((tc) => ({
    id: tc.id,
    name: tc.name,
    input: tc.input,
    summary: tc.summary,
    result: tc.result,
    counts: tc.counts,
    status: tc.status,
  }));
  const formReplies = readFormReplies(m.formReplies);
  const hasFormReplies = Object.keys(formReplies).length > 0;
  if (
    m.turnStopped ||
    m.executionProposalId ||
    m.plan ||
    m.requestError ||
    hasFormReplies ||
    m.messageId ||
    m.executionOutcome ||
    m.timeline?.some((item) => item.type === "tool")
  ) {
    return JSON.stringify({
      _ui: "assistantTurn",
      tools,
      timeline: (m.timeline || []).map((item) =>
        item.type === "tool"
          ? { type: "tool", id: item.id }
          : { type: "text", text: item.text },
      ),
      plan: m.plan || null,
      turnStopped: m.turnStopped || undefined,
      executionProposalId: m.executionProposalId || undefined,
      planConfirmed: m.planConfirmed || undefined,
      planCancelled: m.planCancelled || undefined,
      planSuperseded: m.planSuperseded || undefined,
      planExecuted: m.planExecuted || undefined,
      planFailed: m.planFailed || undefined,
      planExecStart: Number.isInteger(m.planExecStart)
        ? m.planExecStart
        : undefined,
      requestError: m.requestError || undefined,
      ...(hasFormReplies ? { formReplies } : {}),
      messageId: m.messageId || undefined,
      runId: m.runId || undefined,
      phase: m.phase || undefined,
      lastEventSequence: Number.isSafeInteger(m.lastEventSequence)
        ? m.lastEventSequence
        : undefined,
      planOwnerMessageId: m.planOwnerMessageId || undefined,
      messageState: m.messageState || undefined,
      executionOutcome: m.executionOutcome || undefined,
    });
  }
  if (tools.length) {
    return JSON.stringify(
      tools.map(({ name, input, summary }) => ({ name, input, summary })),
    );
  }
  return null;
}

export function isAiControlMessage(msg) {
  const text = typeof msg === "string" ? msg : msg?.content;
  return text === AI_CONFIRM_PLAN || text === AI_CANCEL_PLAN;
}

/** User turns that must reach the model but are not shown as a bubble. */
export function isHiddenUserMessage(msg) {
  if (!msg || msg.role !== "user") return isAiControlMessage(msg);
  return isAiControlMessage(msg) || !!msg.formReply;
}

/** Stamp questionnaire answers from a hidden user turn onto the previous assistant card. */
export function applyFormReplies(messages) {
  const next = messages.map((m) => ({
    ...m,
    formReplies: { ...(m.formReplies || {}) },
  }));
  for (let i = 0; i < next.length; i++) {
    const reply = next[i]?.formReply;
    if (next[i]?.role !== "user" || !reply) continue;
    const formId = String(reply.formId || "");
    if (!formId) continue;
    for (let j = i - 1; j >= 0; j--) {
      if (next[j].role !== "assistant") continue;
      next[j].formReplies[formId] = {
        answers:
          reply.answers && typeof reply.answers === "object"
            ? reply.answers
            : {},
      };
      break;
    }
  }
  return next;
}

function mergePlanExecution(planMsg, execMsg) {
  const proposal = Array.isArray(planMsg.timeline) ? planMsg.timeline : [];
  const execution = Array.isArray(execMsg.timeline) ? execMsg.timeline : [];
  const content = [planMsg.content, execMsg.content]
    .filter(Boolean)
    .join("\n\n");
  return {
    ...planMsg,
    content,
    timeline: [...proposal, ...execution],
    toolCalls: [...(planMsg.toolCalls || []), ...(execMsg.toolCalls || [])],
    planConfirmed: true,
    planExecuted: true,
    planFailed: !!execMsg.planFailed,
    planCancelled: false,
    planExecStart: proposal.length,
    done: true,
  };
}

/** Hide Confirmed/Cancel user lines and fold the follow-up execution into the plan message. */
export function foldPlanTurns(messages) {
  const next = [];
  for (let i = 0; i < messages.length; i++) {
    const msg = messages[i];
    const confirm = messages[i + 1];
    const exec = messages[i + 2];
    if (
      msg?.role === "assistant" &&
      msg.plan &&
      isAiControlMessage(confirm) &&
      confirm.content === AI_CONFIRM_PLAN &&
      exec?.role === "assistant"
    ) {
      next.push(mergePlanExecution(msg, exec));
      i += 2;
      continue;
    }
    if (isAiControlMessage(msg)) {
      if (msg.content === AI_CONFIRM_PLAN) {
        const prev = next.at(-1);
        if (prev?.plan) prev.planConfirmed = true;
      } else if (msg.content === AI_CANCEL_PLAN) {
        const prev = next.at(-1);
        if (prev?.plan) prev.planCancelled = true;
      }
      continue;
    }
    next.push(msg);
  }
  return next;
}

export function hydrateConversation(rows) {
  return applyFormReplies(
    foldPlanTurns(
      applyPlanDecisions(
        normalizeCopiedPlans((rows || []).map(mapStoredMessage)),
      ),
    ),
  );
}

/** After reload, mark plans confirmed/cancelled from the following user line. */
export function applyPlanDecisions(messages) {
  const next = messages.map((m) => ({ ...m }));
  for (let i = 0; i < next.length; i++) {
    const m = next[i];
    if (m.role !== "user") continue;
    if (m.content === AI_CONFIRM_PLAN) {
      for (let j = i - 1; j >= 0; j--) {
        if (next[j].plan) {
          next[j].planConfirmed = true;
          break;
        }
      }
    } else if (m.content === AI_CANCEL_PLAN) {
      for (let j = i - 1; j >= 0; j--) {
        if (next[j].plan) {
          next[j].planCancelled = true;
          break;
        }
      }
    } else if (!m.formReply) {
      for (let j = i - 1; j >= 0; j--) {
        if (!next[j].plan) continue;
        if (
          next[j].planConfirmed ||
          next[j].planCancelled ||
          next[j].planExecuted ||
          next[j].planSuperseded
        )
          break;
        next[j].planSuperseded = true;
        break;
      }
    }
  }
  return next;
}
