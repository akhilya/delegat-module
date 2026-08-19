import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { catalogConnectMatchQueue } from "./catalogConnectMatchQueue.js";

const ozon = { id: 1, adapterKey: "ozon", productLinksCount: 0 };
const wb = { id: 2, adapterKey: "wildberries", productLinksCount: 0 };

describe("catalogConnectMatchQueue", () => {
  it("excludes the import source even when the store is still empty", () => {
    assert.deepEqual(
      catalogConnectMatchQueue([ozon, wb], { sourceId: 1, localProductCount: 0 }),
      [2],
    );
  });

  it("treats string and number connection ids as the same source", () => {
    assert.deepEqual(
      catalogConnectMatchQueue([ozon, wb], { sourceId: "1", localProductCount: 0 }),
      [2],
    );
  });

  it("skips a fully linked connection after import even if sourceId was lost", () => {
    assert.deepEqual(
      catalogConnectMatchQueue(
        [
          { ...ozon, productLinksCount: 10 },
          { ...wb, productLinksCount: 0 },
        ],
        { localProductCount: 10 },
      ),
      [2],
    );
  });

  it("does not treat zero products as every connection already linked", () => {
    assert.deepEqual(
      catalogConnectMatchQueue(
        [
          { ...ozon, productLinksCount: 0 },
          { ...wb, productLinksCount: 0 },
        ],
        { localProductCount: 0 },
      ),
      [1, 2],
    );
  });

  it("still excludes the source when it is only partly linked", () => {
    assert.deepEqual(
      catalogConnectMatchQueue(
        [
          { ...ozon, productLinksCount: 4 },
          { ...wb, productLinksCount: 0 },
        ],
        { sourceId: 1, localProductCount: 10 },
      ),
      [2],
    );
  });

  it("skips inactive connections", () => {
    assert.deepEqual(
      catalogConnectMatchQueue(
        [
          { ...ozon, isActive: false },
          wb,
        ],
        { localProductCount: 0 },
      ),
      [2],
    );
  });
});
