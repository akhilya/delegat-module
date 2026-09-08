import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { shouldPinPageFooter } from "./pageDockFooter.js";

describe("shouldPinPageFooter", () => {
  it("keeps the footer under the last row when leftover space sits below it", () => {
    assert.equal(
      shouldPinPageFooter({
        scrollHeight: 800,
        clientHeight: 800,
        leftoverBelowFooter: 240,
        pinned: false,
      }),
      false,
    );
    assert.equal(
      shouldPinPageFooter({
        scrollHeight: 820,
        clientHeight: 800,
        leftoverBelowFooter: 200,
        pinned: false,
      }),
      false,
    );
  });

  it("pins the footer when the table overflows the page scroller", () => {
    assert.equal(
      shouldPinPageFooter({
        scrollHeight: 960,
        clientHeight: 800,
        leftoverBelowFooter: -80,
        pinned: false,
      }),
      true,
    );
    assert.equal(
      shouldPinPageFooter({
        scrollHeight: 900,
        clientHeight: 752,
        leftoverBelowFooter: 0,
        pinned: true,
      }),
      true,
    );
  });

  it("releases the pin when a short table fits in the scroller again", () => {
    assert.equal(
      shouldPinPageFooter({
        scrollHeight: 400,
        clientHeight: 752,
        leftoverBelowFooter: 0,
        pinned: true,
      }),
      false,
    );
  });
});
