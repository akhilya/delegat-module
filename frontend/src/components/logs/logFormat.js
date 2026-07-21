import { formatDateTimeCombined } from "@/utils/dateTime";

/**
 * Format a log timestamp in the store timezone.
 * @param {*} value server UTC timestamp
 * @param {string} [timezone] IANA timezone from store settings
 */
export function formatLogDate(value, timezone = "UTC") {
  return formatDateTimeCombined(value, timezone || "UTC", {
    withSeconds: true,
  });
}

/** Badge variant for HTTP status codes. */
export function httpStatusVariant(code) {
  const n = Number(code);
  if (n >= 200 && n < 300) return "secondary";
  if (n >= 400) return "destructive";
  if (n === 0) return "outline";
  return "outline";
}

const TRUNC_RE = /\n?…\[truncated\]\s*$/;

/**
 * Normalize raw body/headers for JSON formatting.
 * Handles objects, BOM, double-encoded JSON strings, and server truncation markers.
 */
export function prepareJsonInput(raw) {
  if (raw == null || raw === "") {
    return { text: "", truncated: false };
  }

  if (typeof raw === "object") {
    try {
      return { text: JSON.stringify(raw), truncated: false };
    } catch {
      return { text: String(raw), truncated: false };
    }
  }

  let s = String(raw).replace(/^\uFEFF/, "").trim();
  let truncated = false;
  if (TRUNC_RE.test(s)) {
    truncated = true;
    s = s.replace(TRUNC_RE, "").trimEnd();
  }

  // Unwrap "\"{...}\"" / repeatedly stringified JSON payloads.
  for (let i = 0; i < 3; i++) {
    try {
      const parsed = JSON.parse(s);
      if (typeof parsed === "string") {
        const inner = parsed.trim();
        if (
          (inner.startsWith("{") && inner.endsWith("}")) ||
          (inner.startsWith("[") && inner.endsWith("]"))
        ) {
          s = inner;
          continue;
        }
      }
      return { text: JSON.stringify(parsed), truncated };
    } catch {
      break;
    }
  }

  return { text: s, truncated };
}

/** Pretty-print JSON when possible; otherwise return the cleaned raw string. */
export function prettyJson(raw) {
  const { text, truncated } = prepareJsonInput(raw);
  if (!text) return "";

  let pretty = text;
  try {
    pretty = JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    // keep cleaned raw (may still be minified / truncated mid-token)
  }

  return truncated ? `${pretty}\n…[truncated]` : pretty;
}

function escapeHtml(s) {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

/**
 * Format + syntax-highlight JSON for safe v-html display.
 * Still tokenizes when the payload is truncated / not fully valid JSON.
 */
export function highlightJsonHtml(raw) {
  const pretty = prettyJson(raw);
  if (!pretty) return "";

  const src = pretty;
  let out = "";
  let i = 0;
  while (i < src.length) {
    const ch = src[i];

    if (ch === "…" && src.startsWith("…[truncated]", i)) {
      out += `<span class="log-json__trunc">${escapeHtml("…[truncated]")}</span>`;
      i += "…[truncated]".length;
      continue;
    }

    if (ch === '"') {
      let j = i + 1;
      let esc = false;
      while (j < src.length) {
        if (esc) {
          esc = false;
          j++;
          continue;
        }
        if (src[j] === "\\") {
          esc = true;
          j++;
          continue;
        }
        if (src[j] === '"') break;
        j++;
      }
      const lit = src.slice(i, Math.min(j + 1, src.length));
      i = j + 1;
      let k = i;
      while (k < src.length && /\s/.test(src[k])) k++;
      const isKey = src[k] === ":";
      out += `<span class="log-json__${isKey ? "key" : "string"}">${escapeHtml(lit)}</span>`;
      continue;
    }
    if (/[-0-9]/.test(ch)) {
      let j = i + 1;
      while (j < src.length && /[0-9.eE+-]/.test(src[j])) j++;
      out += `<span class="log-json__number">${escapeHtml(src.slice(i, j))}</span>`;
      i = j;
      continue;
    }
    if (/[tfn]/.test(ch)) {
      let matched = false;
      for (const word of ["true", "false", "null"]) {
        if (src.startsWith(word, i)) {
          out += `<span class="log-json__literal">${word}</span>`;
          i += word.length;
          matched = true;
          break;
        }
      }
      if (!matched) {
        out += escapeHtml(ch);
        i++;
      }
      continue;
    }
    if ("{}[],:".includes(ch)) {
      out += `<span class="log-json__punct">${ch}</span>`;
      i++;
      continue;
    }
    out += escapeHtml(ch);
    i++;
  }
  return out;
}
