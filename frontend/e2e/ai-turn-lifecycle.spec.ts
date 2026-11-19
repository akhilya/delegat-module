import { expect, test, type Page } from "@playwright/test";

/**
 * Canonical turn-lifecycle journeys: a chat-phase failed tool is never
 * execution evidence, accepted form replies stay bound to their owner row,
 * and only one active interaction may submit. The app runs against fully
 * mocked APIs; canonical SSE legs arrive through a real ReadableStream served
 * by window.fetch interception, and the persisted-history GET is controllable.
 */

const sessionId = "turn-lifecycle-test";
const MSG_CHAT = "m-chat";
const MSG_PLAN = "m-plan";
const MSG_REVIEW1 = "m-review-1";
const MSG_REVIEW2 = "m-review-2";

const userPrompt =
  "Create a product, fill its attributes, then generate a table with its data.";
const followUp =
  "Create product in accessory named Ork mug, SKU OM-1, price 9, quantity 2. Then generate its data table.";
const formAnswers = {
  category: "accessory",
  name: "Ork mug",
  sku: "OM-1",
  price: "9",
  quantity: "2",
};

const sqlFailedTool = {
  id: "sql-1",
  name: "sql_readonly",
  status: "done",
  statusLabel: "Query failed",
  summary: "Query rejected",
  result: { error: "ORDER BY column is not allowlisted" },
};
const invalidFormTool = {
  id: "form-1",
  name: "ask_form",
  status: "done",
  statusLabel: "Form rejected",
  summary: "Form rejected",
  result: {
    error: "unknown_follow_up_fields",
    unknownFields: ["sku", "price", "quantity"],
    form: false,
  },
};
const formResult = {
  form: true,
  kind: "questions",
  title: "New product",
  followUp:
    "Create product in {category} named {name}, SKU {sku}, price {price}, quantity {quantity}. Then generate its data table.",
  questions: [
    {
      key: "category",
      title: "Category",
      type: "single",
      choices: [
        { value: "figure", label: "Figure" },
        { value: "accessory", label: "Accessory" },
      ],
    },
    { key: "name", title: "Product name", type: "text" },
    { key: "sku", title: "SKU", type: "text" },
    { key: "price", title: "Price", type: "text" },
    { key: "quantity", title: "Quantity", type: "text" },
  ],
};
const validFormTool = {
  id: "form-2",
  name: "ask_form",
  status: "done",
  statusLabel: "Form presented",
  summary: "Form presented",
  result: formResult,
};

const plan1 = {
  proposalId: "plan-1",
  summary: "Create the product from the submitted answers.",
  steps: [
    {
      tool: "platform_request",
      description: "Create Mug",
      args: {
        method: "POST",
        path: "/products",
        body: { name: "Ork mug" },
      },
    },
  ],
};
const execTool = {
  id: "exec-1",
  name: "platform_request",
  status: "done",
  statusLabel: "Done",
  summary: "Step completed",
  result: { success: true, id: 9001 },
};

const csvBody = "id,name\n9001,Ork mug\n";
const artifactId = "a".repeat(32);
const artifactUrl = `/api/ai/chat/generated-files/${artifactId}/download`;
const pythonArgs = {
  code: "with open('table.csv', 'w', encoding='utf-8', newline='') as output:\n    output.write('id,name\\n9001,Ork mug\\n')",
  outputs: [{ fileName: "table.csv", contentType: "text/csv" }],
};
const artifact = {
  artifactId,
  fileName: "table.csv",
  contentType: "text/csv",
  length: 21,
  sha256:
    "20cafd31b68129fcc0f1a2ad4acfd1e9fdd9f43ad1207a85138373c351fe1aee",
  expiresAtUtc: "2030-01-01T00:00:00Z",
  downloadUrl: artifactUrl,
};
const plan2 = {
  proposalId: "plan-2",
  summary: "Generate a table from the existing product; do not create it again.",
  steps: [
    {
      tool: "run_python",
      description: "Generate the data table",
      args: pythonArgs,
    },
  ],
};
const generatedTool = {
  id: "gen-1",
  name: "run_python",
  status: "done",
  statusLabel: "Done",
  input: JSON.stringify(pythonArgs),
  summary: "Table generated",
  result: {
    exitCode: 0,
    stdout: "",
    stderr: "",
    error: null,
    artifacts: [artifact],
  },
};

const sse = (type: string, data: unknown, meta: Record<string, unknown>) =>
  `data: ${JSON.stringify({ type, data, ...meta })}\n\n`;
const meta = (
  runId: string,
  messageId: string,
  sequence: number,
  phase: string,
  extra: Record<string, unknown> = {},
) => ({ runId, messageId, sequence, phase, ...extra });

