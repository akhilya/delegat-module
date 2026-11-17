/**
 * Saved generated-file descriptors for the plan card download control.
 *
 * A descriptor is offered only when every layer agrees: the server-minted
 * plan approval (plan.generatedFileImports), the signed step args, the
 * dispatched tool input, and the completed typed import result. Model prose,
 * nested stdout/results, filenames alone, or arbitrary URLs are never read.
 * These checks are presentation defense; the download endpoints remain the
 * authorization boundary (live selected-store membership).
 */
import { toolResultFailed } from "./persist.js";

function record(value) {
  if (typeof value === "string") {
    try {
      value = JSON.parse(value);
    } catch {
      return null;
    }
  }
  return value && typeof value === "object" && !Array.isArray(value)
    ? value
    : null;
}
function toolName(value) {
  return typeof value === "string" ? value.replace(/^functions\./i, "") : "";
}
const positiveId = (value) =>
  Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
const nullableId = (value) => value == null || positiveId(value);
const equalNullable = (a, b) => (a ?? null) === (b ?? null);

export function generatedFileDownloads(
  plan,
  toolCalls,
  { confirmed = false } = {},
) {
  if (
    confirmed !== true ||
    !plan?.proposalId ||
    !Array.isArray(plan.generatedFileImports) ||
    !Array.isArray(toolCalls)
  )
    return [];
  const files = [];
  const seen = new Set();
  for (const approval of plan.generatedFileImports) {
    if (
      !approval ||
      !Number.isInteger(approval.step) ||
      approval.step < 0 ||
      !positiveId(approval.storeId)
    )
      continue;
    const step = plan.steps?.[approval.step];
    const tool = toolCalls[approval.step];
    if (
      toolName(step?.tool) !== "import_generated_file" ||
      toolName(tool?.name) !== "import_generated_file" ||
      tool?.status !== "done"
    )
      continue;
    const args = record(step.args);
    const input = record(tool.input);
    const result = record(tool.result);
    if (
      !args ||
      !input ||
      !result ||
      result.outcomeUnknown === true ||
      (typeof result.errorClass === "string" && result.errorClass.length > 0) ||
      toolResultFailed(result, tool.summary)
    )
      continue;
    if (
      !["store", "product"].includes(approval.destination) ||
      !nullableId(approval.folderId) ||
      (approval.destination === "product"
        ? !positiveId(approval.productId)
        : approval.productId != null)
    )
      continue;
    if (
      !["artifactId", "sha256", "length", "destination"].every(
        (key) => args[key] === approval[key] && input[key] === approval[key],
      )
    )
      continue;
    if (
      typeof args.expiresAtUtc !== "string" ||
      input.expiresAtUtc !== args.expiresAtUtc
    )
      continue;
    if (
      !["productId", "folderId"].every(
        (key) =>
          equalNullable(args[key], approval[key]) &&
          equalNullable(input[key], approval[key]) &&
          equalNullable(result[key], approval[key]),
      )
    )
      continue;
    if (
      !positiveId(result.fileId) ||
      !Number.isSafeInteger(result.length) ||
      result.length < 0 ||
      result.length !== approval.length ||
      result.sha256 !== approval.sha256 ||
      !/^[a-f0-9]{64}$/.test(result.sha256) ||
      !/^[a-f0-9]{32}$/.test(approval.artifactId) ||
      typeof result.fileName !== "string" ||
      !result.fileName ||
      result.fileName !== approval.fileName ||
      result.destination !== approval.destination
    )
      continue;
    if (!(
      (result.idempotent === false && result.writtenCount === 1) ||
      (result.idempotent === true && result.writtenCount === 0)
    ))
      continue;
    // Validate an existing server URL; never mint a link from model text.
    const expectedUrl =
      result.destination === "store"
        ? `/api/store-storage/files/${result.fileId}/download`
        : `/api/files/${result.fileId}/download`;
    if (result.downloadUrl !== expectedUrl || seen.has(result.downloadUrl))
      continue;
    seen.add(result.downloadUrl);
    files.push({
      step: approval.step,
      fileId: result.fileId,
      destination: result.destination,
      storeId: approval.storeId,
      fileName: result.fileName,
      length: result.length,
      downloadUrl: result.downloadUrl,
    });
  }
  return files;
}

