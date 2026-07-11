#!/usr/bin/env node
/**
 * Replace direct Button.vue imports with barrel import.
 * Run: node scripts/codemod-button-imports.mjs
 */
import { readdir, readFile, writeFile } from "node:fs/promises";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const SRC = fileURLToPath(new URL("../src", import.meta.url));

const IMPORT_PATTERNS = [
  /import Button from ["']@\/components\/ui\/button\/Button\.vue["'];?\r?\n/g,
  /import Button from ["']\.\/ui\/button\/Button\.vue["'];?\r?\n/g,
  /import Button from ["']\.\.\/ui\/button\/Button\.vue["'];?\r?\n/g,
];

const REPLACEMENT = 'import { Button } from "@/components/ui/button";\n';

async function walk(dir, files = []) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) await walk(path, files);
    else if (/\.(vue|js)$/.test(entry.name)) files.push(path);
  }
  return files;
}

let changed = 0;
for (const file of await walk(SRC)) {
  let content = await readFile(file, "utf8");
  let updated = content;
  for (const pattern of IMPORT_PATTERNS) {
    updated = updated.replace(pattern, REPLACEMENT);
  }
  if (updated !== content) {
    await writeFile(file, updated, "utf8");
    changed++;
    console.log(relative(SRC, file));
  }
}
console.log(`Updated ${changed} files.`);