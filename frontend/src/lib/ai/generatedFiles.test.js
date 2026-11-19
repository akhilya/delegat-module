import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  declaredGeneratedOutputs,
  generatedFileDownloads,
  generatedTemporaryDownloads,
} from "./generatedFiles.js";
import { mapStoredMessage, serializeToolCallsJson } from "./persist.js";
import { generatedFileHistory } from "./chatHistory.js";

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

// ---- Temporary generated outputs (F4) --------------------------------------
const TMP_ID = "c".repeat(32);
const TMP_ID2 = "e".repeat(32);
const TMP_SHA = "d".repeat(64);
const TMP_SHA2 = "f".repeat(64);
const TMP_URL = `/api/ai/chat/generated-files/${TMP_ID}/download`;
const TMP_URL2 = `/api/ai/chat/generated-files/${TMP_ID2}/download`;
const TMP_EXPIRY = "2030-01-01T00:00:00Z";

function tempOutputs(second = false) {
  return [
    { fileName: "r.csv", contentType: "text/csv" },
    ...(second
      ? [{ fileName: "b.bin", contentType: "application/octet-stream" }]
      : []),
  ];
}
function tempMeta(over = {}) {
  return {
    artifactId: TMP_ID,
    fileName: "r.csv",
    contentType: "text/csv",
    length: 3,
    sha256: TMP_SHA,
    expiresAtUtc: TMP_EXPIRY,
    downloadUrl: TMP_URL,
    ...over,
  };
}
function tempPlan({ code = "print(1)", outputs, steps } = {}) {
  return {
    proposalId: "prop-t",
    summary: "s",
    steps: steps || [
      {
        tool: "run_python",
        description: "capture",
        args: { code, outputs: outputs ?? tempOutputs() },
      },
    ],
  };
}
function tempTool({
  status = "done",
  input,
  result,
  name = "run_python",
} = {}) {
  return {
    id: "s1",
    name,
    status,
    input:
      input === undefined
        ? JSON.stringify({ code: "print(1)", outputs: tempOutputs() })
        : input,
    result:
      result === undefined
        ? {
            exitCode: 0,
            stdout: "",
            stderr: "",
            error: null,
            artifacts: [tempMeta()],
          }
        : result,
  };
}

describe("declaredGeneratedOutputs", () => {
  it("normalizes declared outputs of a one-step run_python plan", () => {
    const plan = tempPlan({
      outputs: [{ fileName: "R.CSV", contentType: "Text/CSV" }],
    });
    assert.deepEqual(declaredGeneratedOutputs(plan), [
      { fileName: "R.CSV", contentType: "text/csv" },
    ]);
  });

  it("accepts JSON-string args and defaults the content type", () => {
    const plan = tempPlan({
      steps: [
        {
          tool: "functions.run_python",
          args: JSON.stringify({
            code: "x",
            outputs: [{ fileName: "a.csv" }],
          }),
        },
      ],
    });
    assert.deepEqual(declaredGeneratedOutputs(plan), [
      { fileName: "a.csv", contentType: "application/octet-stream" },
    ]);
  });

  it("accepts a valid emoji pair in the file name", () => {
    const plan = tempPlan({
      outputs: [{ fileName: "итог 😀.csv", contentType: "text/csv" }],
    });
    assert.deepEqual(declaredGeneratedOutputs(plan), [
      { fileName: "итог 😀.csv", contentType: "text/csv" },
    ]);
  });

  it("rejects malformed declarations", () => {
    const bad = [
      tempPlan({ steps: [] }), // no steps
      tempPlan({ outputs: [] }), // empty declaration
      tempPlan({
        outputs: [
          { fileName: "a.csv" },
          { fileName: "b.csv" },
          { fileName: "c.csv" },
          { fileName: "d.csv" },
          { fileName: "e.csv" },
        ],
      }), // > 4
      tempPlan({ outputs: [{ fileName: "a.csv" }, { fileName: "a.csv" }] }),
      tempPlan({ outputs: [{ fileName: "../evil.csv" }] }),
      tempPlan({ outputs: [{ fileName: "a/b.csv" }] }),
      tempPlan({ outputs: [{ fileName: " a.csv" }] }),
      tempPlan({ outputs: [{ fileName: "a.csv", extra: 1 }] }),
      tempPlan({ outputs: [{ contentType: "text/csv" }] }),
      tempPlan({ outputs: [{ fileName: "a.csv", contentType: " text/csv" }] }),
      tempPlan({ outputs: [{ fileName: "a.csv", contentType: null }] }), // explicit null rejects, omitted defaults
      tempPlan({ outputs: [{ fileName: "a.csv", contentType: 5 }] }),
      tempPlan({ outputs: [{ fileName: "a\u0085.csv" }] }), // C1 control
      tempPlan({ outputs: [{ fileName: "a\uD800.csv" }] }), // lone surrogate
      tempPlan({
        steps: [
          { tool: "run_python", args: { code: "x", outputs: tempOutputs() } },
          { tool: "run_python", args: { code: "x" } },
        ],
      }), // two steps
      tempPlan({
        steps: [
          {
            tool: "platform_request",
            args: { code: "x", outputs: tempOutputs() },
          },
        ],
      }),
    ];
    for (const plan of bad)
      assert.deepEqual(declaredGeneratedOutputs(plan), []);
  });
});

