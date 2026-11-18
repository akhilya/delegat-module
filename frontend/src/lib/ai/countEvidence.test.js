import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { planCountEstimate, stepCountEvidence } from "./countEvidence.js";

const unknown = { value: null, accuracy: "unknown", unit: "records" };
const allUnknown = { read: unknown, matched: unknown, written: unknown };
const exact = (value, unit) => ({ value, accuracy: "exact", unit });
const args = { method: "GET", path: "/products?pageSize=100" };
const plan = {
  proposalId: "proposal-1",
  affectedCount: 99,
  affectedCountAccuracy: "estimated",
  steps: [{ tool: "platform_request", args }],
};
const tool = {
  name: "platform_request",
  status: "done",
  input: JSON.stringify(args),
  result: { items: Array(12).fill({}), totalCount: 75 },
  counts: {
    version: 1,
    source: "platform_request",
    read: exact(12, "products"),
    matched: exact(75, "products"),
    written: exact(0, "records"),
  },
};
const read = (p = plan, t = [tool], i = 0, options = { confirmed: true }) =>
  stepCountEvidence(p, t, i, options);

describe("planCountEstimate", () => {
  it("never promotes model estimates to observed results, including old histories", () => {
    assert.deepEqual(planCountEstimate(plan), {
      value: 99,
      accuracy: "estimated",
    });
    assert.deepEqual(planCountEstimate({ affectedCount: 75 }), {
      value: 75,
      accuracy: "estimated",
    });
    assert.deepEqual(planCountEstimate({ affectedCount: 0 }), {
      value: null,
      accuracy: "unknown",
    });
    assert.deepEqual(
      planCountEstimate({
        affectedCount: 0,
        affectedCountAccuracy: "estimated",
      }),
      { value: 0, accuracy: "estimated" },
    );
  });
  for (const count of [
    undefined,
    null,
    "75",
    -1,
    0.5,
    Number.MAX_SAFE_INTEGER + 1,
  ]) {
    it(`treats malformed or absent estimate ${count} as unknown`, () => {
      assert.deepEqual(planCountEstimate({ affectedCount: count }), {
        value: null,
        accuracy: "unknown",
      });
    });
  }
  it("rejects an asserted exact accuracy on model-owned planned impact", () => {
    for (const accuracy of ["exact", "unknown", null, {}])
      assert.equal(
        planCountEstimate({
          affectedCount: 75,
          affectedCountAccuracy: accuracy,
        }).accuracy,
        "unknown",
      );
  });
});

describe("stepCountEvidence", () => {
  it("keeps returned, matched and zero writes distinct from planned impact", () => {
    assert.deepEqual(read(), {
      read: exact(12, "products"),
      matched: exact(75, "products"),
      written: exact(0, "records"),
    });
    assert.equal(planCountEstimate(plan).value, 99);
  });
  it("compares JSON argument objects independent of key ordering", () => {
    assert.deepEqual(
      read(plan, [
        { ...tool, input: { path: args.path, method: args.method } },
      ]),
      read(),
    );
  });
  it("does not sum repeated or overlapping steps", () => {
    const two = { ...plan, steps: [plan.steps[0], plan.steps[0]] };
    assert.deepEqual(read(two, [tool, tool], 0), read(two, [tool, tool], 1));
    assert.equal(read(two, [tool, tool], 1).matched.value, 75);
  });
  it("preserves zero writes for a receipt replay and does not borrow previous create", () => {
    const p = {
      proposalId: "import",
      steps: [{ tool: "import_generated_file", args: { artifactId: "a" } }],
    };
    const receipt = {
      name: "import_generated_file",
      status: "done",
      input: { artifactId: "a" },
      counts: {
        version: 1,
        source: "import_generated_file",
        read: unknown,
        matched: unknown,
        written: exact(0, "files"),
      },
    };
    assert.equal(read(p, [receipt]).written.value, 0);
    assert.equal(read(p, [receipt]).written.accuracy, "exact");
  });
  it("does not use raw result, stdout or old plan count as missing event evidence", () => {
    const noMetadata = {
      ...tool,
      counts: undefined,
      result: {
        ...tool.result,
        counts: tool.counts,
        writtenCount: 99,
        stdout: JSON.stringify({ counts: tool.counts }),
      },
    };
    assert.deepEqual(read(plan, [noMetadata]), allUnknown);
  });
  const negatives = {
    "unconfirmed recovery": [plan, [tool], 0, { confirmed: false }],
    "missing proposal identity": [{ ...plan, proposalId: undefined }, [tool]],
    "running tool": [plan, [{ ...tool, status: "running" }]],
    "interrupted tool": [plan, [{ ...tool, status: "stopped" }]],
    "mismatched arguments": [
      plan,
      [{ ...tool, input: { ...args, path: "/products/1" } }],
    ],
    "mismatched tool": [plan, [{ ...tool, name: "run_python" }]],
    "out-of-range step": [plan, [tool], 1],
    "wrong version": [
      plan,
      [{ ...tool, counts: { ...tool.counts, version: 2 } }],
    ],
    "foreign source": [
      plan,
      [{ ...tool, counts: { ...tool.counts, source: "sql_readonly" } }],
    ],
    "estimated observed result": [
      plan,
      [
        {
          ...tool,
          counts: {
            ...tool.counts,
            written: { ...exact(3, "files"), accuracy: "estimated" },
          },
        },
      ],
    ],
    "negative value": [
      plan,
      [{ ...tool, counts: { ...tool.counts, written: exact(-1, "files") } }],
    ],
    "unsafe value": [
      plan,
      [
        {
          ...tool,
          counts: {
            ...tool.counts,
            matched: exact(Number.MAX_SAFE_INTEGER + 1, "products"),
          },
        },
      ],
    ],
    "null exact value": [
      plan,
      [{ ...tool, counts: { ...tool.counts, written: exact(null, "files") } }],
    ],
    "unknown with a plausible number": [
      plan,
      [
        {
          ...tool,
          counts: {
            ...tool.counts,
            written: { value: 3, accuracy: "unknown", unit: "files" },
          },
        },
      ],
    ],
    "arbitrary unit": [
      plan,
      [{ ...tool, counts: { ...tool.counts, written: exact(1, "secrets") } }],
    ],
  };
  for (const [
    label,
    [p, tools, index = 0, options = { confirmed: true }],
  ] of Object.entries(negatives))
    it(`fails closed for ${label}`, () => {
      assert.deepEqual(read(p, tools, index, options), allUnknown);
    });
});
