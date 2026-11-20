import {
  generatedFileDownloads,
  generatedTemporaryDownloads,
} from "./generatedFiles.js";
import { timelineFromLegacy } from "./timeline.js";

/** Append assistant protocol blocks without crossing a tool-result boundary. */
export function appendAssistantApiBlock(apiBlocks, block) {
  let assistantBlock = apiBlocks.at(-1);
  if (assistantBlock?.role !== "assistant") {
    assistantBlock = { role: "assistant", content: [] };
    apiBlocks.push(assistantBlock);
  }

  const previous = assistantBlock.content.at(-1);
  if (block.type === "text" && previous?.type === "text") {
    previous.text += block.text;
  } else {
    assistantBlock.content.push(block);
  }
}

/** Convert UI API blocks into provider messages while preserving tool round order. */
export function apiHistoryFromBlocks(apiBlocks) {
  const history = [];
  let toolResults = [];

  const flushToolResults = () => {
    if (!toolResults.length) return;
    history.push({ role: "user", content: toolResults });
    toolResults = [];
  };

  for (const block of apiBlocks || []) {
    if (block?.role === "assistant") {
      flushToolResults();
      if (block.content?.length) {
        history.push({ role: "assistant", content: block.content });
      }
      continue;
    }

    if (block?.role === "tool_result" || block?.type === "tool_result") {
      toolResults.push({
        type: "tool_result",
        tool_use_id: block.tool_use_id,
        content: block.content ?? "",
      });
    }
  }

  flushToolResults();
  return history;
}

export function generatedFileHistory(message) {
  if (
    !message?.planConfirmed ||
    !message.planExecuted ||
    message.planFailed ||
    message.turnStopped
  )
    return null;
  const timeline = message.timeline?.length
    ? message.timeline
    : timelineFromLegacy(message.content, message.toolCalls);
  const start = message.planExecStart;
  if (!Number.isInteger(start) || start < 0 || start > timeline.length)
    return null;
  const tools = timeline
    .slice(start)
    .filter((item) => item.type === "tool" && item.tool)
    .map((item) => item.tool);
  const artifacts = generatedTemporaryDownloads(message.plan, tools, {
    confirmed: true,
  });
  const savedFiles = generatedFileDownloads(message.plan, tools, {
    confirmed: true,
  });
  return artifacts.length || savedFiles.length
    ? { artifacts, savedFiles }
    : null;
}
