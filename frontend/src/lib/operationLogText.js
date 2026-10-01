/**
 * Split an operation log line into plain text, pretty JSON, and product links.
 * Product mentions use `product #id` and an optional `«name»`.
 */

const PRODUCT_RE = /product #(\d+)(?: «([^»]*)»)?/gi;

export function splitOperationLog(message) {
  const source = message == null ? "" : String(message);
  const parts = [];
  for (const chunk of splitJson(source)) {
    if (chunk.type === "json") {
      parts.push(chunk);
      continue;
    }
    parts.push(...splitProducts(chunk.text));
  }
  return parts.filter((part) => part.type !== "text" || part.text.length > 0);
}

function splitProducts(text) {
  const parts = [];
  let last = 0;
  PRODUCT_RE.lastIndex = 0;
  let match;
  while ((match = PRODUCT_RE.exec(text))) {
    if (match.index > last) {
      parts.push({ type: "text", text: text.slice(last, match.index) });
    }
    const name = match[2]?.trim();
    parts.push({
      type: "product",
      productId: Number(match[1]),
      text: name || `#${match[1]}`,
    });
    last = match.index + match[0].length;
  }
  if (last < text.length) parts.push({ type: "text", text: text.slice(last) });
  if (parts.length === 0) parts.push({ type: "text", text });
  return parts;
}

function splitJson(source) {
  const parts = [];
  let i = 0;
  while (i < source.length) {
    const start = findJsonStart(source, i);
    if (start < 0) {
      parts.push({ type: "text", text: source.slice(i) });
      break;
    }
    const end = matchJsonEnd(source, start);
    if (end < 0) {
      parts.push({ type: "text", text: source.slice(i) });
      break;
    }
    const raw = source.slice(start, end);
    let pretty = null;
    try {
      const value = JSON.parse(raw);
      if (value !== null && typeof value === "object") {
        pretty = JSON.stringify(value, null, 2);
      }
    } catch {
      pretty = null;
    }
    if (pretty == null) {
      parts.push({ type: "text", text: source.slice(i, start + 1) });
      i = start + 1;
      continue;
    }
    if (start > i) parts.push({ type: "text", text: source.slice(i, start) });
    parts.push({ type: "json", text: pretty });
    i = end;
  }
  return parts;
}

function findJsonStart(source, from) {
  for (let i = from; i < source.length; i++) {
    const ch = source[i];
    if (ch === "{" || ch === "[") return i;
  }
  return -1;
}

function matchJsonEnd(source, start) {
  const open = source[start];
  const close = open === "{" ? "}" : "]";
  let depth = 0;
  let inString = false;
  let escaped = false;
  for (let i = start; i < source.length; i++) {
    const ch = source[i];
    if (inString) {
      if (escaped) {
        escaped = false;
        continue;
      }
      if (ch === "\\") {
        escaped = true;
        continue;
      }
      if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"') {
      inString = true;
      continue;
    }
    if (ch === open) depth++;
    else if (ch === close) {
      depth--;
      if (depth === 0) return i + 1;
    }
  }
  return -1;
}
