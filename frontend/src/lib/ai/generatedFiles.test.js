import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { generatedFileDownloads } from "./generatedFiles.js";
import { mapStoredMessage, serializeToolCallsJson } from "./persist.js";

// A realistic signed import step: server approval, exact signed args,
// dispatched input and the typed service result all carry the same tuple.
const ART = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
const SHA = "b".repeat(64);
const REQUEST = {
  artifactId: ART,
  sha256: SHA,
  length: 18,
  expiresAtUtc: "2030-01-01T00:00:00Z",
  destination: "store",
  productId: null,
  folderId: null,
};
const RESULT = {
  destination: "store",
  fileId: 42,
  productId: null,
  folderId: null,
  fileName: "r.csv",
  contentType: "text/csv",
  length: 18,
  sha256: SHA,
  downloadUrl: "/api/store-storage/files/42/download",
  idempotent: false,
  writtenCount: 1,
};

function makePlan({
  destination = "store",
  productId = null,
  folderId = null,
  step = 0,
  steps,
  approval = {},
  expiresAtUtc = REQUEST.expiresAtUtc,
} = {}) {
  const req = {
    ...REQUEST,
    destination,
    productId,
    folderId,
    expiresAtUtc,
  };
  const imp = {
    step,
    artifactId: ART,
    fileName: "r.csv",
    length: 18,
    sha256: SHA,
    expiresAtUtc,
    destination,
    storeId: 7,
    productId,
    folderId,
    ...approval,
  };
  const base = steps || [
    { tool: "import_generated_file", description: "save", args: { ...req } },
  ];
  return {
    plan: {
      proposalId: "prop-1",
      summary: "s",
      steps: base,
      generatedFileImports: [imp],
    },
    req,
    imp,
  };
}

function makeTool({
  result = RESULT,
  status = "done",
  input,
  name = "import_generated_file",
  summary,
} = {}) {
  return {
    id: "s1",
    name,
    status,
    input: input === undefined ? JSON.stringify(REQUEST) : input,
    summary,
    result,
  };
}

describe("generatedFileDownloads", () => {
  it("binds a confirmed store import to its canonical download path", () => {
    const { plan } = makePlan();
    const files = generatedFileDownloads(plan, [makeTool()], {
      confirmed: true,
    });
    assert.equal(files.length, 1);
    assert.deepEqual(files[0], {
      step: 0,
      fileId: 42,
      destination: "store",
      storeId: 7,
      fileName: "r.csv",
      length: 18,
      downloadUrl: "/api/store-storage/files/42/download",
    });
  });

  it("binds a product import to the product file route", () => {
    const { plan } = makePlan({ destination: "product", productId: 9 });
    const result = {
      ...RESULT,
      destination: "product",
      productId: 9,
      downloadUrl: "/api/files/42/download",
    };
    const tool = makeTool({
      result,
      input: JSON.stringify({
        ...REQUEST,
        destination: "product",
        productId: 9,
      }),
    });
    const files = generatedFileDownloads(plan, [tool], { confirmed: true });
    assert.equal(files.length, 1);
    assert.equal(files[0].downloadUrl, "/api/files/42/download");
  });

  it("accepts result as object or JSON string", () => {
    const { plan } = makePlan();
    for (const result of [RESULT, JSON.stringify(RESULT)]) {
      const files = generatedFileDownloads(plan, [makeTool({ result })], {
        confirmed: true,
      });
      assert.equal(files.length, 1);
    }
  });

  it("accepts an idempotent completed-receipt result", () => {
    const { plan } = makePlan();
    const result = { ...RESULT, idempotent: true, writtenCount: 0 };
    const files = generatedFileDownloads(plan, [makeTool({ result })], {
      confirmed: true,
    });
    assert.equal(files.length, 1);
  });

  it("keeps a saved file downloadable after source staging expires", () => {
    const { plan } = makePlan({ expiresAtUtc: "2020-01-01T00:00:00Z" });
    const tool = makeTool({
      input: JSON.stringify({
        ...REQUEST,
        expiresAtUtc: "2020-01-01T00:00:00Z",
      }),
    });
    const files = generatedFileDownloads(plan, [tool], { confirmed: true });
    assert.equal(files.length, 1);
  });

  it("maps the zero-based step across interleaved tools", () => {
    const steps = [
      {
        tool: "platform_request",
        description: "read",
        args: { method: "GET", path: "/products/1" },
      },
      {
        tool: "import_generated_file",
        description: "save",
        args: { ...REQUEST },
      },
    ];
    const { plan } = makePlan({ step: 1, steps });
    const tools = [
      {
        id: "t0",
        name: "platform_request",
        status: "done",
        input: JSON.stringify({ method: "GET", path: "/products/1" }),
        result: { status: 200 },
      },
      makeTool(),
    ];
    const files = generatedFileDownloads(plan, tools, { confirmed: true });
    assert.equal(files.length, 1);
    assert.equal(files[0].step, 1);
  });

  it("still exposes an earlier completed import when a later step failed", () => {
    const steps = [
      {
        tool: "import_generated_file",
        description: "save",
        args: { ...REQUEST },
      },
      { tool: "platform_request", description: "x", args: {} },
    ];
    const { plan } = makePlan({ step: 0, steps });
    const tools = [
      makeTool(),
      {
        id: "t1",
        name: "platform_request",
        status: "done",
        input: "{}",
        summary: "HTTP 400",
        result: { status: 400 },
      },
    ];
    plan.planFailed = true;
    const files = generatedFileDownloads(plan, tools, { confirmed: true });
    assert.equal(files.length, 1);
  });

  it("still exposes an earlier completed import when a later step stopped", () => {
    const steps = [
      {
        tool: "import_generated_file",
        description: "save",
        args: { ...REQUEST },
      },
      { tool: "platform_request", description: "x", args: {} },
    ];
    const { plan } = makePlan({ step: 0, steps });
    const tools = [
      makeTool(),
      {
        id: "t1",
        name: "platform_request",
        status: "stopped",
        input: "{}",
        summary: "interrupted",
        result: null,
      },
    ];
    plan.turnStopped = true;
    const files = generatedFileDownloads(plan, tools, { confirmed: true });
    assert.equal(files.length, 1);
  });

  it("round-trips through persist serialize/hydrate with the execution boundary intact", () => {
    const { plan } = makePlan();
    const proposalRead = {
      id: "r0",
      name: "platform_request",
      status: "done",
      input: JSON.stringify({ method: "GET", path: "/products/1" }),
      summary: "200",
      result: { status: 200 },
    };
    const message = {
      role: "assistant",
      content: "",
      toolCalls: [proposalRead, makeTool()],
      timeline: [
        { type: "text", text: "prepared" },
        { type: "tool", id: "r0" },
        { type: "tool", id: "s1" },
      ],
      plan,
      planConfirmed: true,
      planExecuted: true,
      planExecStart: 2,
      messageId: "srv-m1",
    };
    const restored = mapStoredMessage({
      dbId: 5,
      role: "assistant",
      content: "",
      toolCallsJson: serializeToolCallsJson(message),
    });
    const execTools = restored.timeline
      .slice(restored.planExecStart)
      .filter((item) => item.type === "tool" && item.tool)
      .map((item) => item.tool);
    const files = generatedFileDownloads(restored.plan, execTools, {
      confirmed: restored.planConfirmed,
    });
    assert.equal(files.length, 1);
    assert.equal(files[0].downloadUrl, "/api/store-storage/files/42/download");
  });
});

