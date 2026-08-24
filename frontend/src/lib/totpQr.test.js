import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { otpauthQrSvg } from "./totpQr.js";

describe("otpauthQrSvg", () => {
  it("returns an SVG for an otpauth URI and never uses Google Charts", async () => {
    const svg = await otpauthQrSvg(
      "otpauth://totp/Delegat:admin?secret=JBSWY3DPEHPK3PXP&issuer=Delegat&digits=6&period=30",
    );
    assert.match(svg, /<svg[\s\S]*<\/svg>/i);
    assert.doesNotMatch(svg, /chart\.googleapis\.com/i);
  });

  it("rejects non-otpauth values", async () => {
    await assert.rejects(
      () => otpauthQrSvg("https://chart.googleapis.com/chart?chs=200x200"),
      /otpauth URI required/,
    );
  });
});
