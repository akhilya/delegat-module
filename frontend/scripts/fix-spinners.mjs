/**
 * Fix migrate-spinners artifacts: broken tags + missing imports.
 * Run: node scripts/fix-spinners.mjs
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const srcRoot = path.resolve(__dirname, "../src");
const spinnerImport = 'import { Spinner } from "@/components/ui/spinner";\n';

function walk(dir, out = []) {
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, ent.name);
    if (ent.isDirectory()) walk(p, out);
    else if (p.endsWith(".vue") || p.endsWith(".js")) out.push(p);
  }
  return out;
}

let fixedTags = 0;
let fixedImports = 0;

for (const file of walk(srcRoot)) {
  let code = fs.readFileSync(file, "utf8");
  let next = code;

  const before = next;
  next = next.replace(/<Spinner\s+\/\s+/g, () => {
    fixedTags++;
    return "<Spinner ";
  });
  next = next.replace(/<Spinner\s+\/\s*\/>/g, () => {
    fixedTags++;
    return "<Spinner />";
  });

  const usesSpinner =
    /<Spinner\b/.test(next) ||
    /\bcomponents\s*:\s*\{[^}]*\bSpinner\b/s.test(next);
  const hasImport = next.includes("@/components/ui/spinner");

  if (usesSpinner && !hasImport) {
    if (/^<script\b/m.test(next)) {
      next = next.replace(/^(<script\b[^>]*>\s*\n)/m, `$1${spinnerImport}`);
      fixedImports++;
    }
  }

  if (next !== before || (usesSpinner && !hasImport && next !== code)) {
    if (next !== code) {
      fs.writeFileSync(file, next);
      console.log("fixed", path.relative(srcRoot, file));
    }
  }
}

// OperationItem uses Loader2 as a dynamic status icon component
{
  const file = path.join(srcRoot, "components/operations/OperationItem.vue");
  let code = fs.readFileSync(file, "utf8");
  let next = code;
  if (/\breturn Loader2\b/.test(next) && !/^\s*Loader2,?$/m.test(next.split("lucide-vue-next")[0])) {
    next = next.replace(
      /import \{([\s\S]*?)\} from "lucide-vue-next";/,
      (m, body) => {
        if (/\bLoader2\b/.test(body)) return m;
        return `import {${body.trimEnd()},\n  Loader2,\n} from "lucide-vue-next";`;
      },
    );
  }
  if (/<Spinner\b/.test(next) && !next.includes("@/components/ui/spinner")) {
    next = next.replace(/^(<script setup>\s*\n)/m, `$1${spinnerImport}`);
  }
  if (next !== code) {
    fs.writeFileSync(file, next);
    console.log("fixed OperationItem.vue");
  }
}

console.log({ fixedTags, fixedImports });
