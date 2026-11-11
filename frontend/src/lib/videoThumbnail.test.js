import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { loadVideoThumbnail, videoThumbnailTime } from "./videoThumbnail.js";

class VideoFake extends EventTarget {
  duration = 8;
  readyState = 0;
  videoWidth = 0;
  seeking = false;
  currentTime = 0;
  pauses = 0;
  loads = 0;
  pause() {
    this.pauses++;
  }
  load() {
    this.loads++;
  }
  removeAttribute(name) {
    delete this[name];
  }
  emit(name) {
    this.dispatchEvent(new Event(name));
  }
}

describe("video thumbnail", () => {
  it("chooses an initial frame within even very short videos", () => {
    assert.equal(videoThumbnailTime(20), 0.01);
    assert.equal(videoThumbnailTime(0.01), 0.005);
    for (const duration of [0, -1, NaN, Infinity])
      assert.equal(videoThumbnailTime(duration), 0);
  });

  it("seeks and waits for decoded pixels, without playback", () => {
    const video = new VideoFake();
    let ready = 0;
    const dispose = loadVideoThumbnail(video, "/api/files/12/download", {
      onReady: () => ready++,
      onError: () => assert.fail("unexpected video error"),
    });
    try {
      assert.equal(video.src, "/api/files/12/download");
      assert.equal(video.preload, "metadata");
      assert.equal(video.muted, true);
      assert.equal(video.playsInline, true);
      video.emit("loadedmetadata");
      assert.equal(video.currentTime, 0.01);
      video.emit("seeked");
      assert.equal(ready, 0);
      video.readyState = 2;
      video.videoWidth = 640;
      video.seeking = true;
      video.emit("loadeddata");
      assert.equal(ready, 0);
      video.seeking = false;
      video.emit("seeked");
      video.emit("loadeddata");
      assert.equal(ready, 1);
      assert.equal(video.pauses, 1);
    } finally {
      dispose();
    }
  });

  it("releases failed and disposed requests and ignores stale events", () => {
    const video = new VideoFake();
    let errors = 0;
    const dispose = loadVideoThumbnail(video, "blob:local-video", {
      onReady: () => assert.fail("stale video became ready"),
      onError: () => errors++,
    });
    video.emit("error");
    video.emit("error");
    assert.equal(errors, 1);
    assert.equal(video.src, undefined);
    dispose();
    video.readyState = 2;
    video.videoWidth = 640;
    video.emit("loadeddata");
    video.emit("error");
    assert.equal(errors, 1);
  });

  it("falls back and releases media when loading times out", async () => {
    const video = new VideoFake();
    let dispose;
    try {
      await new Promise((resolve) => {
        dispose = loadVideoThumbnail(video, "/slow-video", {
          onReady: () => assert.fail("unexpected ready"),
          onError: resolve,
          timeoutMs: 1,
        });
      });
      assert.equal(video.src, undefined);
    } finally {
      dispose?.();
    }
  });

  it("uses shared badges and lazy, non-interactive video thumbnails", () => {
    const gallery = readFileSync(
      new URL(
        "../components/products/ProductGallerySidebar.vue",
        import.meta.url,
      ),
      "utf8",
    );
    const thumbnail = readFileSync(
      new URL(
        "../components/products/ProductVideoThumbnail.vue",
        import.meta.url,
      ),
      "utf8",
    );
    assert.match(gallery, /import \{ Badge \} from "@\/components\/ui\/badge"/);
    assert.equal((gallery.match(/<Badge\b/g) || []).length, 2);
    assert.match(gallery, /<ProductVideoThumbnail/);
    assert.match(thumbnail, /IntersectionObserver/);
    assert.match(thumbnail, /tabindex="-1"/);
    assert.doesNotMatch(thumbnail, /\b(?:autoplay|controls)\b/);
  });
});