const runningTool = (tool: Record<string, unknown>) => ({
  id: tool.id,
  name: tool.name,
  status: "running",
  input: (tool.input as string) || "{}",
});

const toolEvents = (
  tool: Record<string, unknown>,
  runId: string,
  messageId: string,
  sequence: number,
  phase: string,
) => [
  sse(
    "tool",
    runningTool(tool),
    meta(runId, messageId, sequence, phase),
  ),
  sse("tool", tool, meta(runId, messageId, sequence + 1, phase)),
];

const legChat = () =>
  [
    sse("message_start", {}, meta("run-chat", MSG_CHAT, 1, "chat", { dbId: 2 })),
    ...toolEvents(sqlFailedTool, "run-chat", MSG_CHAT, 2, "chat"),
    ...toolEvents(invalidFormTool, "run-chat", MSG_CHAT, 4, "chat"),
    ...toolEvents(validFormTool, "run-chat", MSG_CHAT, 6, "chat"),
    sse(
      "done",
      { modelName: "test-model" },
      meta("run-chat", MSG_CHAT, 8, "chat", { dbId: 2 }),
    ),
  ].join("");

const legPlan = () =>
  [
    sse("message_start", {}, meta("run-plan", MSG_PLAN, 1, "chat", { dbId: 4 })),
    sse("plan", plan1, meta("run-plan", MSG_PLAN, 2, "chat", { dbId: 4 })),
    sse(
      "done",
      { modelName: "test-model" },
      meta("run-plan", MSG_PLAN, 3, "chat", { dbId: 4 }),
    ),
  ].join("");

const legInterruptedPlan = () =>
  [
    sse("message_start", {}, meta("run-plan", MSG_PLAN, 1, "chat", { dbId: 4 })),
    sse("plan", plan1, meta("run-plan", MSG_PLAN, 2, "chat", { dbId: 4 })),
    sse(
      "done",
      { interrupted: true },
      meta("run-plan", MSG_PLAN, 3, "chat", { dbId: 4 }),
    ),
  ].join("");

const legExec1 = () =>
  [
    sse(
      "message_start",
      {},
      meta("run-exec-1", MSG_PLAN, 1, "execution", {
        dbId: 4,
        proposalId: "plan-1",
      }),
    ),
    ...toolEvents(execTool, "run-exec-1", MSG_PLAN, 2, "execution"),
    sse(
      "execution_status",
      { status: "completed", total: 1, succeeded: 1, summary: "1 step completed." },
      meta("run-exec-1", MSG_PLAN, 4, "execution"),
    ),
    sse("done", { ok: true }, meta("run-exec-1", MSG_PLAN, 5, "execution")),
    sse(
      "assistant_start",
      { executionProposalId: "plan-1" },
      meta("run-exec-1", MSG_REVIEW1, 6, "review", { dbId: 5 }),
    ),
    sse(
      "text",
      { delta: "The product step finished. Generate the data table next." },
      meta("run-exec-1", MSG_REVIEW1, 7, "review"),
    ),
    sse("plan", plan2, meta("run-exec-1", MSG_REVIEW1, 8, "review")),
    sse("done", {}, meta("run-exec-1", MSG_REVIEW1, 9, "review")),
  ].join("");

const legExec2 = () =>
  [
    sse(
      "message_start",
      {},
      meta("run-exec-2", MSG_REVIEW1, 1, "execution", {
        dbId: 5,
        proposalId: "plan-2",
      }),
    ),
    ...toolEvents(generatedTool, "run-exec-2", MSG_REVIEW1, 2, "execution"),
    sse(
      "execution_status",
      { status: "completed", total: 1, succeeded: 1, summary: "1 step completed." },
      meta("run-exec-2", MSG_REVIEW1, 4, "execution"),
    ),
    sse("done", { ok: true }, meta("run-exec-2", MSG_REVIEW1, 5, "execution")),
    sse(
      "assistant_start",
      { executionProposalId: "plan-2" },
      meta("run-exec-2", MSG_REVIEW2, 6, "review", { dbId: 6 }),
    ),
    sse(
      "text",
      { delta: "The table is ready from the existing product data." },
      meta("run-exec-2", MSG_REVIEW2, 7, "review"),
    ),
    sse("done", {}, meta("run-exec-2", MSG_REVIEW2, 8, "review")),
  ].join("");

const envelope = (over: Record<string, unknown>) =>
  JSON.stringify({ _ui: "assistantTurn", tools: [], timeline: [], ...over });
