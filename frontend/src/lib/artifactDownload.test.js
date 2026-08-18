import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { artifactDownloadUrl } from "./artifactDownload.js";

describe("artifactDownloadUrl", () => {
  it("builds a same-origin operations artifact path", () => {
    assert.equal(
      artifactDownloadUrl("/api", "abc123"),
      "/api/operations/abc123/artifact",
    );
  });

  it("strips a trailing slash on the API base", () => {
    assert.equal(
      artifactDownloadUrl("http://localhost:5000/api/", "op1"),
      "http://localhost:5000/api/operations/op1/artifact",
    );
  });

  it("encodes the operation id", () => {
    assert.equal(
      artifactDownloadUrl("/api", "a/b"),
      "/api/operations/a%2Fb/artifact",
    );
  });
});
