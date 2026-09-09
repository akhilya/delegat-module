import { askFormFromTool, chatActionsFromTool } from "./replyUi.js";

export { chatActionsFromTool };

/**
 * Build and split the assistant execution timeline so thinking, tools, and
 * the final reply stay in chronological order instead of "all tools on top".
 */

export function appendTextDelta(timeline, delta) {
  const next = Array.isArray(timeline) ? [...timeline] : [];
  if (!delta) return next;
  const last = next[next.length - 1];
  if (last?.type === "text") {
    next[next.length - 1] = { type: "text", text: last.text + delta };
  } else {
    next.push({ type: "text", text: delta });
  }
  return next;
}

export function appendTool(timeline, tool) {
  const next = Array.isArray(timeline) ? [...timeline] : [];
  if (!tool) return next;
  next.push({ type: "tool", id: tool.id || tool.name, tool });
  return next;
}

export function timelineFromLegacy(content, toolCalls) {
  const timeline = [];
  for (const tc of toolCalls || []) {
    timeline.push({ type: "tool", id: tc.id || tc.name, tool: tc });
  }
  if (content) timeline.push({ type: "text", text: content });
  return timeline;
}

function joinText(items) {
  return items
    .filter((item) => item.type === "text" && item.text)
    .map((item) => item.text)
    .join("\n\n")
    .trim();
}

export function isReplyChromeTool(item) {
  const name = item?.tool?.name;
  return name === "navigate_to" || name === "ask_form";
}

export function isNavigateTool(item) {
  return item?.type === "tool" && item.tool?.name === "navigate_to";
}

export function collectChatActions(timeline) {
  const actions = [];
  const seen = new Set();
  for (const item of timeline || []) {
    if (!isNavigateTool(item)) continue;
    for (const action of chatActionsFromTool(item.tool)) {
      const key = `${action.url}|${action.prompt}|${action.label}`;
      if (seen.has(key)) continue;
      seen.add(key);
      actions.push(action);
    }
  }
  return actions;
}

export function collectChatForms(timeline) {
  const forms = [];
  for (const item of timeline || []) {
    if (item?.type !== "tool" || item.tool?.name !== "ask_form") continue;
    const form = askFormFromTool(item.tool);
    if (form) forms.push({ ...form, id: item.id || item.tool?.id || forms.length });
  }
  return forms.slice(0, 2);
}

/**
 * @param {Array} timeline
 * @param {{ done?: boolean }} [opts]
 */
export function executionView(timeline, opts = {}) {
  const items = Array.isArray(timeline) ? timeline : [];
  const done = opts.done === true;
  const actions = collectChatActions(items);
  const forms = collectChatForms(items);
  if (!items.length) return { process: [], reply: "", actions, forms };

  let lastToolIdx = -1;
  for (let i = items.length - 1; i >= 0; i--) {
    if (items[i].type === "tool") {
      lastToolIdx = i;
      break;
    }
  }

  if (lastToolIdx < 0) {
    if (!done) return { process: items, reply: "", actions, forms };
    return { process: [], reply: joinText(items), actions, forms };
  }

  return {
    process: items.slice(0, lastToolIdx + 1).filter((item) => !isReplyChromeTool(item)),
    reply: joinText(items.slice(lastToolIdx + 1)),
    actions,
    forms,
  };
}

const emptyView = { process: [], reply: "", actions: [], forms: [] };

/**
 * Keep the proposal (thinking / reply / plan) separate from tools that run after Execute.
 * @param {Array} timeline
 * @param {number | null | undefined} planExecStart
 * @param {{ done?: boolean }} [opts]
 */
export function splitPlanExecution(timeline, planExecStart, opts = {}) {
  const items = Array.isArray(timeline) ? timeline : [];
  const start = Number(planExecStart);
  if (!Number.isInteger(start) || start < 0 || start > items.length) {
    return {
      proposal: executionView(items, opts),
      execution: { ...emptyView },
    };
  }
  return {
    proposal: executionView(items.slice(0, start), { done: true }),
    execution: executionView(items.slice(start), opts),
  };
}

export function hydrateTimeline(timeline, toolCalls) {
  const tools = toolCalls || [];
  return (timeline || []).map((item) => {
    if (item.type !== "tool") return item;
    const tool =
      tools.find((tc) => (tc.id || tc.name) === item.id) || item.tool;
    return { ...item, tool };
  });
}
