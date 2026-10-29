import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const errors = [];
const requiredFiles = [
  "README.md",
  "MEMORY.md",
  "CHANGELOG.md",
  "VERSION",
  "docs/README.md",
  "docs/plans/README.md",
  "docs/platform/releasing.md",
  ".github/copilot-instructions.md",
  ".github/workflows/release.yml",
];

function absolute(relativePath) {
  return path.resolve(root, relativePath);
}

function existsExactCase(filePath) {
  const relative = path.relative(root, filePath);
  if (relative === ".." || relative.startsWith(`..${path.sep}`)) return false;
  let current = root;
  for (const segment of relative.split(path.sep).filter(Boolean)) {
    if (!fs.existsSync(current) || !fs.readdirSync(current).includes(segment)) return false;
    current = path.join(current, segment);
  }
  return fs.existsSync(current);
}

function read(relativePath) {
  const file = absolute(relativePath);
  if (!existsExactCase(file)) {
    errors.push(`Missing required file: ${relativePath}`);
    return "";
  }
  return fs.readFileSync(file, "utf8");
}

function walkMarkdown(directory) {
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) return walkMarkdown(file);
    return entry.isFile() && entry.name.toLowerCase().endsWith(".md") ? [file] : [];
  });
}

function markdownLinks(source) {
  const text = source
    .replace(/```[\s\S]*?```/g, "")
    .replace(/~~~[\s\S]*?~~~/g, "")
    .replace(/`[^`]*`/g, "");
  const links = [];
  for (const match of text.matchAll(/\[[^\]]+\]\(([^)]*)\)/g)) {
    let target = match[1].trim();
    if (target.startsWith("<")) target = target.slice(1, target.indexOf(">"));
    else target = target.split(/\s+/)[0];
    if (target) links.push(target);
  }
  return links;
}

function resolveLocalLink(sourceFile, target) {
  if (/^(https?:|mailto:|tel:|data:|javascript:|\/\/|\/|#)/i.test(target)) return null;
  const filePath = decodeURIComponent(target.split(/[?#]/, 1)[0]);
  if (!filePath) return null;
  const resolved = path.resolve(path.dirname(sourceFile), filePath);
  const relative = path.relative(root, resolved);
  if (relative === ".." || relative.startsWith(`..${path.sep}`)) return null;
  return resolved;
}

for (const relativePath of requiredFiles) read(relativePath);

const docsDirectory = absolute("docs");
const docsMarkdown = walkMarkdown(docsDirectory);
const markdownFiles = [
  absolute("README.md"),
  absolute("MEMORY.md"),
  absolute("CHANGELOG.md"),
  absolute(".github/copilot-instructions.md"),
  ...docsMarkdown,
];

for (const sourceFile of markdownFiles) {
  if (!fs.existsSync(sourceFile)) continue;
  const source = fs.readFileSync(sourceFile, "utf8");
  for (const target of markdownLinks(source)) {
    const resolved = resolveLocalLink(sourceFile, target);
    if (resolved && !existsExactCase(resolved)) {
      errors.push(`${path.relative(root, sourceFile)} links to missing path: ${target}`);
    }
  }
}

const indexFiles = docsMarkdown.filter((file) => path.basename(file).toLowerCase() === "readme.md");
const indexedFiles = new Set();
for (const indexFile of indexFiles) {
  const source = fs.readFileSync(indexFile, "utf8");
  for (const target of markdownLinks(source)) {
    const resolved = resolveLocalLink(indexFile, target);
    if (resolved) indexedFiles.add(path.normalize(resolved));
  }
}

for (const file of docsMarkdown) {
  if (file === absolute("docs/README.md")) continue;
  if (!indexedFiles.has(path.normalize(file))) {
    errors.push(`${path.relative(root, file)} is not linked from a docs README index`);
  }
}

const changelog = read("CHANGELOG.md");
const version = read("VERSION").trim();
if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) {
  errors.push(`VERSION is not a valid SemVer version: ${version || "<empty>"}`);
}

const changelogLines = changelog.split(/\r?\n/);
const unreleasedHeaders = [];
for (let index = 0; index < changelogLines.length; index++) {
  if (/^## \[Unreleased\]$/.test(changelogLines[index])) unreleasedHeaders.push(index);
}
if (unreleasedHeaders.length !== 1) {
  errors.push(`CHANGELOG.md must contain exactly one ## [Unreleased] heading (found ${unreleasedHeaders.length})`);
} else {
  const start = unreleasedHeaders[0];
  let end = changelogLines.length;
  for (let index = start + 1; index < changelogLines.length; index++) {
    if (/^## \[/.test(changelogLines[index])) {
      end = index;
      break;
    }
  }

  const allowedCategories = ["Added", "Changed", "Fixed", "Deprecated", "Removed", "Security"];
  const seenCategories = [];
  const categoryEntryCounts = new Map();
  let currentCategory = null;
  for (const line of changelogLines.slice(start + 1, end)) {
    const heading = line.match(/^### (.+)$/);
    if (heading) {
      const category = heading[1];
      if (!allowedCategories.includes(category)) {
        errors.push(`Unsupported CHANGELOG.md category: ${category}`);
      }
      if (seenCategories.includes(category)) {
        errors.push(`Duplicate CHANGELOG.md category under [Unreleased]: ${category}`);
      }
      const order = allowedCategories.indexOf(category);
      const previousOrder = seenCategories.length
        ? allowedCategories.indexOf(seenCategories.at(-1))
        : -1;
      if (order >= 0 && order < previousOrder) {
        errors.push(`CHANGELOG.md categories are out of order at: ${category}`);
      }
      seenCategories.push(category);
      categoryEntryCounts.set(category, 0);
      currentCategory = category;
      continue;
    }
    if (/^### /.test(line)) {
      errors.push(`Malformed CHANGELOG.md category heading: ${line}`);
      currentCategory = null;
      continue;
    }
    if (/^\s*-\s+/.test(line)) {
      if (!currentCategory) errors.push(`Uncategorized [Unreleased] entry: ${line.trim()}`);
      else categoryEntryCounts.set(currentCategory, categoryEntryCounts.get(currentCategory) + 1);
    }
  }
  for (const [category, count] of categoryEntryCounts) {
    if (count === 0) errors.push(`Empty CHANGELOG.md category under [Unreleased]: ${category}`);
  }
}

const escapedVersion = version.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
if (!new RegExp(`^## \\[${escapedVersion}\\] - \\d{4}-\\d{2}-\\d{2}$`, "m").test(changelog)) {
  errors.push(`CHANGELOG.md has no release heading matching VERSION (${version})`);
}
if (!new RegExp(`^\\[Unreleased\\]: https://[^\\s]+/compare/v${escapedVersion}\\.\\.\\.HEAD$`, "m").test(changelog)) {
  errors.push(`CHANGELOG.md Unreleased comparison link must start at v${version}`);
}

const readme = read("README.md");
const capabilities = read("docs/platform/capabilities.md");
const moduleText = read(".gitmodules");
const pluginRoot = absolute("plugins");
const submodulePaths = new Set(
  [...moduleText.matchAll(/^\s*path\s*=\s*(plugins\/Delegat\.Marketplace\.[^/\s]+)\s*$/gm)]
    .map((match) => path.basename(match[1])),
);
const inRepositoryPlugins = fs.existsSync(pluginRoot)
  ? fs.readdirSync(pluginRoot, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && entry.name.startsWith("Delegat.Marketplace."))
      .map((entry) => entry.name)
  : [];
const pluginDirectories = [...new Set([...submodulePaths, ...inRepositoryPlugins])].sort();
const normalizeName = (value) => value.toLowerCase().replace(/[^a-z0-9]/g, "");

for (const pluginDirectory of pluginDirectories) {
  const suffix = pluginDirectory.slice("Delegat.Marketplace.".length);
  const readmeLine = readme.split(/\r?\n/).find((line) => line.includes(`${pluginDirectory}/`));
  if (!readmeLine) {
    errors.push(`${pluginDirectory} is missing from the README.md structure`);
  } else {
    const expectedKind = submodulePaths.has(pluginDirectory) ? "Git submodule" : "in-repository";
    if (!readmeLine.toLowerCase().includes(expectedKind.toLowerCase())) {
      errors.push(`${pluginDirectory} must be identified as ${expectedKind} in README.md`);
    }
  }
  if (!normalizeName(capabilities).includes(normalizeName(suffix))) {
    errors.push(`${pluginDirectory} is missing from docs/platform/capabilities.md`);
  }
}

const releaseGuide = read("docs/platform/releasing.md");
const releaseWorkflow = read(".github/workflows/release.yml");
if (!readme.includes("Do not create the release tag manually")) {
  errors.push("README.md must direct releases through the VERSION workflow, not manual tags");
}
if (!releaseGuide.includes("Git tags alone") || !releaseGuide.includes("VERSION")) {
  errors.push("docs/platform/releasing.md must state VERSION as the release source of truth");
}
if (!releaseWorkflow.includes("paths: ['VERSION']") || !releaseWorkflow.includes("git tag")) {
  errors.push("Release workflow must trigger on VERSION changes and create its own tag");
}

if (process.env.GITHUB_EVENT_NAME === "pull_request" && process.env.GITHUB_EVENT_PATH) {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, "utf8"));
  const pullRequestBody = event.pull_request?.body ?? "";
  const requiredAcknowledgements = [
    "Reviewed CHANGELOG impact; updated for qualifying changes, or no entry is needed.",
    "Reviewed MEMORY impact; updated only for durable project decisions, or no change is needed.",
    "Reviewed canonical docs and indexes; updated applicable docs, or no doc change is needed.",
    "Public API docs and Swagger/XML updated if the contract changed; otherwise not applicable.",
  ];
  for (const acknowledgement of requiredAcknowledgements) {
    const escaped = acknowledgement.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    if (!new RegExp(`^- \\[x\\] ${escaped}\\s*$`, "im").test(pullRequestBody)) {
      errors.push(`PR checklist is incomplete: ${acknowledgement}`);
    }
  }
}

if (errors.length) {
  console.error("Project documentation checks failed:");
  for (const error of errors) console.error(`- ${error}`);
  process.exitCode = 1;
} else {
  console.log(`Project documentation checks passed (${docsMarkdown.length} indexed docs, ${pluginDirectories.length} adapters).`);
}
