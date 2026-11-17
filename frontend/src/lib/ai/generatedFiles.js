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
