/**
 * shadcn-vue CLI currently emits broken Hugeicons imports:
 *   import { FooIcon } from '@hugeicons/vue'
 * Official Vue pattern (Hugeicons docs):
 *   import { HugeiconsIcon } from '@hugeicons/vue'
 *   import { FooIcon } from '@hugeicons/core-free-icons'
 *   <HugeiconsIcon :icon="FooIcon" />
 *
 * Run after `npx shadcn-vue add|apply` until the CLI is fixed.
 */
import fs from "fs";
import path from "path";

const root = "src/components/ui";

function walk(dir) {
  const out = [];
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, ent.name);
    if (ent.isDirectory()) out.push(...walk(p));
    else if (/\.(vue|ts|js)$/.test(p)) out.push(p);
  }
  return out;
}

let changed = 0;
for (const file of walk(root)) {
  let src = fs.readFileSync(file, "utf8");
  if (!src.includes("@hugeicons/vue")) continue;
  if (src.includes("@hugeicons/core-free-icons")) continue;

  const importRe = /import\s*\{([^}]+)\}\s*from\s*['"]@hugeicons\/vue['"];?/;
  const m = src.match(importRe);
  if (!m) continue;

  const icons = m[1]
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
  const iconNames = icons.filter((n) => n !== "HugeiconsIcon");
  if (!iconNames.length) continue;

  const newImport =
    "import { HugeiconsIcon } from '@hugeicons/vue'\n" +
    `import { ${iconNames.join(", ")} } from '@hugeicons/core-free-icons'`;
  src = src.replace(importRe, newImport);

  for (const icon of iconNames) {
    const selfClosing = new RegExp(`<${icon}(\\s[^>]*)?\\s*/>`, "g");
    src = src.replace(selfClosing, (_full, attrs = "") => {
      return `<HugeiconsIcon :icon="${icon}"${attrs || ""} />`;
    });
  }

  fs.writeFileSync(file, src);
  changed++;
  console.log("fixed", file);
}

console.log("changed", changed);
