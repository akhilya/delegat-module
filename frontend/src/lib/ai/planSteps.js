/** First steps stay visible. The rest sit behind a disclosure. */
export const PLAN_STEP_PREVIEW = 3;
export const PLAN_DESCRIPTION_PREVIEW = 110;

export function planStepDescriptionNeedsCollapse(step) {
  return typeof step?.description === "string" && step.description.length > 180;
}

export function planStepPreview(step) {
  return `${step.description.slice(0, PLAN_DESCRIPTION_PREVIEW).trimEnd()}…`;
}

export function planStepList(plan) {
  const steps = plan?.steps;
  return Array.isArray(steps) ? steps : [];
}

/** Separates catalog writes from operational steps without exposing API IDs. */
export function planStepPresentation(plan) {
  const productChanges = [];
  const otherSteps = [];

  for (const step of planStepList(plan)) {
    const args = step?.args;
    const method =
      typeof args?.method === "string" ? args.method.toUpperCase() : "";
    const rawPath = typeof args?.path === "string" ? args.path : "";
    const path = rawPath
      .split(/[?#]/, 1)[0]
      .replace(/^\/api(?=\/)/i, "")
      .replace(/\/+$/, "");
    const body = args?.body;
    const description =
      typeof step?.description === "string" ? step.description : "";

    const isPlatformRequest = step?.tool === "platform_request";
    const priceDelta = body?.updates?.priceDelta;
    if (
      isPlatformRequest &&
      method === "PATCH" &&
      /^\/products\/bulk$/i.test(path) &&
      typeof priceDelta === "number" &&
      Number.isFinite(priceDelta)
    ) {
      productChanges.push({
        kind: "bulk-price",
        description,
        priceDelta,
        targetCount: Array.isArray(body.productIds)
          ? body.productIds.length
          : null,
      });
      continue;
    }

    const productPath = path.match(/^\/products\/([^/]+)$/i);
    if (
      isPlatformRequest &&
      productPath &&
      !["bulk", "trash", "count"].includes(productPath[1].toLowerCase()) &&
      ["PATCH", "PUT", "POST", "DELETE"].includes(method)
    ) {
      productChanges.push({ kind: "product", description });
      continue;
    }

    otherSteps.push(step);
  }

  return { productChanges, otherSteps };
}

/** A plan with no summary and no steps is not something the user can confirm. */
export function planHasContent(plan) {
  if (String(plan?.summary || "").trim()) return true;
  return planStepList(plan).length > 0;
}

export function planHiddenStepCount(count, max = PLAN_STEP_PREVIEW) {
  const n = Number(count) || 0;
  const limit = Number.isFinite(max) && max > 0 ? max : PLAN_STEP_PREVIEW;
  return Math.max(0, n - limit);
}

export function planStepsNeedCollapse(count, max = PLAN_STEP_PREVIEW) {
  return planHiddenStepCount(count, max) > 0;
}