describe("generatedTemporaryDownloads", () => {
  it("binds a completed multi-file capture to canonical temporary routes", () => {
    const plan = tempPlan({ outputs: tempOutputs(true) });
    const tool = tempTool({
      input: JSON.stringify({ code: "print(1)", outputs: tempOutputs(true) }),
      result: {
        exitCode: 0,
        stdout: "",
        stderr: "",
        error: null,
        artifacts: [
          tempMeta(),
          tempMeta({
            artifactId: TMP_ID2,
            fileName: "b.bin",
            contentType: "application/octet-stream",
            length: 4,
            sha256: TMP_SHA2,
            downloadUrl: TMP_URL2,
          }),
        ],
      },
    });
    const files = generatedTemporaryDownloads(plan, [tool], {
      confirmed: true,
    });
    assert.equal(files.length, 2);
    assert.equal(files[0].kind, "temporary");
    assert.equal(files[0].downloadUrl, TMP_URL);
    assert.equal(files[1].downloadUrl, TMP_URL2);
    assert.equal(files[1].fileName, "b.bin");
  });

  it("accepts the script fallback for an explicit code:null", () => {
    // Mirrors the host: an explicit code:null falls back to script, while a
    // missing code does not.
    const plan = tempPlan({
      steps: [
        {
          tool: "run_python",
          args: {
            code: null,
            script: "print(1)",
            outputs: tempOutputs(),
          },
        },
      ],
    });
    const tool = tempTool({
      input: { code: null, script: "print(1)", outputs: tempOutputs() },
    });
    assert.equal(
      generatedTemporaryDownloads(plan, [tool], { confirmed: true }).length,
      1,
    );
  });

  it("binds a truncated root capture envelope after reload", () => {
    // The production shape after AgentToolResult.TruncateForModel: the root
    // verdict stays beside the promoted artifacts; important/preview is noise.
    const plan = tempPlan();
    const tool = tempTool({
      result: {
        truncated: true,
        originalLength: 90_000,
        important: {
          exitCode: 1,
          error: "nested lie",
          stdout: "x".repeat(500),
        },
        artifacts: [tempMeta()],
        exitCode: 0,
        error: null,
        preview: `{"exitCode":1,"artifacts":[{"artifactId":"${TMP_ID2}"}]}`,
      },
    });
    const message = {
      role: "assistant",
      content: "",
      toolCalls: [tool],
      timeline: [{ type: "tool", id: "s1" }],
      plan,
      planConfirmed: true,
      planExecuted: true,
      planExecStart: 0,
      messageId: "srv-m2",
    };
    const restored = mapStoredMessage({
      dbId: 6,
      role: "assistant",
      content: "",
      toolCallsJson: serializeToolCallsJson(message),
    });
    const execTools = restored.timeline
      .slice(restored.planExecStart)
      .filter((item) => item.type === "tool" && item.tool)
      .map((item) => item.tool);
    const files = generatedTemporaryDownloads(restored.plan, execTools, {
      confirmed: restored.planConfirmed,
    });
    assert.equal(files.length, 1);
    assert.equal(files[0].downloadUrl, TMP_URL);
  });

  it("returns an already-expired descriptor for the disabled UI state", () => {
    const tool = tempTool({
      result: {
        exitCode: 0,
        stdout: "",
        stderr: "",
        error: null,
        artifacts: [tempMeta({ expiresAtUtc: "2000-01-01T00:00:00Z" })],
      },
    });
    const files = generatedTemporaryDownloads(tempPlan(), [tool], {
      confirmed: true,
    });
    assert.equal(files.length, 1);
    assert.equal(files[0].expiresAtUtc, "2000-01-01T00:00:00Z");
  });

  it("keeps a complete capture despite a later interrupted review tool", () => {
    const later = {
      id: "s2",
      name: "platform_request",
      status: "interrupted",
      input: JSON.stringify({ method: "GET", path: "/products/1" }),
      result: { status: 200 },
    };
    // The binder reads only toolCalls[0]: a later interrupted tool does not
    // retract verified handles.
    const files = generatedTemporaryDownloads(tempPlan(), [tempTool(), later], {
      confirmed: true,
    });
    assert.equal(files.length, 1);
  });

  it("round-trips through persist serialize/hydrate", () => {
    const plan = tempPlan();
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
      toolCalls: [proposalRead, tempTool()],
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
    const files = generatedTemporaryDownloads(restored.plan, execTools, {
      confirmed: restored.planConfirmed,
    });
    assert.equal(files.length, 1);
    assert.equal(files[0].downloadUrl, TMP_URL);
  });
});

