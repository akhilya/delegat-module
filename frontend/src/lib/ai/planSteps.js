/** First steps stay visible. The rest sit behind a disclosure. */
export const PLAN_STEP_PREVIEW = 3;

export function planStepList(plan) {
  const steps = plan?.steps;
  return Array.isArray(steps) ? steps : [];
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
