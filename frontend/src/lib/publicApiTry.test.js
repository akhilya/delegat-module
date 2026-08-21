import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  findPublicApiTryEndpoint,
  normalizePublicApiTryPath,
  PUBLIC_API_TRY_ENDPOINTS,
  publicApiV1Url,
  publicApiV1UrlFromEnv,
  sendPublicApiTry,
} from "./publicApiTry.js";

describe("publicApiV1Url", () => {
  it("joins the SPA /api prefix with a v1 path", () => {
    assert.equal(publicApiV1Url("/me"), "/api/v1/me");
    assert.equal(publicApiV1Url("stores", "/api"), "/api/v1/stores");
    assert.equal(
      publicApiV1Url("/me", "http://localhost:5000/api"),
      "http://localhost:5000/api/v1/me",
    );
  });

  it("uses resolveApiBaseUrl for a bare Vite origin", () => {
    assert.equal(
      publicApiV1UrlFromEnv("/me", "http://localhost:5000"),
      "http://localhost:5000/api/v1/me",
    );
  });
});

describe("normalizePublicApiTryPath", () => {
  it("accepts relative v1 paths and strips an /api/v1 prefix", () => {
    assert.equal(normalizePublicApiTryPath("/me"), "/me");
    assert.equal(normalizePublicApiTryPath("stores"), "/stores");
    assert.equal(normalizePublicApiTryPath("/api/v1/stores"), "/stores");
    assert.equal(
      normalizePublicApiTryPath("/stores/1/products"),
      "/stores/1/products",
    );
  });

  it("rejects empty values, URLs, and parent segments", () => {
    assert.equal(normalizePublicApiTryPath(""), null);
    assert.equal(normalizePublicApiTryPath("https://evil.test/me"), null);
    assert.equal(normalizePublicApiTryPath("/api/v1/../admin"), null);
  });
});

describe("PUBLIC_API_TRY_ENDPOINTS", () => {
  it("exposes lightweight GET meta routes", () => {
    assert.deepEqual(
      PUBLIC_API_TRY_ENDPOINTS.map((e) => e.path),
      ["/me", "/stores"],
    );
    assert.ok(PUBLIC_API_TRY_ENDPOINTS.every((e) => e.method === "GET"));
    assert.equal(findPublicApiTryEndpoint("stores").path, "/stores");
    assert.equal(findPublicApiTryEndpoint("missing").id, "me");
  });
});

describe("sendPublicApiTry", () => {
  it("refuses an empty key without calling fetch", async () => {
    let called = false;
    const result = await sendPublicApiTry({
      fetchImpl: async () => {
        called = true;
        return { ok: true, status: 200, text: async () => "" };
      },
      url: "/api/v1/me",
      apiKey: "  ",
    });
    assert.equal(called, false);
    assert.equal(result.error, "key_required");
    assert.equal(result.status, 0);
  });

  it("sends Bearer auth, omits cookies, and pretty-prints JSON", async () => {
    let captured;
    const result = await sendPublicApiTry({
      fetchImpl: async (url, init) => {
        captured = { url, init };
        return {
          ok: true,
          status: 200,
          text: async () => '{"id":1}',
        };
      },
      url: "/api/v1/me",
      apiKey: " dk_test ",
      now: (() => {
        let n = 0;
        return () => {
          n += 12;
          return n;
        };
      })(),
    });
    assert.equal(result.status, 200);
    assert.equal(result.body, '{\n  "id": 1\n}');
    assert.equal(result.durationMs, 12);
    assert.equal(captured.url, "/api/v1/me");
    assert.equal(captured.init.method, "GET");
    assert.equal(captured.init.credentials, "omit");
    assert.equal(captured.init.headers.Authorization, "Bearer dk_test");
  });
});