describe("generatedTemporaryDownloads negatives", () => {
  const plan = tempPlan();
  const good = tempTool();
  const goodResult = () => JSON.parse(JSON.stringify(good.result));
  const withArtifacts = (mutate) => {
    const result = goodResult();
    mutate(result);
    return tempTool({ result });
  };
  const cases = {
    "unconfirmed proposal": [plan, [good], {}],
    "pending recovery (no confirmed flag)": [
      plan,
      [good],
      { confirmed: false },
    ],
    "missing proposalId": [
      { ...plan, proposalId: undefined },
      [good],
      { confirmed: true },
    ],
    "wrong tool name": [
      plan,
      [tempTool({ name: "platform_request" })],
      { confirmed: true },
    ],
    "running tool": [
      plan,
      [tempTool({ status: "running" })],
      { confirmed: true },
    ],
    "interrupted tool": [
      plan,
      [tempTool({ status: "interrupted" })],
      { confirmed: true },
    ],
    "nonzero exit code": [
      plan,
      [withArtifacts((r) => (r.exitCode = 3))],
      { confirmed: true },
    ],
    "error string": [
      plan,
      [withArtifacts((r) => (r.error = "boom"))],
      { confirmed: true },
    ],
    "error object": [
      plan,
      [withArtifacts((r) => (r.error = { detail: "boom" }))],
      { confirmed: true },
    ],
    errorClass: [
      plan,
      [withArtifacts((r) => (r.errorClass = "generated_artifact"))],
      { confirmed: true },
    ],
    "errorClass object": [
      plan,
      [withArtifacts((r) => (r.errorClass = { detail: "boom" }))],
      { confirmed: true },
    ],
    outcomeUnknown: [
      plan,
      [withArtifacts((r) => (r.outcomeUnknown = true))],
      { confirmed: true },
    ],
    "non-bool outcomeUnknown": [
      plan,
      [withArtifacts((r) => (r.outcomeUnknown = "yes"))],
      { confirmed: true },
    ],
    isError: [
      plan,
      [withArtifacts((r) => (r.isError = true))],
      { confirmed: true },
    ],
    "failed status": [
      plan,
      [withArtifacts((r) => (r.status = "failed"))],
      { confirmed: true },
    ],
    "bare script-only args": [
      tempPlan({
        steps: [
          {
            tool: "run_python",
            args: { script: "print(1)", outputs: tempOutputs() },
          },
        ],
      }),
      [
        tempTool({
          input: { script: "print(1)", outputs: tempOutputs() },
        }),
      ],
      { confirmed: true },
    ],
    "mismatched input code": [
      plan,
      [
        tempTool({
          input: JSON.stringify({
            code: "print(2)",
            outputs: tempOutputs(),
          }),
        }),
      ],
      { confirmed: true },
    ],
    "mismatched input outputs": [
      plan,
      [
        tempTool({
          input: JSON.stringify({
            code: "print(1)",
            outputs: [{ fileName: "other.csv", contentType: "text/csv" }],
          }),
        }),
      ],
      { confirmed: true },
    ],
    "missing artifacts": [
      plan,
      [withArtifacts((r) => delete r.artifacts)],
      { confirmed: true },
    ],
    "wrong artifact count": [
      plan,
      [
        withArtifacts((r) =>
          r.artifacts.push(
            tempMeta({ artifactId: TMP_ID2, downloadUrl: TMP_URL2 }),
          ),
        ),
      ],
      { confirmed: true },
    ],
    "name mismatch": [
      plan,
      [withArtifacts((r) => (r.artifacts[0].fileName = "evil.csv"))],
      { confirmed: true },
    ],
    "MIME mismatch": [
      plan,
      [withArtifacts((r) => (r.artifacts[0].contentType = "text/html"))],
      { confirmed: true },
    ],
    "missing result MIME": [
      plan,
      [withArtifacts((r) => delete r.artifacts[0].contentType)],
      { confirmed: true },
    ],
    "invalid sha256 format": [
      plan,
      [withArtifacts((r) => (r.artifacts[0].sha256 = "not-hex!"))],
      { confirmed: true },
    ],
    "negative length": [
      plan,
      [withArtifacts((r) => (r.artifacts[0].length = -1))],
      { confirmed: true },
    ],
    "over-limit length": [
      plan,
      [withArtifacts((r) => (r.artifacts[0].length = 33 * 1024 * 1024))],
      { confirmed: true },
    ],
    "duplicate artifact id": [
      tempPlan({ outputs: tempOutputs(true) }),
      [
        tempTool({
          input: JSON.stringify({
            code: "print(1)",
            outputs: tempOutputs(true),
          }),
          result: {
            exitCode: 0,
            stdout: "",
            stderr: "",
            error: null,
            artifacts: [
              tempMeta(),
              tempMeta({
                fileName: "b.bin",
                contentType: "application/octet-stream",
              }),
            ],
          },
        }),
      ],
      { confirmed: true },
    ],
    "malformed expiry": [
      plan,
      [withArtifacts((r) => (r.artifacts[0].expiresAtUtc = "not-a-date"))],
      { confirmed: true },
    ],
    "missing downloadUrl (old metadata)": [
      plan,
      [withArtifacts((r) => delete r.artifacts[0].downloadUrl)],
      { confirmed: true },
    ],
    "foreign downloadUrl": [
      plan,
      [
        withArtifacts(
          (r) => (r.artifacts[0].downloadUrl = "https://evil.example/x"),
        ),
      ],
      { confirmed: true },
    ],
    "suffixed downloadUrl": [
      plan,
      [withArtifacts((r) => (r.artifacts[0].downloadUrl = `${TMP_URL}?x=1`))],
      { confirmed: true },
    ],
    "wrong artifact id in URL": [
      plan,
      [
        withArtifacts(
          (r) =>
            (r.artifacts[0].downloadUrl = `/api/ai/chat/generated-files/${TMP_ID2}/download`),
        ),
      ],
      { confirmed: true },
    ],
    "nested stdout envelope spoof": [
      plan,
      [
        withArtifacts((r) => {
          r.artifacts = [];
          r.stdout = JSON.stringify({ artifacts: [tempMeta()] });
        }),
      ],
      { confirmed: true },
    ],
    "truncated envelope missing root verdict": [
      plan,
      [
        tempTool({
          result: {
            truncated: true,
            originalLength: 90_000,
            // A nested important.exitCode or a preview string never counts.
            important: { exitCode: 0, error: null },
            artifacts: [tempMeta()],
            preview: `{"exitCode":0,"artifacts":[{"artifactId":"${TMP_ID}"}]}`,
          },
        }),
      ],
      { confirmed: true },
    ],
  };
  for (const [label, [p, t, o]] of Object.entries(cases)) {
    it(`yields [] for ${label}`, () => {
      assert.deepEqual(generatedTemporaryDownloads(p, t, o), []);
    });
  }
});

