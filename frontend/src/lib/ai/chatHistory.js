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
