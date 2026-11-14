import { test } from "node:test";
import assert from "node:assert/strict";
import { defaultImportAttributeDecisions } from "./importAttributeDecisions.js";

test("only unambiguous duplicate candidates are suggested", () => {
  const decisions = defaultImportAttributeDecisions([
    {
      key: "clear",
      potentialDuplicates: [
        { id: 1, score: 90 },
        { id: 2, score: 85 },
      ],
    },
    {
      key: "ambiguous",
      potentialDuplicates: [
        { id: 3, score: 90 },
        { id: 4, score: 86 },
      ],
    },
    { key: "missing-scores", potentialDuplicates: [{ id: 5 }, { id: 6 }] },
    { key: "single", potentialDuplicates: [{ id: 7, score: 75 }] },
    {
      key: "base",
      isBaseField: true,
      potentialDuplicates: [{ id: 8, score: 100 }],
    },
  ]);
  assert.deepEqual(decisions, {
    clear: { action: "reuse", existingAttributeId: 1 },
    single: { action: "reuse", existingAttributeId: 7 },
  });
});
