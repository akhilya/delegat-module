import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  adapterKeyFromGroupIcon,
  groupIconForAdapter,
  inferAdapterKeyFromGroupName,
  resolveGroupIcon,
  suggestedGroupIcon,
} from "./attributeGroupIcon.js";

describe("attributeGroupIcon", () => {
  it("encodes and reads adapter tokens", () => {
    assert.equal(groupIconForAdapter("Wildberries"), "adapter:wildberries");
    assert.equal(adapterKeyFromGroupIcon("adapter:ozon"), "ozon");
  });

  it("infers marketplace keys from imported group names", () => {
    assert.equal(inferAdapterKeyFromGroupName("Wildberries"), "wildberries");
    assert.equal(inferAdapterKeyFromGroupName("Yandex Market"), "yandex");
    assert.equal(inferAdapterKeyFromGroupName("Ozon"), "ozon");
  });

  it("resolves a stored adapter icon first", () => {
    assert.deepEqual(resolveGroupIcon("Ozon", "adapter:ozon"), {
      type: "adapter",
      key: "ozon",
    });
  });

  it("falls back to a lucide name for built-in groups", () => {
    const resolved = resolveGroupIcon("\u0411\u0440\u0435\u043d\u0434", "");
    assert.equal(resolved?.type, "lucide");
    assert.equal(resolved?.name, "Tag");
  });

  it("suggests adapter: token for a marketplace group name", () => {
    assert.equal(suggestedGroupIcon("Wildberries"), "adapter:wildberries");
  });
});
