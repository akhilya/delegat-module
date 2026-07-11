#!/usr/bin/env node
/**
 * UI audit: counts raw HTML vs shadcn usage. Excludes graph editor by default.
 * Run: node scripts/ui-audit.mjs
 */
import { readdir, readFile } from "node:fs/promises";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const SRC = fileURLToPath(new URL("../src", import.meta.url));
const GRAPH_PREFIX = "components/marketplace/graph/";
const UI_PREFIX = "components/ui/";

const PATTERNS = [
  { id: "raw_button", regex: /<button\b/g },
  { id: "shadcn_button_import", regex: /@\/components\/ui\/button/g },
  { id: "direct_button_vue", regex: /button\/Button\.vue/g },
  { id: "shadcn_card", regex: /<Card\b|@\/components\/ui\/card/g },
  { id: "card_like_div", regex: /rounded-lg border.*bg-(card|background)/g },
  { id: "raw_input", regex: /<input\b/g },
  { id: "shadcn_input", regex: /<Input\b|@\/components\/ui\/input/g },
];

async function walk(dir, files = []) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) await walk(path, files);
    else if (/\.(vue|js)$/.test(entry.name)) files.push(path);
  }
  return files;
}

function rel(p) {
  return relative(SRC, p).replace(/\\/g, "/");
}

const files = await walk(SRC);
const totals = Object.fromEntries(PATTERNS.map((p) => [p.id, 0]));
const byFile = {};
const excludeGraph = process.argv.includes("--all") ? false : true;

for (const file of files) {
  const r = rel(file);
  if (excludeGraph && r.startsWith(GRAPH_PREFIX)) continue;
  if (r.startsWith(UI_PREFIX)) continue;

  const content = await readFile(file, "utf8");
  let fileHits = 0;
  for (const { id, regex } of PATTERNS) {
    const matches = content.match(regex);
    if (matches?.length) {
      totals[id] += matches.length;
      fileHits += matches.length;
      if (id === "raw_button") {
        byFile[r] = (byFile[r] || 0) + matches.length;
      }
    }
  }
}

console.log("UI Audit", excludeGraph ? "(excluding graph/ and ui/)" : "(all src)");
console.log("--- totals ---");
for (const [id, n] of Object.entries(totals)) console.log(`  ${id}: ${n}`);

console.log("--- raw <button> by file ---");
Object.entries(byFile)
  .sort((a, b) => b[1] - a[1])
  .forEach(([f, n]) => console.log(`  ${n}\t${f}`));