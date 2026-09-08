/**
 * Cabinet chat display helpers: Apple inset process grouping, Python run
 * parsing, and stripping emoji so Emoji: Don't use is visible in the UI.
 */

const EMOJI_SEQUENCE =
  /\p{Extended_Pictographic}(?:\uFE0F|\u200D\p{Extended_Pictographic})*/gu;

export function stripDisplayEmoji(text) {
  if (!text) return "";
  return String(text)
    .replace(EMOJI_SEQUENCE, "")
    .replace(/[\uFE0F\u200D]/g, "")
    .replace(/[ \t]{2,}/g, " ")
    .replace(/[ \t]+\n/g, "\n")
    .replace(/[ \t]+$/gm, "")
    .replace(/\n{3,}/g, "\n\n");
}

export function parseJsonObject(raw) {
  if (!raw || typeof raw !== "string") return null;
  try {
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? parsed
      : null;
  } catch {
    return null;
  }
}

export function pythonRunParts(tool) {
  const input = tool?.input || "";
  const parsed = parseJsonObject(input);
  const code =
    typeof parsed?.code === "string"
      ? parsed.code
      : parsed
        ? ""
        : String(input);
  const result =
    tool?.result && typeof tool.result === "object" ? tool.result : {};
  return {
    code: code.trim(),
    stdout: String(result.stdout || ""),
    stderr: String(result.stderr || ""),
    error: String(result.error || ""),
    exitCode: result.exitCode,
  };
}

export function sqlResultParts(tool) {
  const result =
    tool?.result && typeof tool.result === "object" ? tool.result : {};
  const rows = Array.isArray(result.rows) ? result.rows : [];
  const columns =
    Array.isArray(result.columns) && result.columns.length
      ? result.columns
      : rows[0]
        ? Object.keys(rows[0])
        : [];
  return { columns, rows: rows.slice(0, 30) };
}

/**
 * Consecutive catalog tools share one compact list. Python is its own
 * block. Thinking stays as caption text between groups.
 */
export function groupProcessItems(process) {
  const groups = [];
  for (const item of process || []) {
    if (item?.type === "text") {
      const text = item.text?.trim();
      if (!text) continue;
      groups.push({ type: "text", text: item.text });
      continue;
    }
    const tool = item?.tool;
    if (!tool) continue;
    if (tool.name === "run_python") {
      groups.push({ type: "python", tool });
      continue;
    }
    const last = groups[groups.length - 1];
    if (last?.type === "tools") last.tools.push(tool);
    else groups.push({ type: "tools", tools: [tool] });
  }
  return groups;
}

export function processTools(process) {
  return (process || [])
    .map((item) => item?.tool)
    .filter(Boolean);
}

export function runningProcessTool(process) {
  return processTools(process).find((tool) => tool.status === "running") || null;
}
