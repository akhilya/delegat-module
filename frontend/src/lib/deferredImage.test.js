import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { readFileSync } from "node:fs";
import { imagePaintState } from "./deferredImage.js";

const imageFile = readFileSync(
  new URL("../components/imageFile.vue", import.meta.url),
  "utf8",
);
const appImage = readFileSync(
  new URL("../components/AppImage.vue", import.meta.url),
  "utf8",
);

describe("imagePaintState", () => {
  it("does not wait when there is no src", () => {
    assert.deepEqual(imagePaintState("", null), {
      isLoading: false,
      failed: false,
    });
  });

  it("keeps the in-thumb loader until the real img element reports a decode", () => {
    assert.deepEqual(imagePaintState("/api/files/1/thumbnail?w=256", null), {
      isLoading: true,
      failed: false,
    });
    assert.deepEqual(
      imagePaintState("/api/files/1/thumbnail?w=256", {
        complete: false,
        naturalWidth: 0,
      }),
      { isLoading: true, failed: false },
    );
  });

  it("clears the loader from the element, not a second Image() prefetch", () => {
    assert.deepEqual(
      imagePaintState("/api/files/1/thumbnail?w=256", {
        complete: true,
        naturalWidth: 44,
      }),
      { isLoading: false, failed: false },
    );
    assert.deepEqual(
      imagePaintState("/api/files/1/thumbnail?w=256", {
        complete: true,
        naturalWidth: 0,
      }),
      { isLoading: false, failed: true },
    );
  });
});

describe("catalog photo components do not prefetch", () => {
  it("lets the table paint while thumbs lazy-load into their own spinner", () => {
    for (const src of [imageFile, appImage]) {
      assert.match(src, /loading="lazy"/);
      assert.match(src, /decoding="async"/);
      assert.doesNotMatch(src, /new window\.Image/);
      assert.match(src, /imagePaintState/);
    }
  });
});