describe("generatedFileHistory", () => {
  const hydrate = (message) =>
    mapStoredMessage({
      dbId: 9,
      role: "assistant",
      content: message.content ?? "",
      toolCallsJson: serializeToolCallsJson(message),
    });

  it("preserves verified artifact metadata through serialize/hydrate", () => {
    const message = {
      role: "assistant",
      content: "",
      toolCalls: [tempTool()],
      timeline: [{ type: "tool", id: "s1" }],
      plan: tempPlan(),
      planConfirmed: true,
      planExecuted: true,
      planExecStart: 0,
    };
    const history = generatedFileHistory(hydrate(message));
    assert.ok(history);
    assert.equal(history.savedFiles.length, 0);
    const file = history.artifacts[0];
    assert.equal(file.artifactId, TMP_ID);
    assert.equal(file.sha256, TMP_SHA);
    assert.equal(file.length, 3);
    assert.equal(file.expiresAtUtc, TMP_EXPIRY);
    assert.equal(file.downloadUrl, TMP_URL);
    // The serialized handle carries metadata only - never stdout or code.
    const serialized = JSON.stringify(history);
    assert.equal(serialized.includes("stdout"), false);
    assert.equal(serialized.includes("print("), false);
  });

  it("does not shift binding when a pre-proposal read precedes planExecStart", () => {
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
      toolCalls: [proposalRead, tempTool()],
      timeline: [
        { type: "text", text: "prepared" },
        { type: "tool", id: "r0" },
        { type: "tool", id: "s1" },
      ],
      plan: tempPlan(),
      planConfirmed: true,
      planExecuted: true,
      planExecStart: 2,
    };
    const history = generatedFileHistory(hydrate(message));
    assert.ok(history);
    assert.equal(history.artifacts[0].artifactId, TMP_ID);
  });

  it("returns null for unconfirmed, failed, or stopped turns", () => {
    const base = {
      role: "assistant",
      content: "",
      toolCalls: [tempTool()],
      timeline: [{ type: "tool", id: "s1" }],
      plan: tempPlan(),
      planExecuted: true,
      planExecStart: 0,
    };
    assert.equal(generatedFileHistory(hydrate(base)), null);
    assert.equal(
      generatedFileHistory(
        hydrate({ ...base, planConfirmed: true, planFailed: true }),
      ),
      null,
    );
    assert.equal(
      generatedFileHistory(
        hydrate({ ...base, planConfirmed: true, turnStopped: true }),
      ),
      null,
    );
  });

  it("returns null when planExecStart is missing or outside the timeline", () => {
    const base = {
      role: "assistant",
      content: "",
      toolCalls: [tempTool()],
      timeline: [{ type: "tool", id: "s1" }],
      plan: tempPlan(),
      planConfirmed: true,
      planExecuted: true,
    };
    assert.equal(generatedFileHistory(hydrate(base)), null);
    assert.equal(
      generatedFileHistory(hydrate({ ...base, planExecStart: 9 })),
      null,
    );
  });

  it("returns null for a foreign artifact URL", () => {
    const message = {
      role: "assistant",
      content: "",
      toolCalls: [
        tempTool({
          result: {
            exitCode: 0,
            stdout: "",
            stderr: "",
            error: null,
            artifacts: [
              tempMeta({ downloadUrl: "https://evil.example/files/x" }),
            ],
          },
        }),
      ],
      timeline: [{ type: "tool", id: "s1" }],
      plan: tempPlan(),
      planConfirmed: true,
      planExecuted: true,
      planExecStart: 0,
    };
    assert.equal(generatedFileHistory(hydrate(message)), null);
  });

  it("returns saved files without promoting a temporary artifact", () => {
    const { plan } = makePlan();
    const message = {
      role: "assistant",
      content: "",
      toolCalls: [makeTool()],
      timeline: [{ type: "tool", id: "s1" }],
      plan,
      planConfirmed: true,
      planExecuted: true,
      planExecStart: 0,
    };
    const history = generatedFileHistory(hydrate(message));
    assert.ok(history);
    assert.equal(history.artifacts.length, 0);
    assert.equal(history.savedFiles.length, 1);
    assert.equal(history.savedFiles[0].fileId, 42);
    assert.equal(
      history.savedFiles[0].downloadUrl,
      "/api/store-storage/files/42/download",
    );
  });
});