// ---- Temporary generated outputs (F4) --------------------------------------
// Mirrors GeneratedFileContract: the declaration lives in the signed
// run_python args.outputs, each captured artifact must carry the canonical
// server route, and the whole batch fails closed on any mismatch.
const TEMP_MAX_OUTPUTS = 4;
const TEMP_MAX_FILE_BYTES = 32 * 1024 * 1024;
const TEMP_MAX_BATCH_BYTES = 64 * 1024 * 1024;
const TEMP_MAX_NAME_CHARS = 180;
const TEMP_MAX_NAME_BYTES = 255;
const TEMP_MAX_TYPE_CHARS = 200;
const TEMP_DEFAULT_TYPE = "application/octet-stream";
const artifactIdRx = /^[a-f0-9]{32}$/;
const sha256Rx = /^[a-f0-9]{64}$/;
const mediaTypeRx = /^[a-zA-Z0-9!#$&^_.+-]+\/[a-zA-Z0-9!#$&^_.+-]+$/;
// eslint-disable-next-line no-control-regex
const controlCharRx = /[\x00-\x1f\x7f-\x9f/\\]/;

function validOutputFileName(name) {
  if (
    typeof name !== "string" ||
    name.length === 0 ||
    name.length > TEMP_MAX_NAME_CHARS ||
    name !== name.trim() ||
    name === "." ||
    name === ".." ||
    controlCharRx.test(name) ||
    new TextEncoder().encode(name).length > TEMP_MAX_NAME_BYTES
  )
    return false;
  for (const char of name) {
    const cp = char.codePointAt(0);
    if (cp >= 0xd800 && cp <= 0xdfff) return false;
  }
  return true;
}

function normalizeContentType(value) {
  if (value == null) return TEMP_DEFAULT_TYPE;
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  if (
    trimmed.length === 0 ||
    trimmed.length > TEMP_MAX_TYPE_CHARS ||
    trimmed !== value ||
    !mediaTypeRx.test(trimmed)
  )
    return null;
  return trimmed.toLowerCase();
}

// Parses args.outputs to normalized specs or null when the declaration is
// malformed (wrong types, unknown keys, duplicates, 0 or >4 entries).
function parseOutputsDeclaration(args) {
  if (!args || !Array.isArray(args.outputs)) return null;
  const specs = [];
  const seen = new Set();
  for (const item of args.outputs) {
    if (item === null || typeof item !== "object" || Array.isArray(item))
      return null;
    const keys = Object.keys(item);
    if (keys.some((key) => key !== "fileName" && key !== "contentType"))
      return null;
    if (!validOutputFileName(item.fileName) || seen.has(item.fileName))
      return null;
    if (
      Object.prototype.hasOwnProperty.call(item, "contentType") &&
      typeof item.contentType !== "string"
    )
      return null;
    const contentType = normalizeContentType(item.contentType);
    if (contentType === null) return null;
    seen.add(item.fileName);
    specs.push({ fileName: item.fileName, contentType });
  }
  if (specs.length === 0 || specs.length > TEMP_MAX_OUTPUTS) return null;
  return specs;
}

// The declared outputs of a one-step run_python plan, or [] when the plan has
// no valid declaration. Nothing is read from model prose.
export function declaredGeneratedOutputs(plan) {
  if (!Array.isArray(plan?.steps) || plan.steps.length !== 1) return [];
  const step = plan.steps[0];
  if (toolName(step?.tool) !== "run_python") return [];
  const specs = parseOutputsDeclaration(record(step?.args));
  return specs ?? [];
}

function effectiveCode(args) {
  if (typeof args?.code === "string") return args.code;
  if (args?.code === null && typeof args?.script === "string")
    return args.script;
  return null;
}

function sameOutputs(a, b) {
  return (
    Array.isArray(a) &&
    Array.isArray(b) &&
    a.length === b.length &&
    a.every(
      (spec, i) =>
        spec.fileName === b[i].fileName &&
        spec.contentType === b[i].contentType,
    )
  );
}

/**
 * Temporary download descriptors for captured run_python outputs.
 * Offered only after confirmation, only for the signed single step whose
 * dispatched input matches, and only when the completed result promotes an
 * exact batch of server-owned artifacts carrying the canonical downloadUrl.
 * An old handle without downloadUrl yields no control.
 */
export function generatedTemporaryDownloads(
  plan,
  toolCalls,
  { confirmed = false } = {},
) {
  if (confirmed !== true || !plan?.proposalId || !Array.isArray(toolCalls))
    return [];
  const declared = declaredGeneratedOutputs(plan);
  if (declared.length === 0) return [];
  const step = plan.steps[0];
  const tool = toolCalls[0];
  if (toolName(tool?.name) !== "run_python" || tool?.status !== "done")
    return [];
  const args = record(step.args);
  const input = record(tool.input);
  const result = record(tool.result);
  if (
    !args ||
    !input ||
    !result ||
    (result.error != null && result.error !== "") ||
    (result.errorClass != null && result.errorClass !== "") ||
    (result.outcomeUnknown != null && result.outcomeUnknown !== false) ||
    (result.isError != null && result.isError !== false) ||
    result.exitCode !== 0 ||
    toolResultFailed(result, tool.summary)
  )
    return [];
  const code = effectiveCode(args);
  if (code === null || code !== effectiveCode(input)) return [];
  if (!sameOutputs(declared, parseOutputsDeclaration(input))) return [];

  const artifacts = result.artifacts;
  if (!Array.isArray(artifacts) || artifacts.length !== declared.length)
    return [];
  const files = [];
  const seen = new Set();
  let total = 0;
  for (let i = 0; i < artifacts.length; i++) {
    const meta = record(artifacts[i]);
    const spec = declared[i];
    if (
      !meta ||
      typeof meta.artifactId !== "string" ||
      !artifactIdRx.test(meta.artifactId) ||
      seen.has(meta.artifactId) ||
      meta.fileName !== spec.fileName ||
      typeof meta.contentType !== "string" ||
      normalizeContentType(meta.contentType) !== spec.contentType ||
      !Number.isSafeInteger(meta.length) ||
      meta.length < 0 ||
      meta.length > TEMP_MAX_FILE_BYTES ||
      typeof meta.sha256 !== "string" ||
      !sha256Rx.test(meta.sha256) ||
      typeof meta.expiresAtUtc !== "string" ||
      Number.isNaN(Date.parse(meta.expiresAtUtc)) ||
      meta.downloadUrl !==
        `/api/ai/chat/generated-files/${meta.artifactId}/download`
    )
      return [];
    seen.add(meta.artifactId);
    total += meta.length;
    if (total > TEMP_MAX_BATCH_BYTES) return [];
    files.push({
      kind: "temporary",
      artifactId: meta.artifactId,
      fileName: meta.fileName,
      contentType: normalizeContentType(meta.contentType),
      length: meta.length,
      sha256: meta.sha256,
      expiresAtUtc: meta.expiresAtUtc,
      downloadUrl: meta.downloadUrl,
    });
  }
  return files;
}
