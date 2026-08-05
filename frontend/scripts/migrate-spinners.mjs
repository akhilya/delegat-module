/**
 * Migrate Loader2 → Spinner with formal size prop across frontend/src.
 * Run: node scripts/migrate-spinners.mjs
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const srcRoot = path.resolve(__dirname, "../src");

const SIZE_MAP = {
  "size-3": "xs",
  "size-3.5": "sm",
  "size-4": "default",
  "size-5": "lg",
  "size-6": "xl",
  "size-8": "2xl",
};

function walk(dir, out = []) {
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, ent.name);
    if (ent.isDirectory()) walk(p, out);
    else if (ent.name.endsWith(".vue") || ent.name.endsWith(".js")) out.push(p);
  }
  return out;
}

function stripSizeAndSpin(classStr) {
  const tokens = classStr
    .split(/\s+/)
    .filter(Boolean)
    .filter((t) => t !== "animate-spin" && !(t in SIZE_MAP));
  return tokens.join(" ");
}

function detectSize(classStr) {
  const tokens = classStr.split(/\s+/).filter(Boolean);
  for (const t of tokens) {
    if (t in SIZE_MAP) return SIZE_MAP[t];
  }
  return "default";
}

function transformTag(tag) {
  // <Loader2 ... /> or <Loader2 ...></Loader2>
  const selfClosing = /\/\s*>$/.test(tag);
  const m = tag.match(/^<Loader2\b([^>]*)\/?\s*>$/);
  if (!m) return null;
  let attrs = m[1].replace(/\/\s*$/, "").trim();

  let size = "default";
  let restClass = "";

  const classMatch = attrs.match(/\bclass\s*=\s*(["'])(.*?)\1/);
  if (classMatch) {
    const raw = classMatch[2];
    size = detectSize(raw);
    restClass = stripSizeAndSpin(raw);
    attrs = attrs.replace(classMatch[0], "").trim();
  }

  const parts = ["<Spinner"];
  // Preserve other attrs (v-if, etc.)
  if (attrs) parts.push(" " + attrs.replace(/\s+/g, " ").trim());
  if (size !== "default") parts.push(` size="${size}"`);
  if (restClass) parts.push(` class="${restClass}"`);
  parts.push(selfClosing || true ? " />" : "></Spinner>");
  // Always self-close Spinner
  return parts.join("").replace(/\s+\/>$/, " />");
}

function ensureSpinnerImport(code) {
  if (code.includes("@/components/ui/spinner")) return code;

  // Prefer adding next to other ui imports
  const spinnerImport = `import { Spinner } from "@/components/ui/spinner";\n`;

  if (/from ["']@\/components\/ui\//.test(code)) {
    return code.replace(
      /(import .+ from ["']@\/components\/ui\/[^"']+["'];?\n)/,
      `$1${spinnerImport}`,
    );
  }
  if (/^<script\b[^>]*>/m.test(code)) {
    return code.replace(/^<script\b[^>]*>\s*\n/m, (m) => m + spinnerImport);
  }
  return spinnerImport + code;
}

function removeLoader2FromLucide(code) {
  // named import block from lucide-vue-next
  return code.replace(
    /import\s*\{([^}]*)\}\s*from\s*["']lucide-vue-next["'];?/g,
    (full, body) => {
      const names = body
        .split(",")
        .map((s) => s.trim())
        .filter(Boolean)
        .filter((n) => n !== "Loader2" && !n.startsWith("Loader2 "));
      if (names.length === 0) return "";
      return `import {\n  ${names.join(",\n  ")},\n} from "lucide-vue-next";`;
    },
  );
}

function removeLoader2FromComponents(code) {
  // Options API components: { ..., Loader2, ... }
  return code.replace(
    /(components\s*:\s*\{)([^}]*)(\})/gs,
    (full, a, body, c) => {
      if (!/\bLoader2\b/.test(body)) return full;
      const cleaned = body
        .split(",")
        .map((s) => s.trim())
        .filter((s) => s && s !== "Loader2")
        .join(",\n    ");
      return `${a}\n    ${cleaned}\n  ${c}`;
    },
  );
}

function addSpinnerToComponents(code) {
  if (!/\bcomponents\s*:\s*\{/.test(code)) return code;
  if (/\bSpinner\b/.test(code.match(/components\s*:\s*\{[^}]*/)?.[0] || ""))
    return code;
  return code.replace(/(components\s*:\s*\{)/, "$1\n    Spinner,");
}

let changedFiles = 0;
let replacements = 0;

for (const file of walk(srcRoot)) {
  if (file.includes(`${path.sep}ui${path.sep}spinner${path.sep}`)) continue;
  let code = fs.readFileSync(file, "utf8");
  if (!code.includes("Loader2") && !/<Spinner\b/.test(code)) continue;

  let next = code;
  let fileRepls = 0;

  // Replace Loader2 tags
  next = next.replace(/<Loader2\b[^>]*\/?\s*>/g, (tag) => {
    const out = transformTag(tag.endsWith("/>") || tag.endsWith("/ >") ? tag : tag.replace(/>$/, " />"));
    if (!out) return tag;
    fileRepls++;
    return out;
  });

  // Also migrate existing Spinner with size-* classes to size prop
  next = next.replace(/<Spinner\b([^>]*)\/?\s*>/g, (tag, attrsRaw) => {
    const classMatch = attrsRaw.match(/\bclass\s*=\s*(["'])(.*?)\1/);
    if (!classMatch) return tag;
    const raw = classMatch[2];
    if (!Object.keys(SIZE_MAP).some((k) => raw.split(/\s+/).includes(k))) {
      // Still strip redundant animate-spin
      if (!/\banimate-spin\b/.test(raw)) return tag;
      const rest = stripSizeAndSpin(raw);
      const attrs = attrsRaw.replace(classMatch[0], rest ? `class="${rest}"` : "").trim();
      fileRepls++;
      return `<Spinner ${attrs} />`.replace(/\s+/g, " ").replace(" >", ">");
    }
    if (/\bsize=/.test(attrsRaw)) {
      // already has size prop — just clean class
      const rest = stripSizeAndSpin(raw);
      let attrs = attrsRaw.replace(classMatch[0], rest ? `class="${rest}"` : "").trim();
      fileRepls++;
      return `<Spinner ${attrs} />`.replace(/\s{2,}/g, " ");
    }
    const size = detectSize(raw);
    const rest = stripSizeAndSpin(raw);
    let attrs = attrsRaw.replace(classMatch[0], "").trim();
    const parts = ["<Spinner"];
    if (attrs) parts.push(" " + attrs);
    if (size !== "default") parts.push(` size="${size}"`);
    if (rest) parts.push(` class="${rest}"`);
    parts.push(" />");
    fileRepls++;
    return parts.join("");
  });

  if (fileRepls === 0 && !code.includes("Loader2")) continue;

  if (next.includes("<Spinner")) {
    next = ensureSpinnerImport(next);
    next = removeLoader2FromLucide(next);
    next = removeLoader2FromComponents(next);
    if (/\bcomponents\s*:\s*\{/.test(next)) {
      next = addSpinnerToComponents(next);
    }
  } else {
    next = removeLoader2FromLucide(next);
    next = removeLoader2FromComponents(next);
  }

  // Clean double blank lines from removed imports
  next = next.replace(/\n{3,}/g, "\n\n");

  if (next !== code) {
    fs.writeFileSync(file, next);
    changedFiles++;
    replacements += fileRepls;
    console.log(`updated ${path.relative(srcRoot, file)} (${fileRepls})`);
  }
}

console.log(`\nDone: ${changedFiles} files, ${replacements} tag replacements`);
