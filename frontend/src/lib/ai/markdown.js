import { marked } from "marked";
import DOMPurify from "dompurify";
import { stripDisplayEmoji } from "./display.js";

export function renderAiMarkdown(text) {
  if (!text) return "";
  return DOMPurify.sanitize(
    marked.parse(stripDisplayEmoji(text), { breaks: true, gfm: true }),
  );
}
