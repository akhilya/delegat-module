import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { readFileSync } from "node:fs";
import {
  PLAN_STEP_PREVIEW,
  planHasContent,
  planHiddenStepCount,
  planStepsNeedCollapse,
} from "./planSteps.js";

describe("plan step preview", () => {
  it("keeps a short plan fully open", () => {
    assert.equal(PLAN_STEP_PREVIEW, 3);
    assert.equal(planStepsNeedCollapse(3), false);
    assert.equal(planHiddenStepCount(3), 0);
  });

  it("hides the tail of a long plan", () => {
    assert.equal(planStepsNeedCollapse(4), true);
    assert.equal(planHiddenStepCount(8), 5);
  });

  it("rejects a plan that has nothing to confirm", () => {
    assert.equal(planHasContent(null), false);
    assert.equal(planHasContent({ summary: "  ", steps: [] }), false);
    assert.equal(planHasContent({ summary: "Export products with photos" }), true);
    assert.equal(
      planHasContent({ steps: [{ description: "Export to Wildberries" }] }),
      true,
    );
  });
});

describe("plan card", () => {
  it("numbers steps and discloses the rest", () => {
    const card = readFileSync(
      new URL("../../components/ai/AiPlanCard.vue", import.meta.url),
      "utf8",
    );
    assert.match(card, /planStepsNeedCollapse/);
    assert.match(card, /Collapsible/);
    assert.match(card, /planShowMore/);
    assert.match(card, /tabular-nums/);
    assert.doesNotMatch(card, /InsetGroupItem/);
    assert.doesNotMatch(card, /flex-1/);
  });
});