const toolItem = (tool: Record<string, unknown>) => ({
  type: "tool",
  id: tool.id,
  tool,
});
const textItem = (text: string) => ({ type: "text", text });

// The historical producer bug: the chat turn's tool failures were recorded as
// planFailed even though no plan or execution ever existed.
const chatTurnEnvelope = envelope({
  tools: [sqlFailedTool, invalidFormTool, validFormTool],
  timeline: [
    toolItem(sqlFailedTool),
    toolItem(invalidFormTool),
    toolItem(validFormTool),
  ],
  plan: null,
  planFailed: true,
  messageId: MSG_CHAT,
  runId: "run-chat",
  phase: "chat",
  lastEventSequence: 8,
  messageState: "completed",
});

const formReplyRow = {
  dbId: 3,
  role: "user",
  content: followUp,
  toolCallsJson: JSON.stringify({
    _ui: "formReply",
    formId: "form-2",
    answers: formAnswers,
    assistantMessageId: MSG_CHAT,
    assistantDbId: 2,
  }),
};

const planProposalRow = {
  dbId: 4,
  role: "assistant",
  content: "",
  toolCallsJson: envelope({
    plan: plan1,
    messageId: MSG_PLAN,
    runId: "run-plan",
    phase: "chat",
    lastEventSequence: 3,
    messageState: "completed",
  }),
};

// The same plan row while its execution run is still in flight: confirmed,
// not yet executed, no terminal outcome.
const planRunningRow = {
  dbId: 4,
  role: "assistant",
  content: "",
  toolCallsJson: envelope({
    tools: [execTool],
    timeline: [toolItem(execTool)],
    plan: plan1,
    planConfirmed: true,
    planExecStart: 0,
    messageId: MSG_PLAN,
    runId: "run-exec-1",
    phase: "execution",
    lastEventSequence: 3,
    messageState: "running",
  }),
};

const planExecutedRow = {
  dbId: 4,
  role: "assistant",
  content: "1 step completed.",
  toolCallsJson: envelope({
    tools: [execTool],
    timeline: [toolItem(execTool), textItem("1 step completed.")],
    plan: plan1,
    planConfirmed: true,
    planExecuted: true,
    planExecStart: 0,
    executionOutcome: {
      status: "completed",
      total: 1,
      succeeded: 1,
      summary: "1 step completed.",
    },
    messageId: MSG_PLAN,
    runId: "run-exec-1",
    phase: "execution",
    lastEventSequence: 5,
    messageState: "completed",
  }),
};

const reviewProposalRow = {
  dbId: 5,
  role: "assistant",
  content: "The product step finished. Generate the data table next.",
  toolCallsJson: envelope({
    timeline: [
      textItem("The product step finished. Generate the data table next."),
    ],
    plan: plan2,
    executionProposalId: "plan-1",
    messageId: MSG_REVIEW1,
    runId: "run-exec-1",
    phase: "review",
    lastEventSequence: 9,
    messageState: "completed",
  }),
};

const reviewExecutedRow = {
  dbId: 5,
  role: "assistant",
  content: "The product step finished. Generate the data table next.\n\n1 step completed.",
  toolCallsJson: envelope({
    tools: [generatedTool],
    timeline: [
      textItem("The product step finished. Generate the data table next."),
      toolItem(generatedTool),
      textItem("1 step completed."),
    ],
    plan: plan2,
    planConfirmed: true,
    planExecuted: true,
    planExecStart: 1,
    executionOutcome: {
      status: "completed",
      total: 1,
      succeeded: 1,
      summary: "1 step completed.",
    },
    executionProposalId: "plan-1",
    messageId: MSG_REVIEW1,
    runId: "run-exec-2",
    phase: "execution",
    lastEventSequence: 5,
    messageState: "completed",
  }),
};

const reviewFinalRow = {
  dbId: 6,
  role: "assistant",
  content: "The table is ready from the existing product data.",
  toolCallsJson: envelope({
    timeline: [textItem("The table is ready from the existing product data.")],
    executionProposalId: "plan-2",
    messageId: MSG_REVIEW2,
    runId: "run-exec-2",
    phase: "review",
    lastEventSequence: 8,
    messageState: "completed",
  }),
};

const interruptedPlanRow = {
  dbId: 4,
  role: "assistant",
  content: "",
  toolCallsJson: envelope({
    plan: plan1,
    messageId: MSG_PLAN,
    runId: "run-plan",
    phase: "chat",
    lastEventSequence: 3,
    messageState: "interrupted",
  }),
};

const userRow = { dbId: 1, role: "user", content: userPrompt };
const chatAssistantRow = {
  dbId: 2,
  role: "assistant",
  content: "",
  toolCallsJson: chatTurnEnvelope,
};

