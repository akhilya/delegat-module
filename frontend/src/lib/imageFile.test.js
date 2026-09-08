import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const imageFile = readFileSync(
  new URL("../components/imageFile.vue", import.meta.url),
  "utf8",
);
const appImage = readFileSync(
  new URL("../components/AppImage.vue", import.meta.url),
  "utf8",
);
const cell = readFileSync(
  new URL("../components/products/ProductImageCell.vue", import.meta.url),
  "utf8",
);

describe("missing product photo placeholder", () => {
  it("treats a failed thumb like an empty photo, not a broken img with alt text", () => {
    assert.match(imageFile, /failed/);
    assert.match(imageFile, /onImageError/);
    assert.match(imageFile, /failed: true/);
    assert.match(imageFile, /v-if="showPhoto"/);
    assert.match(imageFile, /<Image class="text-secondary-foreground size-\[25%\]"/);
    assert.match(imageFile, /referrerpolicy="no-referrer"/);
    assert.doesNotMatch(imageFile, /v-if="file"/);
  });

  it("shows the empty photo slot when AppImage cannot load the src", () => {
    assert.match(appImage, /failed\.value = state\.failed/);
    assert.match(appImage, /failed: true/);
    assert.match(appImage, /v-if="src && !failed"/);
    assert.match(appImage, /<slot v-else>/);
    assert.match(appImage, /referrerpolicy="no-referrer"/);
  });

  it("does not open a hover preview for a thumb that failed to load", () => {
    assert.match(cell, /thumbFailed/);
    assert.match(cell, /previewSrc && !thumbFailed/);
    assert.match(cell, /@error="thumbFailed = true"/);
  });
});
