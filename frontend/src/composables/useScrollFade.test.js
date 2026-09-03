import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { SCROLL_FADE_SIZE, scrollFadeEdges } from "./useScrollFade.js";

describe("scrollFadeEdges", () => {
  it("hides both fades when content does not overflow", () => {
    assert.deepEqual(
      scrollFadeEdges({ scrollTop: 0, scrollHeight: 200, clientHeight: 200 }),
      { top: 0, bottom: 0 },
    );
  });

  it("hides the top fade at the start and the bottom fade at the end", () => {
    const start = scrollFadeEdges({
      scrollTop: 0,
      scrollHeight: 800,
      clientHeight: 200,
    });
    assert.equal(start.top, 0);
    assert.equal(start.bottom, SCROLL_FADE_SIZE);

    const end = scrollFadeEdges({
      scrollTop: 600,
      scrollHeight: 800,
      clientHeight: 200,
    });
    assert.equal(end.top, SCROLL_FADE_SIZE);
    assert.equal(end.bottom, 0);
  });

  it("shows both fades in the middle of the scroll", () => {
    const mid = scrollFadeEdges({
      scrollTop: 200,
      scrollHeight: 800,
      clientHeight: 200,
    });
    assert.equal(mid.top, SCROLL_FADE_SIZE);
    assert.equal(mid.bottom, SCROLL_FADE_SIZE);
  });

  it("grows the fade over the reveal distance", () => {
    const halfway = scrollFadeEdges({
      scrollTop: 12,
      scrollHeight: 800,
      clientHeight: 200,
      size: 40,
      reveal: 24,
    });
    assert.equal(halfway.top, 20);
    assert.equal(halfway.bottom, 40);
  });
});
