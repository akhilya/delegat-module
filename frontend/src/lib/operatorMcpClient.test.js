import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  OPERATOR_MCP_KEY_NAME,
  createdApiKeyPlaintext,
  issueOperatorMcpKey,
  operatorMcpHeaders,
  operatorMcpJson,
  operatorMcpPublicOrigin,
  operatorMcpUrl,
} from "./operatorMcpClient.js";

describe("operator MCP client config", () => {
  it("builds one Streamable HTTP mcp.json (type http, no stdio command)", () => {
    const url = operatorMcpUrl("https://shop.example/");
    assert.equal(url, "https://shop.example/api/mcp");
    const cfg = operatorMcpJson({
      origin: "https://shop.example",
      apiKey: "dk_secret",
      storeId: 7,
    });
    const server = cfg.mcpServers.delegat;
    assert.equal(server.type, "http");
    assert.equal(server.url, url);
    assert.equal(server.command, undefined);
    assert.deepEqual(server.headers, {
      Authorization: "Bearer dk_secret",
      "X-Delegat-Store-Id": "7",
    });
  });

  it("does not emit a config without a real API key", () => {
    assert.throws(
      () => operatorMcpJson({ origin: "https://shop.example", storeId: 1 }),
      /mcp_key_required/,
    );
    assert.deepEqual(operatorMcpHeaders({ apiKey: "dk_secret", storeId: 3 }), {
      Authorization: "Bearer dk_secret",
      "X-Delegat-Store-Id": "3",
    });
  });

  it("points the client at the API host in local Vite, not the SPA origin", () => {
    assert.equal(
      operatorMcpPublicOrigin({
        pageOrigin: "http://localhost:5173",
        apiBase: "http://localhost:5000/api",
      }),
      "http://localhost:5000",
    );
    assert.equal(
      operatorMcpPublicOrigin({
        pageOrigin: "https://shop.example",
        apiBase: "/api",
      }),
      "https://shop.example",
    );
    assert.equal(
      operatorMcpPublicOrigin({
        pageOrigin: "http://localhost:5173",
        apiBase: "/api",
      }),
      "http://localhost:5000",
    );
    assert.equal(
      operatorMcpPublicOrigin({
        pageOrigin: "http://127.0.0.1:5173",
        apiBase: "/api",
      }),
      "http://127.0.0.1:5000",
    );
  });

  it("mints an API key named MCP and reads plaintext from the create response", async () => {
    assert.equal(OPERATOR_MCP_KEY_NAME, "MCP");
    assert.equal(
      createdApiKeyPlaintext({ value: { plaintext: "dk_from_wrap" } }),
      "dk_from_wrap",
    );
    const calls = [];
    const plaintext = await issueOperatorMcpKey({
      post: async (path, body) => {
        calls.push({ path, body });
        return { data: { value: { plaintext: "dk_minted" } } };
      },
    });
    assert.equal(plaintext, "dk_minted");
    assert.deepEqual(calls, [
      { path: "/api-keys", body: { name: "MCP" } },
    ]);
  });
});
