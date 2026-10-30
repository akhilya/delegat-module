import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { readFileSync } from "node:fs";
import {
  PLAN_STEP_PREVIEW,
  planHasContent,
  planHiddenStepCount,
  planStepPresentation,
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
    assert.equal(
      planHasContent({ summary: "Export products with photos" }),
      true,
    );
    assert.equal(
      planHasContent({ steps: [{ description: "Export to Wildberries" }] }),
      true,
    );
  });
});

describe("plan step presentation", () => {
  it("summarizes bulk price changes without exposing product IDs", () => {
    const result = planStepPresentation({
      affectedCount: 2,
      steps: [
        {
          tool: "platform_request",
          description: "Increase prices for the selected products by 500",
          args: {
            method: "PATCH",
            path: "/api/products/bulk?wait=true",
            body: {
              productIds: [41, 42],
              updates: { priceDelta: 500 },
            },
          },
        },
        {
          tool: "platform_request",
          description: "Export updated prices to Ozon",
          args: {
            method: "POST",
            path: "/tools/export-all-prices/run",
          },
        },
      ],
    });

    assert.deepEqual(result.productChanges, [
      {
        kind: "bulk-price",
        description: "Increase prices for the selected products by 500",
        priceDelta: 500,
        targetCount: 2,
      },
    ]);
    assert.equal(result.otherSteps.length, 1);
    assert.equal(JSON.stringify(result.productChanges).includes("41"), false);
    assert.equal(JSON.stringify(result.productChanges).includes("42"), false);
  });

  it("groups individual product writes and leaves other operations in order", () => {
    const result = planStepPresentation({
      steps: [
        {
          tool: "platform_request",
          description: "Update SKU A-17 price",
          args: {
            method: "PATCH",
            path: "/products/17",
            body: { price: 1200 },
          },
        },
        {
          tool: "platform_request",
          description: "Refresh Ozon prices",
          args: { path: "/tools/export-all-prices/run" },
        },
        {
          tool: "platform_request",
          description: "Update SKU B-21 price",
          args: { method: "PATCH", path: "/products/21", body: { price: 900 } },
        },
      ],
    });

    assert.deepEqual(
      result.productChanges.map(({ kind, description }) => ({
        kind,
        description,
      })),
      [
        { kind: "product", description: "Update SKU A-17 price" },
        { kind: "product", description: "Update SKU B-21 price" },
      ],
    );
    assert.deepEqual(
      result.otherSteps.map((step) => step.description),
      ["Refresh Ozon prices"],
    );
    assert.equal(
      JSON.stringify(result.productChanges).includes("/products/17"),
      false,
    );
  });

  it("does not treat a bulk request without a numeric price delta as a price summary", () => {
    const result = planStepPresentation({
      steps: [
        {
          tool: "platform_request",
          description: "Update inventory",
          args: {
            method: "PATCH",
            path: "/products/bulk",
            body: { productIds: [1], updates: { stock: 10 } },
          },
        },
      ],
    });

    assert.deepEqual(result.productChanges, []);
    assert.equal(result.otherSteps.length, 1);
  });
});

describe("plan card", () => {
  it("waits for the live turn to finish persisting before enabling confirmation", () => {
    const panel = readFileSync(
      new URL("../../components/ai/AiChatPanel.vue", import.meta.url),
      "utf8",
    );
    assert.match(panel, /:ready="msg\.done && !isLoading"/);
  });

  it("numbers steps and discloses the rest", () => {
    const card = readFileSync(
      new URL("../../components/ai/AiPlanCard.vue", import.meta.url),
      "utf8",
    );
    assert.match(card, /planStepsNeedCollapse/);
    assert.match(card, /Collapsible/);
    assert.match(card, /planProductDetails/);
    assert.match(card, /planProductChanges/);
    assert.match(card, /planShowMore/);
    assert.match(card, /tabular-nums/);
    assert.doesNotMatch(card, /InsetGroupItem/);
    assert.doesNotMatch(card, /flex-1/);
  });
});
