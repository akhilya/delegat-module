import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  getAdapterCredentialsDocsSlug,
  getDocsPageUrl,
  getDocsPublicUrl,
  getDocsSiteOrigin,
} from "./docsUrl.js";

describe("docsUrl", () => {
  it("builds public page URLs on the Control Plane docs origin", () => {
    globalThis.window = {
      __DELEGAT_DOCS_PUBLIC_URL__: "https://module.delegat.pro/docs",
    };
    try {
      assert.equal(getDocsSiteOrigin(), "https://module.delegat.pro");
      assert.equal(
        getDocsPageUrl("workspace/products", "ru"),
        "https://module.delegat.pro/docs/workspace/products",
      );
      assert.equal(
        getDocsPageUrl("workspace/products", "en"),
        "https://module.delegat.pro/docs/en/workspace/products",
      );
      assert.equal(getDocsPublicUrl("ru"), "https://module.delegat.pro/docs/");
      assert.equal(getDocsPageUrl("", "ru"), "https://module.delegat.pro/docs/");
      assert.equal(getAdapterCredentialsDocsSlug("ozon"), "adapters/ozon");
      assert.equal(getAdapterCredentialsDocsSlug("unknown"), null);
    } finally {
      delete globalThis.window;
    }
  });
});