const historyPhase: Record<number, object[]> = {
  1: [userRow, chatAssistantRow],
  2: [userRow, chatAssistantRow, formReplyRow, planProposalRow],
  3: [userRow, chatAssistantRow, formReplyRow, planExecutedRow, reviewProposalRow],
  4: [
    userRow,
    chatAssistantRow,
    formReplyRow,
    planExecutedRow,
    reviewExecutedRow,
    reviewFinalRow,
  ],
};

const json = (body: unknown, status = 200) => ({
  status,
  contentType: "application/json",
  body: JSON.stringify(body),
});

async function fixture(
  page: Page,
  options: { messages?: object[] } = {},
) {
  // window.fetch interception drives the streaming endpoints with real
  // ReadableStream bodies; everything else is fulfilled through page.route.
  await page.addInitScript(() => {
    const tlc = ((window as any).__tlc = {
      chatSse: [] as string[],
      execSse: [] as string[],
      streamSse: [] as string[],
      chatBodies: [] as any[],
      execBodies: [] as any[],
      uploads: 0,
      stops: 0,
    });
    const realFetch = window.fetch.bind(window);
    const sseResponse = (body: string) =>
      new Response(
        new ReadableStream({
          start(controller) {
            controller.enqueue(new TextEncoder().encode(body));
            controller.close();
          },
        }),
        { status: 200, headers: { "Content-Type": "text/event-stream" } },
      );
    const jsonResponse = (body: unknown) =>
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    window.fetch = (input: any, init: any) => {
      const url = typeof input === "string" ? input : input?.url || "";
      const path = new URL(url, window.location.origin).pathname;
      const method = init?.method || "GET";
      if (method === "POST" && path === "/api/ai/chat") {
        tlc.chatBodies.push(JSON.parse(String(init.body)));
        return Promise.resolve(sseResponse(tlc.chatSse.shift() || ""));
      }
      if (method === "POST" && path === "/api/ai/execute-plan") {
        tlc.execBodies.push(JSON.parse(String(init.body)));
        return Promise.resolve(sseResponse(tlc.execSse.shift() || ""));
      }
      if (method === "POST" && path.startsWith("/api/ai/chat/attachments")) {
        tlc.uploads += 1;
        return Promise.resolve(jsonResponse({ items: [] }));
      }
      if (method === "POST" && path === "/api/ai/stop") {
        tlc.stops += 1;
        return Promise.resolve(jsonResponse({ stopped: true }));
      }
      if (path === "/api/ai/chat/stream") {
        return Promise.resolve(sseResponse(tlc.streamSse.shift() || ""));
      }
      return realFetch(input, init);
    };
  });

  await page.addInitScript(() => {
    localStorage.setItem("delegat.locale", "en");
    localStorage.setItem("mobile-warning-dismissed", "1");
  });

  const savedBodies: any[] = [];
  const unexpectedWrites: string[] = [];
  let artifactGets = 0;
  const assignedDb = new Map<string, number>();
  let nextDb = 1;
  let phase = 0;
  const historyWaiters: Array<() => void> = [];
  let historyHeld = false;
  let historyMessages = options.messages;
  const persisted = () =>
    historyMessages ?? historyPhase[phase] ?? historyPhase[4];

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url())
      .pathname.replace(/^\/api/, "")
      .replace(/\/+$/, "");
    let body: unknown = {};
    if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
    else if (path === "/auth/me")
      body = {
        id: 1,
        userName: "turn-lifecycle-test",
        isAdmin: true,
        permissions: ["ai.use", "catalog.manage", "files.manage"],
        selectedStoreId: 1,
        aiEnabled: true,
      };
    else if (path === "/health") body = { status: "healthy", version: "1.6.0" };
    else if (path === "/users/me/memberships")
      body = [
        {
          id: 1,
          name: "Test store",
          onboardingCompletedAt: "2026-01-01T00:00:00Z",
        },
      ];
    else if (path === "/stores")
      body = {
        items: [
          {
            id: 1,
            name: "Test store",
            onboardingCompletedAt: "2026-01-01T00:00:00Z",
          },
        ],
        totalCount: 1,
      };
    else if (path === "/products") body = { items: [], totalCount: 0 };
    else if (path === "/ai/models/picker")
      body = [{ id: 1, name: "Test model", isEnabled: true, isDefault: true }];
    else if (path === "/ai/hosted-ai") body = { configured: false };
    else if (path === "/ai/conversations/latest") body = null;
    else if (path === "/ai/conversations" && request.method() === "GET")
      body = { conversations: [] };
    else if (path === `/ai/conversations/${sessionId}`) {
      if (historyHeld) {
        await new Promise<void>((resolve) => historyWaiters.push(resolve));
      }
      body = {
        sessionId,
        title: "Turn lifecycle",
        generating: false,
        messages: persisted(),
      };
    } else if (path === "/ai/conversations/messages") {
      const posted = request.postDataJSON();
      savedBodies.push(posted);
      body = {
        saved: (posted.messages || []).map((m: any) => {
          let dbId = m.dbId;
          if (!Number.isInteger(dbId)) {
            const key = String(m.clientMessageId ?? "");
            dbId = assignedDb.get(key);
            if (dbId == null) {
              const used = new Set<number>([
                ...assignedDb.values(),
                ...(posted.messages || [])
                  .map((x: any) => x.dbId)
                  .filter((x: any) => Number.isInteger(x)),
              ]);
              while (used.has(nextDb)) nextDb++;
              dbId = nextDb++;
              assignedDb.set(key, dbId);
            }
          }
          return { role: m.role, dbId, clientMessageId: m.clientMessageId };
        }),
      };
    } else if (
      request.method() === "POST" &&
      path === `/ai/conversations/${sessionId}/generate-title`
    )
      body = { title: "Turn lifecycle" };
    else if (path === `/ai/chat/generated-files/${artifactId}/download`) {
      artifactGets++;
      await route.fulfill({
        status: 200,
        contentType: "text/csv",
        headers: { "Content-Disposition": 'attachment; filename="table.csv"' },
        body: csvBody,
      });
      return;
    } else if (path === "/settings/branding") body = {};
    else if (request.method() !== "GET")
      unexpectedWrites.push(`${request.method()} ${path}`);
    await route.fulfill(json(body));
  });

  await page.goto("/products");
  await page.waitForFunction(
    () =>
      !!(document.querySelector("#app") as any)?.__vue_app__?.config
        .globalProperties.$store,
  );

  const store = () =>
    page.evaluate(() => {
      return (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store;
    });
  const evaluate = <T>(fn: (arg?: any) => T, arg?: any) =>
    page.evaluate(fn as any, arg);

  return {
    savedBodies,
    unexpectedWrites,
    get artifactGets() {
      return artifactGets;
    },
    setPhase(n: number) {
      phase = n;
    },
    setHistory(messages: object[]) {
      historyMessages = messages;
    },
    holdHistory() {
      historyHeld = true;
    },
    releaseHistory() {
      historyHeld = false;
      for (const resolve of historyWaiters.splice(0)) resolve();
    },
    async openChat() {
      await evaluate((sid: string) => {
        const s = (document.querySelector("#app") as any).__vue_app__.config
          .globalProperties.$store;
        s.commit("aiChat/setSessionId", sid);
        s.commit("aiChat/setOpen", true);
      }, sessionId);
    },
    async loadConversation() {
      await evaluate(async (sid: string) => {
        const s = (document.querySelector("#app") as any).__vue_app__.config
          .globalProperties.$store;
        await s.dispatch("aiChat/loadConversation", sid);
        s.commit("aiChat/setOpen", true);
      }, sessionId);
    },
    async pushChatSse(body: string) {
      await evaluate((b: string) => (window as any).__tlc.chatSse.push(b), body);
    },
    async pushExecSse(body: string) {
      await evaluate((b: string) => (window as any).__tlc.execSse.push(b), body);
    },
    async pushStreamSse(body: string) {
      await evaluate(
        (b: string) => (window as any).__tlc.streamSse.push(b),
        body,
      );
    },
    tlc: () => evaluate(() => (window as any).__tlc),
    store,
    evaluate,
  };
}

