/**
 * Token-level diff for import preview (Catalog Workbench).
 * Short strings → character tokens (catches False vs false).
 * Longer text → word tokens (readable description diffs).
 */

/**
 * @typedef {{ type: 'equal' | 'remove' | 'add', text: string }} DiffPart
 */

const CHAR_MODE_MAX = 64;

/**
 * @param {string} a
 * @param {string} b
 * @returns {'char' | 'word'}
 */
function pickMode(a, b) {
  if (a.length <= CHAR_MODE_MAX && b.length <= CHAR_MODE_MAX) return "char";
  return "word";
}

/**
 * @param {string} s
 * @param {'char' | 'word'} mode
 */
function tokenize(s, mode) {
  if (mode === "char") return Array.from(s);
  return s.split(/(\s+)/).filter((t) => t.length > 0);
}

/**
 * @param {string[]} a
 * @param {string[]} b
 * @returns {DiffPart[]}
 */
function diffTokens(a, b) {
  const n = a.length;
  const m = b.length;
  if (n === 0 && m === 0) return [];
  if (n === 0) return [{ type: "add", text: b.join("") }];
  if (m === 0) return [{ type: "remove", text: a.join("") }];

  /** @type {Uint16Array[]} */
  const dp = Array.from({ length: n + 1 }, () => new Uint16Array(m + 1));
  for (let i = 1; i <= n; i++) {
    for (let j = 1; j <= m; j++) {
      dp[i][j] =
        a[i - 1] === b[j - 1]
          ? dp[i - 1][j - 1] + 1
          : Math.max(dp[i - 1][j], dp[i][j - 1]);
    }
  }

  /** @type {DiffPart[]} */
  const raw = [];
  let i = n;
  let j = m;
  while (i > 0 || j > 0) {
    if (i > 0 && j > 0 && a[i - 1] === b[j - 1]) {
      raw.push({ type: "equal", text: a[i - 1] });
      i--;
      j--;
    } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
      raw.push({ type: "add", text: b[j - 1] });
      j--;
    } else {
      raw.push({ type: "remove", text: a[i - 1] });
      i--;
    }
  }
  raw.reverse();

  /** @type {DiffPart[]} */
  const merged = [];
  for (const part of raw) {
    const last = merged[merged.length - 1];
    if (last && last.type === part.type) last.text += part.text;
    else merged.push({ ...part });
  }
  return merged;
}

/**
 * @param {unknown} value
 * @param {string} emptyLabel
 */
export function displayFieldValue(value, emptyLabel) {
  if (value === null || value === undefined || value === "") return emptyLabel;
  if (Array.isArray(value)) return formatList(value);
  if (typeof value === "string") {
    const pretty = prettyJsonArray(value);
    if (pretty != null) return pretty;
  }
  return String(value);
}

function formatList(items) {
  return items.map((item) => String(item)).join(", ");
}

/** Unwrap a JSON array (and one extra nested array-as-string) into a comma-separated list. */
function prettyJsonArray(s) {
  const t = s.trim();
  if (!t.startsWith("[")) return null;
  try {
    const parsed = JSON.parse(t);
    if (!Array.isArray(parsed)) return null;
    if (
      parsed.length === 1 &&
      typeof parsed[0] === "string" &&
      parsed[0].trim().startsWith("[")
    ) {
      const inner = prettyJsonArray(parsed[0]);
      if (inner != null) return inner;
    }
    return formatList(parsed);
  } catch {
    return null;
  }
}

/**
 * @param {string} a
 * @param {string} b
 */
export function isWhitespaceOnlyDiff(a, b) {
  if (a === b) return false;
  const strip = (s) => s.replace(/[ \t\n\r]+/g, "");
  return strip(a) === strip(b);
}

/**
 * Split text into glyphs; whitespace becomes visible markers for changed spans.
 * @param {string} text
 * @param {boolean} visualizeWhitespace
 * @returns {{ kind: 'text' | 'ws', display: string, raw: string }[]}
 */
export function splitForDisplay(text, visualizeWhitespace) {
  if (!visualizeWhitespace) {
    return [{ kind: "text", display: text, raw: text }];
  }

  /** @type {{ kind: 'text' | 'ws', display: string, raw: string }[]} */
  const out = [];
  let buf = "";
  const flushText = () => {
    if (!buf) return;
    out.push({ kind: "text", display: buf, raw: buf });
    buf = "";
  };

  for (const ch of text) {
    if (ch === "\n") {
      flushText();
      out.push({ kind: "ws", display: "↵", raw: "\\n" });
    } else if (ch === "\r") {
      flushText();
      out.push({ kind: "ws", display: "␍", raw: "\\r" });
    } else if (ch === "\t") {
      flushText();
      out.push({ kind: "ws", display: "→", raw: "\\t" });
    } else if (ch === " ") {
      flushText();
      out.push({ kind: "ws", display: "·", raw: "space" });
    } else {
      buf += ch;
    }
  }
  flushText();
  return out;
}

/**
 * @param {string} oldText
 * @param {string} newText
 * @returns {{ oldParts: DiffPart[], newParts: DiffPart[], whitespaceOnly: boolean }}
 */
export function buildFieldDiff(oldText, newText) {
  if (oldText === newText) {
    return {
      oldParts: [{ type: "equal", text: oldText }],
      newParts: [{ type: "equal", text: newText }],
      whitespaceOnly: false,
    };
  }

  const mode = pickMode(oldText, newText);
  const parts = diffTokens(tokenize(oldText, mode), tokenize(newText, mode));

  return {
    oldParts: parts.filter((p) => p.type !== "add"),
    newParts: parts.filter((p) => p.type !== "remove"),
    whitespaceOnly: isWhitespaceOnlyDiff(oldText, newText),
  };
}
