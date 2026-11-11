import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { getMediaKind, isGalleryMedia } from "./mediaKind.js";

describe("mediaKind", () => {
  it("classifies normalized image and video MIME types", () => {
    assert.equal(getMediaKind("IMAGE/JPEG"), "image");
    assert.equal(getMediaKind(" video/mp4 ; codecs=avc1"), "video");
    assert.equal(isGalleryMedia("image/webp"), true);
    assert.equal(isGalleryMedia("video/webm"), true);
  });

  it("does not infer gallery media from names or malformed MIME values", () => {
    for (const value of [
      "",
      null,
      "image",
      "application/pdf",
      "application/octet-stream",
      "video/../mp4",
      "clip.mp4",
    ]) {
      assert.equal(getMediaKind(value), "file", String(value));
      assert.equal(isGalleryMedia(value), false, String(value));
    }
  });
});