async function fillQuestionnaire(page: Page) {
  await page.locator('[data-slot="questionnaire-choice"]', {
    hasText: "Accessory",
  }).click();
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await page.locator('input[name="name"]').fill("Ork mug");
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await page.locator('input[name="sku"]').fill("OM-1");
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await page.locator('input[name="price"]').fill("9");
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await page.locator('input[name="quantity"]').fill("2");
}

for (const width of [1280, 390]) {
  test(`recoverable chat errors keep the clarification form through history sync at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const state = await fixture(page);
    await state.holdHistory();
    await state.pushChatSse(legChat());
    await state.openChat();
    await page.evaluate(async (prompt) => {
      const store = (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store;
      store.dispatch("aiChat/sendMessage", prompt);
    }, userPrompt);

    // The failed read and rejected draft form are tool evidence, not a plan
    // outcome: the repaired form renders while the history GET is held.
    await expect(
      page.locator('[data-slot="questionnaire-choice"]', {
        hasText: "Accessory",
      }),
    ).toBeVisible();
    await expect(page.getByTestId("ai-plan")).toHaveCount(0);

    // Filling and advancing stay usable during synchronization; the final
    // submit waits, and Enter cannot sneak the form into an answered state.
    await fillQuestionnaire(page);
    const submit = page.getByRole("button", { name: "Continue", exact: true });
    await expect(submit).toBeVisible();
    await expect(submit).toBeDisabled();
    await page.locator('input[name="quantity"]').press("Enter");
    let tlc = await state.tlc();
    expect(tlc.chatBodies).toHaveLength(1);
    expect(tlc.execBodies).toHaveLength(0);

    // A pending composer draft is not consumed by the form reply.
    await page.evaluate(() => {
      const store = (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store;
      store.commit("aiChat/setAttachedFiles", [
        { file: new File(["x"], "note.txt"), relativePath: "note.txt" },
      ]);
    });

    // Release the stale incident snapshot: planFailed on a planless chat row
    // normalizes for presentation and must not hide the form or wipe typing.
    state.setPhase(1);
    state.releaseHistory();
    await expect(page.locator('input[name="quantity"]')).toHaveValue("2");
    await expect(submit).toBeEnabled();

    await state.pushChatSse(legPlan());
    state.setPhase(2);
    await submit.click();

    // Exactly one hidden follow-up, bound to the canonical owner row.
    await expect(page.getByTestId("ai-plan")).toBeVisible();
    tlc = await state.tlc();
    expect(tlc.chatBodies).toHaveLength(2);
    expect(tlc.execBodies).toHaveLength(0);
    expect(tlc.uploads).toBe(0);
    expect(tlc.chatBodies[1].sessionId).toBe(sessionId);
    expect(tlc.chatBodies[1].messages.at(-1).content).toBe(followUp);
    const replyPost = state.savedBodies
      .flatMap((body) => body.messages)
      .find((m: any) => m.toolCallsJson?.includes("formReply"));
    expect(replyPost).toBeTruthy();
    const replyMeta = JSON.parse(replyPost.toolCallsJson);
    expect(replyMeta._ui).toBe("formReply");
    expect(replyMeta.formId).toBe("form-2");
    expect(replyMeta.assistantMessageId).toBe(MSG_CHAT);
    expect(replyMeta.assistantDbId).toBe(2);
    const attachedCount = await page.evaluate(
      () =>
        (document.querySelector("#app") as any).__vue_app__.config
          .globalProperties.$store.state.aiChat.attachedFiles.length,
    );
    expect(attachedCount).toBe(1);

    // The submitted form is a read-only recap on its original row; the fresh
    // plan arrives on a NEW assistant message.
    await expect(page.locator('input[name="name"]')).toHaveCount(0);
    await expect(
      page.getByText("Ork mug", { exact: true }).first(),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Execute", exact: true }),
    ).toBeEnabled();

    // Reload: the recap stays attached to the original turn.
    state.setPhase(2);
    await state.loadConversation();
    await expect(
      page.getByText("Ork mug", { exact: true }).first(),
    ).toBeVisible();
    await expect(page.locator('input[name="name"]')).toHaveCount(0);
    await expect(
      page.getByRole("button", { name: "Execute", exact: true }),
    ).toBeEnabled();

    // Confirm the new plan: execution binds the proposal row, the review
    // proposes a fresh continuation plan.
    await state.pushExecSse(legExec1());
    state.setPhase(3);
    await page.evaluate(() => {
      const store = (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store;
      store.dispatch("aiChat/confirmPlan", "plan-1");
    });
    await expect(
      page.getByTestId("ai-plan").first().getByRole("status"),
    ).toHaveText("Done");
    await expect(page.getByTestId("ai-plan")).toHaveCount(2);
    await expect(
      page.getByRole("button", { name: "Execute", exact: true }),
    ).toBeEnabled();
    tlc = await state.tlc();
    expect(tlc.execBodies).toHaveLength(1);

    // The review continuation is confirmable and does not inherit a false
    // failure from the review context.
    await state.pushExecSse(legExec2());
    state.setPhase(4);
    await page
      .getByRole("button", { name: "Execute", exact: true })
      .click();
    await expect(
      page.getByTestId("ai-plan").nth(1).getByRole("status"),
    ).toHaveText("Done");
    // The captured run_python output is a temporary download, not a
    // pre-existing permanent artifact offered for approval.
    await expect(page.getByTestId("ai-temporary-file")).toHaveCount(1);
    await expect(page.getByTestId("ai-generated-file-approval")).toHaveCount(0);
    await expect(
      page.getByText("The table is ready from the existing product data."),
    ).toBeVisible();
    const downloadPromise = page.waitForEvent("download");
    await page
      .getByTestId("ai-temporary-file")
      .getByRole("button")
      .click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toBe("table.csv");
    expect(state.artifactGets).toBe(1);
    tlc = await state.tlc();
    expect(tlc.execBodies).toHaveLength(2);
    expect(tlc.execBodies[0].proposalId).toBe("plan-1");
    expect(tlc.execBodies[1].proposalId).toBe("plan-2");

    // History reconciliation must not duplicate or re-offer either execution.
    await state.loadConversation();
    await expect(page.getByTestId("ai-plan")).toHaveCount(2);
    await expect(
      page.getByRole("button", { name: "Execute", exact: true }),
    ).toHaveCount(0);
    await expect(page.getByTestId("ai-temporary-file")).toHaveCount(1);
    await expect(page.getByTestId("ai-generated-file-approval")).toHaveCount(0);
    await expect(
      page.getByText("Ork mug", { exact: true }).first(),
    ).toBeVisible();
    tlc = await state.tlc();
    expect(tlc.execBodies).toHaveLength(2);
    expect(tlc.chatBodies).toHaveLength(2);
    expect(state.unexpectedWrites).toEqual([]);
  });
}

test("a terminal-only review frame on a late stream subscription completes cleanly", async ({
  page,
}) => {
  // History still shows the confirmed plan mid-execution, and the stream is
  // not auto-attached (generating: false). A late subscriber receives only the
  // final canonical review frame, so the transport must read its phase before
  // running terminal bookkeeping on the stale execution flag.
  const state = await fixture(page, {
    messages: [userRow, planRunningRow],
  });
  await state.loadConversation();
  await state.pushStreamSse(
    sse("done", {}, meta("run-exec-1", MSG_REVIEW1, 9, "review", { dbId: 5 })),
  );
  // By the time the subscription lands, persisted history already holds the
  // finished execution and the fresh review continuation.
  state.setHistory(historyPhase[3]);
  await state.evaluate(async () => {
    const s = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await s.dispatch("aiChat/attachLiveStream");
  });
  const cards = page.getByTestId("ai-plan");
  await expect(cards).toHaveCount(2);
  await expect(cards.first().getByRole("status")).toHaveText("Done");
  await expect(
    page.getByRole("button", { name: "Execute", exact: true }),
  ).toBeEnabled();
  const ui = await state.evaluate(() => {
    const s = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store.state.aiChat;
    return {
      isLoading: s.isLoading,
      errors: s.messages.filter(
        (m: { requestError?: unknown; planConfirmationError?: unknown }) =>
          m.requestError || m.planConfirmationError,
      ).length,
    };
  });
  expect(ui).toEqual({ isLoading: false, errors: 0 });
  const tlc = await state.tlc();
  expect(tlc.execBodies).toHaveLength(0);
});

test("an older unanswered form is inactive while a submitted recap stays", async ({
  page,
}) => {
  const state = await fixture(page, {
    messages: [
      { dbId: 1, role: "user", content: "Earlier question" },
      {
        dbId: 2,
        role: "assistant",
        content: "",
        toolCallsJson: envelope({
          tools: [validFormTool],
          timeline: [toolItem(validFormTool)],
          messageId: MSG_CHAT,
          runId: "run-chat",
          phase: "chat",
          lastEventSequence: 4,
          messageState: "completed",
        }),
      },
      { dbId: 3, role: "user", content: "Thanks, continue." },
      {
        dbId: 4,
        role: "assistant",
        content: "Understood.",
        toolCallsJson: envelope({
          timeline: [textItem("Understood.")],
          messageId: "m-later",
          runId: "run-later",
          phase: "chat",
          lastEventSequence: 2,
          messageState: "completed",
        }),
      },
    ],
  });
  await state.loadConversation();
  await expect(
    page.getByText(
      "This form is no longer active. Continue from the latest message.",
    ),
  ).toBeVisible();
  await expect(page.locator('input[name="name"]')).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "Continue", exact: true }),
  ).toHaveCount(0);

  // A submitted historical form remains a visible read-only recap.
  const submitted = await fixture(page, {
    messages: [
      { dbId: 1, role: "user", content: "Earlier question" },
      {
        dbId: 2,
        role: "assistant",
        content: "",
        toolCallsJson: envelope({
          tools: [validFormTool],
          timeline: [toolItem(validFormTool)],
          messageId: MSG_CHAT,
          runId: "run-chat",
          phase: "chat",
          lastEventSequence: 4,
          messageState: "completed",
        }),
      },
      formReplyRow,
      {
        dbId: 4,
        role: "assistant",
        content: "Understood.",
        toolCallsJson: envelope({
          timeline: [textItem("Understood.")],
          messageId: "m-later",
          runId: "run-later",
          phase: "chat",
          lastEventSequence: 2,
          messageState: "completed",
        }),
      },
    ],
  });
  await submitted.loadConversation();
  await expect(
    page.getByText("Ork mug", { exact: true }).first(),
  ).toBeVisible();
  await expect(page.locator('input[name="name"]')).toHaveCount(0);
});

test("foreign owners, stale sessions, and double submits cannot send a form reply", async ({
  page,
}) => {
  const state = await fixture(page, { messages: historyPhase[1] });
  await state.pushChatSse(legChat());
  await state.openChat();
  await page.evaluate(async (prompt) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/sendMessage", prompt);
  }, userPrompt);
  await expect(
    page.locator('[data-slot="questionnaire-choice"]', { hasText: "Accessory" }),
  ).toBeVisible();

  const results = await page.evaluate(
    async ({ prompt }) => {
      const store = (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store;
      const owner = store.state.aiChat.messages.find(
        (m: any) => m.role === "assistant",
      );
      const base = {
        sessionId: store.state.aiChat.sessionId,
        messageId: owner.id,
        formId: "form-2",
        answers: { category: "accessory" },
        prompt,
      };
      const foreign = await store.dispatch("aiChat/submitFormReply", {
        ...base,
        messageId: 424242,
      });
      const staleSession = await store.dispatch(
        "aiChat/submitFormReply",
        { ...base, sessionId: "someone-elses-session" },
      );
      const [first, second] = await Promise.all([
        store.dispatch("aiChat/submitFormReply", base),
        store.dispatch("aiChat/submitFormReply", base),
      ]);
      const afterSubmit = await store.dispatch(
        "aiChat/submitFormReply",
        base,
      );
      return { foreign, staleSession, first, second, afterSubmit };
    },
    { prompt: followUp },
  );
  expect(results.foreign).toBe(false);
  expect(results.staleSession).toBe(false);
  expect(results.second).toBe(false);
  expect(results.afterSubmit).toBe(false);
  const tlc = await state.tlc();
  expect(tlc.chatBodies).toHaveLength(2);
  expect(tlc.chatBodies[1].messages.at(-1).content).toBe(followUp);
});

test("an interrupted canonical done disables confirmation before history", async ({
  page,
}) => {
  const state = await fixture(page, {
    messages: [userRow, interruptedPlanRow],
  });
  await state.holdHistory();
  await state.pushChatSse(legInterruptedPlan());
  await state.openChat();
  await page.evaluate(async (prompt) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    store.dispatch("aiChat/sendMessage", prompt);
  }, userPrompt);
  // done.interrupted is terminal immediately: even while the history GET is
  // still pending, the proposal can never expose its Execute button.
  await expect(page.getByTestId("ai-plan")).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Execute", exact: true }),
  ).toHaveCount(0);
  state.releaseHistory();
  await expect(
    page.getByRole("button", { name: "Execute", exact: true }),
  ).toHaveCount(0);
});

test("an execution done without a verdict surfaces an honest error, not a phantom success", async ({
  page,
}) => {
  const state = await fixture(page, {
    messages: [userRow, planProposalRow],
  });
  await state.loadConversation();
  await expect(
    page.getByRole("button", { name: "Execute", exact: true }),
  ).toBeEnabled();
  // Canonical execution leg that ends without ok/cancelled/interrupted.
  await state.pushExecSse(
    [
      sse(
        "message_start",
        {},
        meta("run-exec-1", MSG_PLAN, 1, "execution", {
          dbId: 4,
          proposalId: "plan-1",
        }),
      ),
      sse("done", {}, meta("run-exec-1", MSG_PLAN, 2, "execution")),
    ].join(""),
  );
  await page.evaluate(() => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    store.dispatch("aiChat/confirmPlan", "plan-1");
  });
  // The stream ends without a terminal verdict: the proposal is not marked
  // executed and no retry can fire by itself.
  await expect(page.getByRole("alert")).toBeVisible();
  const tlc = await state.tlc();
  expect(tlc.execBodies).toHaveLength(1);
  const executed = await page.evaluate(() => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    const planMsg = store.state.aiChat.messages.find(
      (m: any) => m.plan?.proposalId === "plan-1",
    );
    return { executed: planMsg.planExecuted, failed: planMsg.planFailed };
  });
  expect(executed.executed).toBeFalsy();
  expect(executed.failed).toBeFalsy();
});