describe("generatedFileDownloads negatives", () => {
  const { plan } = makePlan();
  const good = [makeTool()];
  const cases = {
    "unconfirmed plan": [plan, good, { confirmed: false }],
    "no proposalId": [{ ...plan, proposalId: null }, good, { confirmed: true }],
    "missing approval": [
      { ...plan, generatedFileImports: [] },
      good,
      { confirmed: true },
    ],
    "malformed approval": [
      { ...plan, generatedFileImports: [null] },
      good,
      { confirmed: true },
    ],
    "approval step out of range": [
      {
        ...plan,
        generatedFileImports: [{ ...plan.generatedFileImports[0], step: 3 }],
      },
      good,
      { confirmed: true },
    ],
    "wrong step tool": [
      {
        ...plan,
        steps: [
          {
            tool: "platform_request",
            args: { method: "GET", path: "/products/1" },
          },
        ],
      },
      good,
      { confirmed: true },
    ],
    "wrong tool name": [
      plan,
      [makeTool({ name: "platform_request" })],
      { confirmed: true },
    ],
    "functions.-prefixed ok name still needs done": [
      plan,
      [
        makeTool({
          name: "functions.import_generated_file",
          status: "running",
        }),
      ],
      { confirmed: true },
    ],
    "running tool": [
      plan,
      [makeTool({ status: "running", result: null })],
      { confirmed: true },
    ],
    "interrupted tool": [
      plan,
      [makeTool({ status: "interrupted", summary: "interrupted" })],
      { confirmed: true },
    ],
    "stopped tool": [
      plan,
      [makeTool({ status: "stopped", summary: "interrupted" })],
      { confirmed: true },
    ],
    "failed summary": [
      plan,
      [makeTool({ summary: "HTTP 500" })],
      { confirmed: true },
    ],
    "errorClass result": [
      plan,
      [
        makeTool({
          result: {
            error: "ai_artifact_capture_failed",
            errorClass: "generated_artifact",
            retryable: false,
            outcomeUnknown: true,
          },
        }),
      ],
      { confirmed: true },
    ],
    "outcomeUnknown result": [
      plan,
      [
        makeTool({
          result: {
            error: "ai_artifact_import_unknown",
            errorClass: "generated_artifact",
            outcomeUnknown: true,
          },
        }),
      ],
      { confirmed: true },
    ],
    "outcomeUnknown flag on otherwise valid result": [
      plan,
      [makeTool({ result: { ...RESULT, outcomeUnknown: true } })],
      { confirmed: true },
    ],
    "errorClass flag on otherwise valid result": [
      plan,
      [makeTool({ result: { ...RESULT, errorClass: "generated_artifact" } })],
      { confirmed: true },
    ],
    "error result": [
      plan,
      [makeTool({ result: { error: "ai_artifact_import_unknown" } })],
      { confirmed: true },
    ],
    "malformed args": [
      { ...plan, steps: [{ tool: "import_generated_file", args: "{bad" }] },
      good,
      { confirmed: true },
    ],
    "malformed input": [
      plan,
      [makeTool({ input: "{bad" })],
      { confirmed: true },
    ],
    "malformed result": [
      plan,
      [makeTool({ result: "{bad" })],
      { confirmed: true },
    ],
    "nested preview link not trusted": [
      plan,
      [makeTool({ result: { stdout: JSON.stringify(RESULT) } })],
      { confirmed: true },
    ],
    "fileId zero": [
      plan,
      [
        makeTool({
          result: {
            ...RESULT,
            fileId: 0,
            downloadUrl: "/api/store-storage/files/0/download",
          },
        }),
      ],
      { confirmed: true },
    ],
    "fileId negative": [
      plan,
      [makeTool({ result: { ...RESULT, fileId: -1 } })],
      { confirmed: true },
    ],
    "fileId non-integer": [
      plan,
      [makeTool({ result: { ...RESULT, fileId: 1.5 } })],
      { confirmed: true },
    ],
    "fileId unsafe": [
      plan,
      [makeTool({ result: { ...RESULT, fileId: 2 ** 53 } })],
      { confirmed: true },
    ],
    "artifactId mismatch": [
      plan,
      [
        makeTool({
          input: JSON.stringify({ ...REQUEST, artifactId: "c".repeat(32) }),
        }),
      ],
      { confirmed: true },
    ],
    "sha256 mismatch": [
      plan,
      [makeTool({ result: { ...RESULT, sha256: "d".repeat(64) } })],
      { confirmed: true },
    ],
    "length mismatch": [
      plan,
      [makeTool({ result: { ...RESULT, length: 19 } })],
      { confirmed: true },
    ],
    "fileName mismatch": [
      plan,
      [makeTool({ result: { ...RESULT, fileName: "x.csv" } })],
      { confirmed: true },
    ],
    "destination mismatch": [
      plan,
      [makeTool({ result: { ...RESULT, destination: "product" } })],
      { confirmed: true },
    ],
    "productId mismatch": [
      plan,
      [makeTool({ result: { ...RESULT, productId: 9 } })],
      { confirmed: true },
    ],
    "folderId mismatch": [
      plan,
      [makeTool({ input: JSON.stringify({ ...REQUEST, folderId: 3 }) })],
      { confirmed: true },
    ],
    "external url": [
      plan,
      [
        makeTool({
          result: { ...RESULT, downloadUrl: "https://evil.example/x" },
        }),
      ],
      { confirmed: true },
    ],
    "protocol-relative url": [
      plan,
      [makeTool({ result: { ...RESULT, downloadUrl: "//evil.example/x" } })],
      { confirmed: true },
    ],
    "javascript url": [
      plan,
      [makeTool({ result: { ...RESULT, downloadUrl: "javascript:alert(1)" } })],
      { confirmed: true },
    ],
    "data url": [
      plan,
      [makeTool({ result: { ...RESULT, downloadUrl: "data:text/csv,a" } })],
      { confirmed: true },
    ],
    "wrong route": [
      plan,
      [
        makeTool({
          result: { ...RESULT, downloadUrl: "/api/files/42/download" },
        }),
      ],
      { confirmed: true },
    ],
    "wrong id": [
      plan,
      [
        makeTool({
          result: {
            ...RESULT,
            downloadUrl: "/api/store-storage/files/43/download",
          },
        }),
      ],
      { confirmed: true },
    ],
    "path suffix": [
      plan,
      [
        makeTool({
          result: {
            ...RESULT,
            downloadUrl: "/api/store-storage/files/42/download/extra",
          },
        }),
      ],
      { confirmed: true },
    ],
    "query suffix": [
      plan,
      [
        makeTool({
          result: {
            ...RESULT,
            downloadUrl: "/api/store-storage/files/42/download?x=1",
          },
        }),
      ],
      { confirmed: true },
    ],
    "fragment suffix": [
      plan,
      [
        makeTool({
          result: {
            ...RESULT,
            downloadUrl: "/api/store-storage/files/42/download#f",
          },
        }),
      ],
      { confirmed: true },
    ],
    "missing receipt flags": [
      plan,
      [makeTool({ result: { ...RESULT, idempotent: true, writtenCount: 1 } })],
      { confirmed: true },
    ],
  };
  for (const [label, [p, t, o]] of Object.entries(cases)) {
    it(`yields [] for ${label}`, () => {
      assert.deepEqual(generatedFileDownloads(p, t, o), []);
    });
  }

  it("never emits the same download URL twice on replayed approvals", () => {
    const { plan } = makePlan();
    const dup = {
      ...plan,
      generatedFileImports: [
        plan.generatedFileImports[0],
        { ...plan.generatedFileImports[0] },
      ],
    };
    const files = generatedFileDownloads(dup, good, { confirmed: true });
    assert.equal(files.length, 1);
  });
});
