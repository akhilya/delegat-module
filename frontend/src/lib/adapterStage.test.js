import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  ADAPTER_STAGES,
  adapterStage,
  adapterStageLabelKey,
  isExperimentalAdapter,
  isExperimentalConnection,
} from "./adapterStage.js";

describe("adapterStage", () => {
  it("treats Stable as unlabeled", () => {
    assert.equal(adapterStage("Stable"), ADAPTER_STAGES.stable);
    assert.equal(adapterStage({ stability: "stable" }), ADAPTER_STAGES.stable);
    assert.equal(isExperimentalAdapter({ stability: "Stable" }), false);
    assert.equal(adapterStageLabelKey({ stability: "Stable" }), "");
  });

  it("maps Experimental and Beta to the experimental badge", () => {
    assert.equal(adapterStage("Experimental"), ADAPTER_STAGES.experimental);
    assert.equal(adapterStage({ Stability: "Beta" }), ADAPTER_STAGES.experimental);
    assert.equal(isExperimentalAdapter("beta"), true);
    assert.equal(
      adapterStageLabelKey({ stability: "Experimental" }),
      "common.stageExperimental",
    );
  });

  it("looks up a connection's adapter", () => {
    const adapters = [
      { key: "ozon", stability: "Stable" },
      { key: "medusa", stability: "Experimental" },
    ];
    assert.equal(
      isExperimentalConnection({ adapterKey: "ozon" }, adapters),
      false,
    );
    assert.equal(
      isExperimentalConnection({ adapterKey: "medusa" }, adapters),
      true,
    );
  });
});
