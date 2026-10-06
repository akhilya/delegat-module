import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { mapPool } from "./runPool.js";

describe("mapPool", () => {
  it("keeps result order and limits how many run at once", async () => {
    let inFlight = 0;
    let maxInFlight = 0;
    const started = [];

    const results = await mapPool(
      [1, 2, 3, 4, 5, 6],
      async (item) => {
        started.push(item);
        inFlight += 1;
        maxInFlight = Math.max(maxInFlight, inFlight);
        await new Promise((resolve) => setTimeout(resolve, 20));
        inFlight -= 1;
        return item * 10;
      },
      3,
    );

    assert.deepEqual(results, [10, 20, 30, 40, 50, 60]);
    assert.equal(maxInFlight, 3);
    assert.deepEqual(started.slice(0, 3), [1, 2, 3]);
  });
});
