/** First steps stay visible. The rest sit behind a disclosure. */
export const PLAN_STEP_PREVIEW = 3;

export function planStepList(plan) {
  const steps = plan?.steps;
  return Array.isArray(steps) ? steps : [];
}

export function planHiddenStepCount(count, max = PLAN_STEP_PREVIEW) {
  const n = Number(count) || 0;
  const limit = Number.isFinite(max) && max > 0 ? max : PLAN_STEP_PREVIEW;
  return Math.max(0, n - limit);
}

export function planStepsNeedCollapse(count, max = PLAN_STEP_PREVIEW) {
  return planHiddenStepCount(count, max) > 0;
}
