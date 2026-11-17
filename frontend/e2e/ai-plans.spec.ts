import { expect, test, type Page } from "@playwright/test";

const sessionId = "plan-browser-test";
const proposalId = "signed-test-proposal";
const plan = {
  proposalId,
  summary: "Обновить выбранные карточки",
  affectedCount: 2,
  steps: [
    {
      tool: "platform_request",
      description: "Обновить карточки на выбранной площадке",
      args: {
        method: "POST",
        path: "/marketplace/connections/1/products/export-batch",
        body: { productIds: [1, 2] },
      },
    },
  ],
};
const sse = (type: string, data: unknown) =>
  `data: ${JSON.stringify({ type, data })}\n\n`;

type Outcome =
  | "success"
  | "failed"
  | "partial"
  | "queued"
  | "disconnect"
  | "rejected"
  | "lost-confirmation";
async function fixture(
  page: Page,
  outcome: Outcome = "success",
  options: {
    staleHistory?: boolean;
    review?: "success" | "recovery" | "disconnect";
    generatedImports?: Array<Record<string, unknown>>;
    importPlan?: Record<string, unknown>;
    importResult?: Record<string, unknown>;
  } = {},
) {
  page.on("pageerror", (error) =>
    console.error("Plan fixture page error:", error.message),
  );
  page.on("requestfailed", (request) =>
    console.error(
      "Plan fixture request failed:",
      new URL(request.url()).pathname,
      request.failure()?.errorText,
    ),
  );
  let accepted = false;
  let executions = 0;
  let conversationReads = 0;
  let operationStatus = "running";
  let cancelled = false;
  const unexpectedWrites: string[] = [];
  const fixturePlan: any = options.importPlan ?? plan;
  let downloadGets = 0;
  let downloadResponder: (route: any) => Promise<void> = async (route) =>
    route.fulfill({ status: 404, json: { code: "file_not_found" } });
  const executionTools = () =>
    accepted
      ? [
          ...(outcome === "partial"
            ? [
                {
                  id: "exec-0",
                  name: "platform_request",
                  result: { success: true },
                },
              ]
            : []),
          {
            id: "exec-1",
            name: fixturePlan.steps[0].tool,
            input: JSON.stringify(fixturePlan.steps[0].args),
            summary: ["failed", "partial"].includes(outcome)
              ? "HTTP 400"
              : "Accepted",
            result:
              options.importResult ??
              (outcome === "queued"
                ? { operationId: "op-plan", status: "pending" }
                : ["failed", "partial"].includes(outcome)
                  ? { code: "bad_request", status: 400 }
                  : { success: true }),
          },
        ]
      : [];
  const reviewPlan = {
    ...fixturePlan,
    proposalId: "corrected-proposal",
    summary: "Исправленный расчёт",
    discardDraftText: true,
  };
  const reviewText = "Проверенный результат расчёта: найдено 2 товара.";
  const reviewSse = () =>
    !options.review
      ? ""
      : sse("assistant_start", { executionProposalId: proposalId }) +
        (options.review === "recovery"
          ? sse("plan", reviewPlan)
          : sse("text", { delta: reviewText })) +
        (options.review === "disconnect" ? "" : sse("done", {}));
  const conversation = () => ({
    sessionId,
    title: "Проверка плана",
    generating: false,
    messages: [
      { dbId: 1, role: "user", content: "Обнови выбранные карточки" },
      {
        dbId: 2,
        role: "assistant",
        content: accepted ? "Шаги плана завершены." : "Подготовил план.",
        toolCallsJson: JSON.stringify({
          _ui: "assistantTurn",
          plan: options.generatedImports
            ? { ...fixturePlan, generatedFileImports: options.generatedImports }
            : fixturePlan,
          planConfirmed: accepted,
          planExecuted: accepted && outcome !== "disconnect",
          planFailed: accepted && ["failed", "partial"].includes(outcome),
          planExecStart: accepted ? 1 : undefined,
          planCancelled: cancelled,
          tools: executionTools(),
          timeline: [
            { type: "text", text: "Подготовил план." },
            ...executionTools().map((tool) => ({ type: "tool", id: tool.id })),
            ...(accepted
              ? [{ type: "text", text: "Шаги плана завершены." }]
              : []),
          ],
        }),
      },
      ...(accepted && options.review && options.review !== "disconnect"
        ? [
            {
              dbId: 3,
              role: "assistant",
              content: options.review === "recovery" ? "" : reviewText,
              toolCallsJson: JSON.stringify({
                _ui: "assistantTurn",
                executionProposalId: proposalId,
                plan: options.review === "recovery" ? reviewPlan : null,
                tools: [],
                timeline:
                  options.review === "recovery"
                    ? []
                    : [{ type: "text", text: reviewText }],
              }),
            },
          ]
        : []),
    ],
  });
  await page.addInitScript(() => {
    localStorage.setItem("delegat.locale", "ru");
    localStorage.setItem("mobile-warning-dismissed", "1");
  });
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname
      .replace(/^\/api/, "")
      .replace(/\/+$/, "");
    let body: unknown = [];
    if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
    else if (path === "/auth/me")
      body = {
        id: 1,
        userName: "plan-browser-test",
        isAdmin: true,
        permissions: [
          "ai.use",
          "catalog.manage",
          "files.manage",
          "marketplace.manage",
        ],
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
    else if (path === `/ai/conversations/${sessionId}`) {
      conversationReads++;
      body =
        options.staleHistory && accepted ? initialConversation : conversation();
    } else if (path === "/ai/conversations/messages") {
      const message = request
        .postDataJSON()
        .messages.find((m: any) => m.role === "assistant");
      cancelled = !!JSON.parse(message?.toolCallsJson || "{}").planCancelled;
      body = { saved: [] };
    } else if (path === "/ai/execute-plan") {
      expect(request.postDataJSON()).toEqual({ proposalId, sessionId });
      executions++;
      if (outcome === "lost-confirmation") {
        await route.abort("failed");
        return;
      }
      if (outcome === "rejected") {
        await route.fulfill({
          status: 409,
          json: {
            code: "ai_plan_confirmation_rejected",
            data: { reason: "expired" },
          },
        });
        return;
      }
      accepted = true;
      const result =
        options.importResult ??
        (outcome === "queued"
          ? { operationId: "op-plan", status: "pending" }
          : ["failed", "partial"].includes(outcome)
            ? { code: "bad_request", status: 400 }
            : { success: true });
      await route.fulfill({
        contentType: "text/event-stream",
        body:
          (outcome === "partial"
            ? sse("tool", {
                id: "exec-0",
                name: "platform_request",
                status: "running",
                input: "{}",
              }) +
              sse("tool", {
                id: "exec-0",
                name: "platform_request",
                status: "done",
                result: '{"success":true}',
              })
            : "") +
          sse("tool", {
            id: "exec-1",
            name: fixturePlan.steps[0].tool,
            status: "running",
            input: JSON.stringify(fixturePlan.steps[0].args),
          }) +
          sse("tool", {
            id: "exec-1",
            name: fixturePlan.steps[0].tool,
            status: "done",
            result: JSON.stringify(result),
            summary: outcome === "failed" ? "HTTP 400" : "Accepted",
          }) +
          (outcome === "disconnect"
            ? ""
            : sse("text", { delta: "Шаги плана завершены." }) +
              sse("done", { ok: !["failed", "partial"].includes(outcome) })) +
          reviewSse(),
      });
      return;
    } else if (path === "/operations/op-plan/status") {
      if (operationStatus === "missing") {
        await route.fulfill({ status: 404, json: {} });
        return;
      }
      body = {
        status: operationStatus,
        totalItems: 2,
        completedItems: ["completed", "partial"].includes(operationStatus)
          ? 2
          : 0,
        failedItems: operationStatus === "partial" ? 1 : 0,
      };
    } else if (path === "/ai/chat/stream") {
      await route.fulfill({ status: 404, json: {} });
      return;
    } else if (
      /^\/store-storage\/files\/\d+\/download$/.test(path) ||
      /^\/files\/\d+\/download$/.test(path) ||
      /^\/ai\/chat\/generated-files\/[a-f0-9]{32}\/download$/.test(path)
    ) {
      downloadGets++;
      await downloadResponder(route);
      return;
    } else if (path === "/settings/branding") body = {};
    else if (request.method() !== "GET")
      unexpectedWrites.push(`${request.method()} ${path}`);
    await route.fulfill({ json: body });
  });
  const initialConversation = conversation();
  await page.goto("/products");
  await page.waitForFunction(
    () =>
      !!(document.querySelector("#app") as any)?.__vue_app__?.config
        .globalProperties.$store,
  );
  await page.evaluate(async (sid) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/loadConversation", sid);
    store.commit("aiChat/setOpen", true);
  }, sessionId);
  await expect(
    page.getByRole("button", { name: "Выполнить", exact: true }),
  ).toBeEnabled();
  return {
    executions: () => executions,
    reads: () => conversationReads,
    downloadGets: () => downloadGets,
    setDownload: (responder: (route: any) => Promise<void>) => {
      downloadResponder = responder;
    },
    unexpectedWrites,
    setOperation: (status: string) => {
      operationStatus = status;
    },
  };
}

async function reloadPlan(page: Page, sid = sessionId) {
  await page.reload();
  await page.waitForFunction(
    () => !!(document.querySelector("#app") as any)?.__vue_app__,
  );
  await page.evaluate(async (id) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/loadConversation", id);
    store.commit("aiChat/setOpen", true);
  }, sid);
}

test("execution stays completed after history reconciliation and cannot be submitted twice", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(page.getByText("Готово", { exact: true })).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Выполнить", exact: true }),
  ).toHaveCount(0);
  expect(state.executions()).toBe(1);
  expect(state.reads()).toBeGreaterThanOrEqual(2);
  expect(state.unexpectedWrites).toEqual([]);
  await reloadPlan(page);
  await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
    "Готово",
  );
});

test("an explicitly stale server response cannot reopen an executed plan", async ({
  page,
}) => {
  const state = await fixture(page, "success", { staleHistory: true });
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
    "Готово",
  );
  await expect.poll(state.reads).toBeGreaterThanOrEqual(2);
  await expect(
    page.getByRole("button", { name: "Выполнить", exact: true }),
  ).toHaveCount(0);
});

test("a successful calculation review is separate from the plan and survives reload", async ({
  page,
}) => {
  const state = await fixture(page, "success", { review: "success" });
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(
    page.getByText("Проверенный результат расчёта: найдено 2 товара.", {
      exact: true,
    }),
  ).toHaveCount(1);
  await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
    "Готово",
  );
  await reloadPlan(page);
  await expect(
    page.getByText("Проверенный результат расчёта: найдено 2 товара.", {
      exact: true,
    }),
  ).toHaveCount(1);
  expect(state.executions()).toBe(1);
});

test("failure keeps the failed plan and offers only a fresh unexecuted recovery plan", async ({
  page,
}) => {
  const state = await fixture(page, "failed", { review: "recovery" });
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  const cards = page.getByTestId("ai-plan");
  await expect(cards).toHaveCount(2);
  await expect(cards.nth(0).getByRole("status")).toHaveText(
    "Ошибка выполнения",
  );
  await expect(cards.nth(1).getByRole("status")).toHaveText(
    "Ожидает подтверждения",
  );
  await expect(
    page.getByRole("button", { name: "Выполнить", exact: true }),
  ).toHaveCount(1);
  await page.evaluate(async (pid) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/confirmPlan", pid);
  }, proposalId);
  expect(state.executions()).toBe(1);
  await reloadPlan(page);
  await expect(cards).toHaveCount(2);
  await expect(cards.nth(0).getByRole("status")).toHaveText(
    "Ошибка выполнения",
  );
});

test("replaying an interrupted review does not duplicate the completed execution or review", async ({
  page,
}) => {
  const state = await fixture(page, "success", { review: "disconnect" });
  await page.route("**/api/ai/chat/stream?**", (route) =>
    route.fulfill({
      contentType: "text/event-stream",
      body:
        sse("tool", {
          id: "exec-1",
          name: "platform_request",
          status: "running",
          input: "{}",
        }) +
        sse("tool", {
          id: "exec-1",
          name: "platform_request",
          status: "done",
          result: '{"success":true}',
        }) +
        sse("done", { ok: true }) +
        sse("assistant_start", { executionProposalId: proposalId }) +
        sse("text", { delta: "Анализ результата восстановлен." }) +
        sse("done", {}),
    }),
  );
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(
    page.getByText("Анализ результата восстановлен.", { exact: true }),
  ).toHaveCount(1);
  await expect(
    page.getByText("Проверенный результат расчёта: найдено 2 товара.", {
      exact: true,
    }),
  ).toHaveCount(0);
  await expect(page.getByTestId("ai-plan")).toHaveCount(1);
  expect(state.executions()).toBe(1);
});

test("proposing a plan removes speculative result text before execution", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.evaluate(() => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    store.commit("aiChat/updateLastAssistantMessage", {
      delta: "UNVERIFIED TABLE",
      textBlock: "UNVERIFIED TABLE",
    });
    store.commit("aiChat/updateLastAssistantMessage", {
      plan: {
        ...store.state.aiChat.messages.at(-1).plan,
        discardDraftText: true,
      },
    });
  });
  await expect(page.getByText(/UNVERIFIED TABLE/)).toHaveCount(0);
  expect(state.executions()).toBe(0);
});

for (const [outcome, label] of [
  ["failed", "Ошибка выполнения"],
  ["partial", "Выполнено частично"],
  ["disconnect", "Результат не подтверждён"],
  ["rejected", "Подготовьте новый план"],
  ["lost-confirmation", "Подготовьте новый план"],
] as const) {
  test(`${outcome} remains non-repeatable and is not shown as success`, async ({
    page,
  }) => {
    const state = await fixture(page, outcome);
    await page.getByRole("button", { name: "Выполнить", exact: true }).click();
    await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
      label,
    );
    await expect(
      page.getByRole("button", { name: "Выполнить", exact: true }),
    ).toHaveCount(0);
    expect(state.executions()).toBe(1);
    expect(state.unexpectedWrites).toEqual([]);
    if (["failed", "partial", "disconnect"].includes(outcome)) {
      await reloadPlan(page);
      await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
        label,
      );
    }
  });
}

for (const [operation, label] of [
  ["completed", "Готово"],
  ["failed", "Ошибка выполнения"],
  ["partial", "Выполнено частично"],
  ["missing", "Результат не подтверждён"],
]) {
  test(`queued plan tracks ${operation} without confusing enqueue with completion`, async ({
    page,
  }) => {
    const state = await fixture(page, "queued");
    await page.getByRole("button", { name: "Выполнить", exact: true }).click();
    await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
      "Фоновая операция выполняется",
    );
    state.setOperation(operation);
    await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
      label,
      { timeout: 8000 },
    );
    await expect(
      page.getByRole("button", { name: "Выполнить", exact: true }),
    ).toHaveCount(0);
    expect(state.executions()).toBe(1);
  });
}

test("cancel targets the selected plan and persists without executing it", async ({
  page,
}) => {
  const state = await fixture(page);
  const saved = page.waitForResponse(
    (response) =>
      response.url().endsWith("/ai/conversations/messages") &&
      response.request().method() === "POST",
  );
  await page
    .getByTestId("ai-plan")
    .getByRole("button", { name: "Отмена", exact: true })
    .click();
  await saved;
  await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
    "Отменено",
  );
  await reloadPlan(page);
  await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
    "Отменено",
  );
  expect(state.executions()).toBe(0);
  expect(state.unexpectedWrites).toEqual([]);
});

test("wrong proposal identity is ignored and concurrent confirmation runs once", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.evaluate(async (pid) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/confirmPlan", "other-proposal");
    await Promise.all([
      store.dispatch("aiChat/confirmPlan", pid),
      store.dispatch("aiChat/confirmPlan", pid),
    ]);
  }, proposalId);
  await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
    "Готово",
  );
  expect(state.executions()).toBe(1);
});

test("reconnecting replays execution once without duplicating tools or resubmitting", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.route("**/api/ai/chat/stream?**", (route) =>
    route.fulfill({
      contentType: "text/event-stream",
      body:
        sse("tool", {
          id: "replay-1",
          name: "platform_request",
          status: "running",
          input: "{}",
        }) +
        sse("tool", {
          id: "replay-1",
          name: "platform_request",
          status: "done",
          result: '{"success":true}',
        }) +
        sse("done", { ok: true }),
    }),
  );
  const count = await page.evaluate(async (pid) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    store.commit("aiChat/setPlanConfirmed", pid);
    store.commit("aiChat/updateLastAssistantMessage", {
      toolCall: { id: "replay-1", name: "platform_request", status: "running" },
    });
    await store.dispatch("aiChat/attachLiveStream");
    return store.state.aiChat.messages
      .at(-1)
      .toolCalls.filter((tool: any) => tool.id === "replay-1").length;
  }, proposalId);
  expect(count).toBe(1);
  await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
    "Готово",
  );
  expect(state.executions()).toBe(0);
});

test("superseded proposals cannot be executed or cancelled", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.evaluate(async (pid) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    store.commit("aiChat/supersedePendingPlans");
    await store.dispatch("aiChat/confirmPlan", pid);
    await store.dispatch("aiChat/cancelPlan", pid);
  }, proposalId);
  await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
    "Нужно пересмотреть",
  );
  await expect(
    page
      .getByTestId("ai-plan")
      .getByRole("button", { name: "Выполнить", exact: true }),
  ).toHaveCount(0);
  expect(state.executions()).toBe(0);
});

for (const width of [1280, 390]) {
  for (const dark of [false, true]) {
    test(`long ordered plan supports keyboard at ${width}px ${dark ? "dark" : "light"}`, async ({
      page,
    }, testInfo) => {
      await page.setViewportSize({ width, height: 900 });
      await page.emulateMedia({ reducedMotion: "reduce" });
      await fixture(page);
      await page.evaluate((dark) => {
        document.documentElement.classList.toggle("dark", dark);
        const store = (document.querySelector("#app") as any).__vue_app__.config
          .globalProperties.$store;
        const plan = store.state.aiChat.messages.at(-1).plan;
        store.commit("aiChat/updateLastAssistantMessage", {
          plan: {
            ...plan,
            steps: [
              {
                description: "Проверить выбранные карточки",
                tool: "sql_readonly",
                args: {},
              },
              {
                description: "Обновить данные товара",
                tool: "platform_request",
                args: {
                  method: "PATCH",
                  path: "/products/1",
                  body: { price: 100 },
                },
              },
              {
                description: `Сверить результат: ${"ОченьДлинныйИдентификатор".repeat(15)}`,
                tool: "sql_readonly",
                args: {},
              },
              {
                description: "Выполнить следующее действие",
                tool: "platform_request",
                args: {},
              },
            ],
          },
        });
      }, dark);
      const card = page.getByTestId("ai-plan");
      await expect(card).toBeVisible();
      await page.screenshot({
        path: testInfo.outputPath("plan-preview.png"),
        fullPage: true,
      });
      await expect(card.locator("li")).toHaveCount(3);
      await expect(card.locator("li").nth(0)).toContainText(
        "Проверить выбранные карточки",
      );
      await expect(card.locator("li").nth(1)).toContainText(
        "Обновить данные товара",
      );
      const more = card.getByRole("button", { name: "Ещё 1", exact: true });
      await more.focus();
      await page.keyboard.press("Enter");
      await expect(card.locator("li")).toHaveCount(4);
      const details = card.locator("li").nth(2).getByRole("button");
      await details.focus();
      await page.keyboard.press("Enter");
      await expect(details).toHaveAttribute("aria-expanded", "true");
      expect(
        await card.evaluate((el) => el.scrollWidth <= el.clientWidth + 1),
      ).toBe(true);
      await page.screenshot({
        path: testInfo.outputPath("plan.png"),
        fullPage: true,
      });
    });
  }
}


// --- Canonical typed-event run (M4): stable runId/messageId/sequence ---------

const canonicalSession = "plan-canonical-test";
const canonicalRun = "run-exec";
const recoveryPlan = {
  ...plan,
  proposalId: "corrected-proposal",
  summary: "Исправленный расчёт",
};
const canonSse = (
  type: string,
  data: unknown,
  meta: {
    messageId: string;
    sequence: number;
    phase?: string;
    proposalId?: string;
    dbId?: number;
  },
) =>
  `data: ${JSON.stringify({
    type,
    data,
    runId: canonicalRun,
    messageId: meta.messageId,
    sequence: meta.sequence,
    ...(meta.phase ? { phase: meta.phase } : {}),
    ...(meta.proposalId ? { proposalId: meta.proposalId } : {}),
    ...(meta.dbId ? { dbId: meta.dbId } : {}),
  })}\n\n`;

// One canonical live-turn buffer: execution A (failed), review B (plan),
// review C (text only) - two distinct reviews sharing parent A.
const canonicalStream = [
  canonSse("message_start", {}, {
    messageId: "srv-a", sequence: 1, phase: "execution", proposalId, dbId: 2,
  }),
  canonSse("tool", {
    id: "exec-1", name: "platform_request", status: "running",
    input: JSON.stringify(plan.steps[0].args),
  }, { messageId: "srv-a", sequence: 2 }),
  canonSse("tool", {
    id: "exec-1", name: "platform_request", status: "done",
    result: JSON.stringify({ code: "bad_request", status: 400 }),
    summary: "HTTP 400",
  }, { messageId: "srv-a", sequence: 3 }),
  canonSse("execution_status", {
    status: "failed", succeeded: 0, total: 1, queued: 0, failed: 1,
    skipped: 0, summary: "No steps completed successfully.",
    outcomeUnknown: true,
  }, { messageId: "srv-a", sequence: 4 }),
  canonSse("done", { ok: false, failedCount: 1 }, {
    messageId: "srv-a", sequence: 5,
  }),
  canonSse("assistant_start", { executionProposalId: proposalId }, {
    messageId: "srv-b", sequence: 6, phase: "review",
  }),
  canonSse("plan", recoveryPlan, { messageId: "srv-b", sequence: 7 }),
  canonSse("done", {}, { messageId: "srv-b", sequence: 8 }),
  canonSse("assistant_start", { executionProposalId: proposalId }, {
    messageId: "srv-c", sequence: 9, phase: "review",
  }),
  canonSse("text", { delta: "Второй отзыв без плана." }, {
    messageId: "srv-c", sequence: 10,
  }),
  canonSse("done", {}, { messageId: "srv-c", sequence: 11 }),
];

async function canonicalFixture(
  page: Page,
  opts: { streamEof?: boolean; staleGet?: boolean; reviewGap?: boolean } = {},
) {
  page.on("pageerror", (error) =>
    console.error("Canonical fixture page error:", error.message),
  );
  page.on("requestfailed", (request) =>
    console.error(
      "Canonical fixture request failed:",
      request.url(),
      request.failure()?.errorText,
    ),
  );
  let executed = !!opts.reviewGap;
  let stopped = false;
  let executions = 0;
  const unexpectedWrites: string[] = [];
  const expectedWrites = new Set([
    "POST /ai/execute-plan",
    "POST /ai/stop",
    "POST /ai/conversations/messages",
  ]);
  const assistantRow = (
    dbId: number,
    envelope: Record<string, unknown>,
    content = "",
  ) => ({
    dbId,
    role: "assistant",
    content,
    toolCallsJson: JSON.stringify(envelope),
  });
  const conversation = () => ({
    sessionId: canonicalSession,
    title: "Канонический диалог",
    generating: executed && !stopped,
    messages: [
      { dbId: 1, role: "user", content: "Обнови выбранные карточки" },
      assistantRow(
        2,
        {
          _ui: "assistantTurn",
          messageId: "srv-a",
          runId: executed ? canonicalRun : "run-propose",
          phase: executed ? "execution" : "chat",
          lastEventSequence: executed ? 5 : 3,
          messageState: "completed",
          plan,
          planOwnerMessageId: "srv-a",
          planConfirmed: executed || undefined,
          planExecuted: executed || undefined,
          planFailed: executed || undefined,
          planExecStart: executed ? 1 : undefined,
          executionOutcome: executed
            ? { status: "failed", succeeded: 0, total: 1, failed: 1 }
            : undefined,
          tools: executed
            ? [
                {
                  id: "exec-1",
                  name: "platform_request",
                  status: "done",
                  summary: "HTTP 400",
                  result: '{"code":"bad_request","status":400}',
                },
              ]
            : [],
          timeline: [
            { type: "text", text: "Подготовил план." },
            ...(executed ? [{ type: "tool", id: "exec-1" }] : []),
          ],
        },
        "Подготовил план.",
      ),
      // With reviewGap the GET raced review-row insertion: only the
      // executed canonical A is persisted yet, still generating=true.
      ...(executed && !opts.reviewGap
        ? [
            assistantRow(
              3,
              opts.staleGet
                ? {
                    // A read that raced persistence: the review row exists but
                    // only with its begin-time envelope.
                    _ui: "assistantTurn",
                    messageId: "srv-b",
                    runId: canonicalRun,
                    phase: "review",
                    lastEventSequence: 0,
                    messageState: "running",
                    executionProposalId: proposalId,
                    tools: [],
                    timeline: [],
                  }
                : {
                    _ui: "assistantTurn",
                    messageId: "srv-b",
                    runId: canonicalRun,
                    phase: "review",
                    lastEventSequence: 8,
                    messageState: "completed",
                    executionProposalId: proposalId,
                    plan: recoveryPlan,
                    planOwnerMessageId: "srv-b",
                    tools: [],
                    timeline: [],
                  },
            ),
            ...(opts.staleGet
              ? []
              : [
                  assistantRow(
                    4,
                    {
                      _ui: "assistantTurn",
                      messageId: "srv-c",
                      runId: canonicalRun,
                      phase: "review",
                      lastEventSequence: stopped ? 12 : 11,
                      messageState: stopped ? "stopped" : "completed",
                      executionProposalId: proposalId,
                      turnStopped: stopped || undefined,
                      tools: [],
                      timeline: [
                        { type: "text", text: "Второй отзыв без плана." },
                      ],
                    },
                    "Второй отзыв без плана.",
                  ),
                ]),
          ]
        : []),
    ],
  });
  await page.addInitScript(
    ({ replay, session, eof }) => {
      localStorage.setItem("delegat.locale", "ru");
      localStorage.setItem("mobile-warning-dismissed", "1");
      const chunks = replay.map((line: string) =>
        new TextEncoder().encode(line),
      );
      const listeners = new Set<ReadableStreamDefaultController>();
      const originalFetch = window.fetch.bind(window);
      (window as any).__canonicalTest = {
        emit(line: string) {
          const chunk = new TextEncoder().encode(line);
          chunks.push(chunk);
          for (const listener of listeners) listener.enqueue(chunk);
          for (const listener of listeners) listener.close();
          listeners.clear();
        },
        attached() {
          return listeners.size;
        },
      };
      window.fetch = async (input, init) => {
        const url = new URL(
          typeof input === "string"
            ? input
            : input instanceof URL
              ? input.href
              : input.url,
          location.href,
        );
        if (
          url.pathname === "/api/ai/chat/stream" &&
          url.searchParams.get("sessionId") === session
        ) {
          return new Response(
            new ReadableStream({
              start(controller) {
                for (const chunk of chunks) controller.enqueue(chunk);
                if (eof) {
                  controller.close();
                  return;
                }
                listeners.add(controller);
                init?.signal?.addEventListener(
                  "abort",
                  () => {
                    listeners.delete(controller);
                    controller.error(
                      new DOMException("Detached", "AbortError"),
                    );
                  },
                  { once: true },
                );
              },
            }),
            { headers: { "Content-Type": "text/event-stream" } },
          );
        }
        return originalFetch(input, init);
      };
    },
    {
      // In the gap the live buffer can only replay A's terminal segment; the
      // review events arrive later as live emits.
      replay: opts.reviewGap
        ? canonicalStream.slice(0, 5)
        : canonicalStream,
      session: canonicalSession,
      eof: !!opts.streamEof,
    },
  );
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname
      .replace(/^\/api/, "")
      .replace(/\/+$/, "");
    let body: unknown = [];
    if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
    else if (path === "/auth/me")
      body = {
        id: 1,
        userName: "canonical-test",
        isAdmin: true,
        permissions: ["ai.use", "catalog.manage"],
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
    else if (path === "/ai/conversations")
      body = [
        {
          sessionId: canonicalSession,
          title: "Канонический диалог",
          generating: executed && !stopped,
          messageCount: 4,
          lastMessageAt: new Date().toISOString(),
        },
      ];
    else if (path === `/ai/conversations/${canonicalSession}`)
      body = conversation();
    else if (path === "/ai/execute-plan") {
      expect(request.postDataJSON()).toEqual({
        proposalId,
        sessionId: canonicalSession,
      });
      executions++;
      executed = true;
      await route.fulfill({
        contentType: "text/event-stream",
        body: canonicalStream.join(""),
      });
      return;
    } else if (path === "/ai/stop") {
      stopped = true;
      body = { stopped: true };
    } else if (path === "/ai/conversations/messages") body = { saved: [] };
    else if (path === "/settings/branding") body = {};
    else if (request.method() !== "GET") {
      const write = `${request.method()} ${path}`;
      if (!expectedWrites.has(write)) unexpectedWrites.push(write);
    }
    await route.fulfill({ json: body });
  });
  await page.goto("/products");
  await page.waitForFunction(
    () => !!(document.querySelector("#app") as any)?.__vue_app__,
  );
  await page.evaluate(async (sid) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/loadConversation", sid);
    store.commit("aiChat/setOpen", true);
  }, canonicalSession);
  if (opts.reviewGap) {
    // A's executed card is the tail; there is no pending plan to execute.
    await expect(page.getByTestId("ai-plan")).toHaveCount(1);
  } else {
    await expect(
      page.getByRole("button", { name: "Выполнить", exact: true }),
    ).toBeEnabled();
  }
  return {
    executions: () => executions,
    unexpectedWrites,
    emitReview: () =>
      page.evaluate(
        (line) => (window as any).__canonicalTest.emit(line),
        canonSse(
          "assistant_start",
          { executionProposalId: proposalId },
          { messageId: "srv-b", sequence: 6, phase: "review" },
        ) +
          canonSse(
            "text",
            { delta: "Отзыв после выполнения." },
            { messageId: "srv-b", sequence: 7 },
          ) +
          canonSse("done", {}, { messageId: "srv-b", sequence: 8 }),
      ),
    emitStopped: () =>
      page.evaluate(
        (line) => (window as any).__canonicalTest.emit(line),
        canonSse("stopped", {}, { messageId: "srv-c", sequence: 12 }),
      ),
    attachedCount: () =>
      page.evaluate(() => (window as any).__canonicalTest.attached()),
  };
}

for (const width of [1280, 390]) {
  test(`canonical run replays deduped across reload and keeps review ownership at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const state = await canonicalFixture(page);
    await page.getByRole("button", { name: "Выполнить", exact: true }).click();

    // Execution A failed; recovery B is a separate confirmable review; C is a
    // second no-plan review sharing the same parent.
    const cards = page.getByTestId("ai-plan");
    await expect(cards).toHaveCount(2);
    await expect(cards.nth(0).getByRole("status")).toHaveText(
      "Ошибка выполнения",
    );
    await expect(cards.nth(1).getByRole("status")).toHaveText(
      "Ожидает подтверждения",
    );
    await expect(
      page.getByRole("button", { name: "Выполнить", exact: true }),
    ).toHaveCount(1);
    await expect(page.getByTestId("ai-execution-outcome")).toContainText(
      "Ни один шаг не завершился успешно.",
    );
    await expect(
      page.getByText("Второй отзыв без плана.", { exact: true }),
    ).toHaveCount(1);
    expect(state.executions()).toBe(1);

    // Reload while the review turn is still generating: the live attach
    // replays the whole canonical buffer; every event is a deduped no-op.
    await reloadPlan(page, canonicalSession);
    await expect(page.getByTestId("ai-plan")).toHaveCount(2);
    await expect(
      page.getByText("Второй отзыв без плана.", { exact: true }),
    ).toHaveCount(1);
    await expect(page.getByTestId("ai-execution-outcome")).toHaveCount(1);
    await expect(
      page.getByRole("button", { name: "Остановить", exact: false }),
    ).toBeVisible();

    // Stopping the trailing review does not reopen completed A or pending B.
    await page
      .getByRole("button", { name: "Остановить", exact: false })
      .click();
    await state.emitStopped();
    await expect(page.getByTestId("ai-turn-stopped")).toHaveCount(1);
    await expect(cards.nth(0).getByRole("status")).toHaveText(
      "Ошибка выполнения",
    );
    await expect(cards.nth(1).getByRole("status")).toHaveText(
      "Ожидает подтверждения",
    );
    await expect(
      page.getByRole("button", { name: "Выполнить", exact: true }),
    ).toHaveCount(1);
    expect(state.executions()).toBe(1);
    expect(state.unexpectedWrites).toEqual([]);
  });
}

async function waitForAttachCycle(page: Page) {
  // The replay attach flips isLoading true then back to false at EOF.
  await page.waitForFunction(() => {
    const s = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store.state.aiChat;
    return s.isLoading === true;
  });
  await page.waitForFunction(() => {
    const s = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store.state.aiChat;
    return s.isLoading === false;
  });
}

test("an all-duplicate canonical replay that EOFs ends cleanly", async ({
  page,
}) => {
  await canonicalFixture(page, { streamEof: true });
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(page.getByTestId("ai-plan")).toHaveCount(2);

  // Reload: the attach replays the full buffer (every event a dedupe no-op)
  // and the stream EOFs at once - a completed review is a clean end, not a
  // transport error.
  await reloadPlan(page, canonicalSession);
  await waitForAttachCycle(page);
  await expect(page.getByTestId("ai-plan")).toHaveCount(2);
  await expect(
    page.getByText("Второй отзыв без плана.", { exact: true }),
  ).toHaveCount(1);
  const ui = await page.evaluate(() => {
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
});

test("a stale canonical reload keeps the streamed pending review and its text", async ({
  page,
}) => {
  // The GET only knows the begin-time review row (seq 0, running, no plan);
  // the canonical reconciliation must keep the finished local projection.
  await canonicalFixture(page, { streamEof: true, staleGet: true });
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(page.getByTestId("ai-plan")).toHaveCount(2);

  await reloadPlan(page, canonicalSession);
  await waitForAttachCycle(page);
  const cards = page.getByTestId("ai-plan");
  await expect(cards).toHaveCount(2);
  await expect(cards.nth(1).getByRole("status")).toHaveText(
    "Ожидает подтверждения",
  );
  await expect(
    page.getByText("Второй отзыв без плана.", { exact: true }),
  ).toHaveCount(1);
  const ui = await page.evaluate(() => {
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
});

test("canonical attach bridges the executed/review persistence gap", async ({
  page,
}) => {
  // The GET raced the review-row insert: only executed canonical A is
  // persisted and generating=true. The live attach must NOT bail out on
  // planExecuted - it replays A's terminal events, then the review segment
  // arrives live.
  const state = await canonicalFixture(page, { reviewGap: true });
  await expect(page.getByTestId("ai-plan")).toHaveCount(1);
  await page.waitForFunction(
    () =>
      (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store.state.aiChat.isLoading === true,
  );
  // The stream is attached before the review emit is injected.
  await expect
    .poll(() => state.attachedCount(), { timeout: 5000 })
    .toBeGreaterThan(0);
  await state.emitReview();
  await expect(
    page.getByText("Отзыв после выполнения.", { exact: true }),
  ).toHaveCount(1);
  await page.waitForFunction(
    () =>
      (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store.state.aiChat.isLoading === false,
  );
  const ui = await page.evaluate(() => {
    const s = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store.state.aiChat;
    return {
      reviews: s.messages.filter(
        (m: { content?: string }) => m.content === "Отзыв после выполнения.",
      ).length,
      errors: s.messages.filter(
        (m: { requestError?: unknown; planConfirmationError?: unknown }) =>
          m.requestError || m.planConfirmationError,
      ).length,
    };
  });
  expect(ui).toEqual({ reviews: 1, errors: 0 });
  // The attach alone must never trigger another execute request.
  expect(state.executions()).toBe(0);
  expect(state.unexpectedWrites).toEqual([]);
});

for (const width of [1280, 390]) {
  test(`generated-file approval shows server metadata, no overflow at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const sha = "a".repeat(64);
    const state = await fixture(page, "success", {
      generatedImports: [
        {
          step: 0,
          artifactId: "0123456789abcdef0123456789abcdef",
          fileName:
            "отчёт-по-продажам-за-очень-длинный-период-2026.csv",
          length: 1048576,
          sha256: sha,
          expiresAtUtc: "2026-12-31T23:59:00Z",
          destination: "store",
          storeId: 1,
          productId: null,
          folderId: 3,
        },
      ],
    });
    const approval = page.getByTestId("ai-generated-file-approval");
    await expect(approval).toBeVisible();
    await expect(approval).toContainText("Сохранение созданного файла");
    await expect(approval).toContainText(
      "отчёт-по-продажам-за-очень-длинный-период-2026.csv",
    );
    await expect(approval).toContainText("Хранилище магазина №1");
    await expect(approval).toContainText("Папка №3");
    await expect(approval).toContainText("Доступен до");
    await expect(approval).toContainText(sha);
    // Full hash and long Cyrillic filename wrap inside the viewport.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);

    // Keyboard confirmation executes the exact signed plan once.
    await page.getByRole("button", { name: "Выполнить", exact: true }).focus();
    await page.keyboard.press("Enter");
    await expect
      .poll(() => state.executions(), { timeout: 5000 })
      .toBe(1);
    await expect(
      page.getByRole("button", { name: "Выполнить", exact: true }),
    ).toHaveCount(0);
    await expect(approval).toBeVisible();
    expect(state.unexpectedWrites).toEqual([]);
  });
}

test("generated-file approval metadata survives reload", async ({ page }) => {
  const sha = "b".repeat(64);
  await fixture(page, "success", {
    generatedImports: [
      {
        step: 0,
        artifactId: "fedcba9876543210fedcba9876543210",
        fileName: "report.csv",
        length: 2048,
        sha256: sha,
        expiresAtUtc: "2026-12-31T23:59:00Z",
        destination: "product",
        storeId: 1,
        productId: 7,
        folderId: null,
      },
    ],
  });
  await reloadPlan(page);
  const approval = page.getByTestId("ai-generated-file-approval");
  await expect(approval).toBeVisible();
  await expect(approval).toContainText("report.csv");
  await expect(approval).toContainText("Файлы товара №7");
  await expect(approval).toContainText("Корневая папка");
  await expect(approval).toContainText(sha);
});

// ---- F3: saved generated-file download control ----

const CSV_BYTES = Buffer.from("name,qty\nWidget,3\n", "utf8");
const ART_ID = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
const SHA256 = "b".repeat(64);

function generatedImport(destination: "store" | "product") {
  const productId = destination === "product" ? 9 : null;
  const fileId = 42;
  const downloadUrl =
    destination === "store"
      ? `/api/store-storage/files/${fileId}/download`
      : `/api/files/${fileId}/download`;
  const args = {
    artifactId: ART_ID,
    sha256: SHA256,
    length: CSV_BYTES.length,
    expiresAtUtc: "2030-01-01T00:00:00Z",
    destination,
    productId,
    folderId: null,
  };
  const importPlan = {
    proposalId,
    summary: "Сохранить созданный файл",
    steps: [
      {
        tool: "import_generated_file",
        description: "Сохранить r.csv",
        args,
      },
    ],
    generatedFileImports: [
      {
        step: 0,
        artifactId: ART_ID,
        fileName: "r.csv",
        length: CSV_BYTES.length,
        sha256: SHA256,
        expiresAtUtc: args.expiresAtUtc,
        destination,
        storeId: 1,
        productId,
        folderId: null,
      },
    ],
  };
  const importResult = {
    destination,
    fileId,
    productId,
    folderId: null,
    fileName: "r.csv",
    contentType: "text/csv",
    length: CSV_BYTES.length,
    sha256: SHA256,
    downloadUrl,
    idempotent: false,
    writtenCount: 1,
  };
  return { importPlan, importResult };
}

const okDownload =
  () => async (route: any) =>
    route.fulfill({
      status: 200,
      contentType: "text/csv",
      headers: {
        "Content-Disposition": 'attachment; filename="r.csv"',
      },
      body: CSV_BYTES,
    });

const errorDownload =
  (status: number) => async (route: any) =>
    route.fulfill({
      status,
      json: {
        code: "file_not_found",
        message: "SENSITIVE DOWNLOAD DIAGNOSTIC",
      },
    });

async function downloadBytes(dl: any): Promise<Buffer> {
  const stream = await dl.createReadStream();
  const chunks: Buffer[] = [];
  for await (const chunk of stream) chunks.push(chunk as Buffer);
  return Buffer.concat(chunks);
}

const downloadButton = (page: Page) =>
  page.getByTestId("ai-generated-file-download").getByRole("button");

async function confirmAndSeeDownload(page: Page) {
  await expect(downloadButton(page)).toHaveCount(0);
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(downloadButton(page)).toHaveCount(1);
  await expect(
    page.getByTestId("ai-generated-file-approval").first(),
  ).toContainText("Файл сохранён");
  await expect(
    page.getByTestId("ai-generated-file-approval").first(),
  ).not.toContainText("Доступен до");
}

async function shot(page: Page, name: string) {
  await page.screenshot({
    path: `artifacts/f3-screenshots/${name}.png`,
    animations: "disabled",
  });
}

for (const destination of ["store", "product"] as const) {
  for (const viewport of [
    { width: 1280, height: 800, tag: "desktop" },
    { width: 390, height: 844, tag: "mobile" },
  ]) {
    test(`generated-file download saves and re-downloads - ${destination} (${viewport.tag})`, async ({
      page,
    }) => {
      const { importPlan, importResult } = generatedImport(destination);
      const state = await fixture(page, "success", {
        importPlan,
        importResult,
      });
      state.setDownload(okDownload());
      await page.setViewportSize(viewport);
      if (destination === "store" && viewport.tag === "desktop") {
        await page.evaluate(() => {
          const w = window as any;
          w.__f3ou = { created: 0, revoked: 0 };
          const oc = URL.createObjectURL.bind(URL);
          const orv = URL.revokeObjectURL.bind(URL);
          URL.createObjectURL = (b: any) => {
            w.__f3ou.created++;
            return oc(b);
          };
          URL.revokeObjectURL = (u: any) => {
            w.__f3ou.revoked++;
            return orv(u);
          };
        });
      }
      await confirmAndSeeDownload(page);
      await shot(page, `${destination}-${viewport.tag}-saved`);

      const [dl] = await Promise.all([
        page.waitForEvent("download"),
        downloadButton(page).press("Enter"),
      ]);
      expect(dl.suggestedFilename()).toBe("r.csv");
      expect(await downloadBytes(dl)).toEqual(CSV_BYTES);
      // The native disabled state during the GET drops keyboard focus;
      // finishing the request must restore it to the re-enabled button.
      await expect(downloadButton(page)).toBeFocused();
      expect(state.executions()).toBe(1);
      expect(state.unexpectedWrites).toEqual([]);
      if (destination === "store" && viewport.tag === "desktop") {
        // The object URL is created once and revoked by its 1s timer —
        // nothing outstanding once the timer runs.
        await expect
          .poll(() =>
            page.evaluate(() => {
              const o = (window as any).__f3ou;
              return o.created - o.revoked;
            }),
          )
          .toBe(0);
      }

      await reloadPlan(page);
      await expect(downloadButton(page)).toHaveCount(1);
      const [dl2] = await Promise.all([
        page.waitForEvent("download"),
        downloadButton(page).click(),
      ]);
      expect(dl2.suggestedFilename()).toBe("r.csv");
      expect(await downloadBytes(dl2)).toEqual(CSV_BYTES);
      expect(state.executions()).toBe(1);
      const overflow = await page.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth,
      );
      expect(overflow).toBeLessThanOrEqual(0);
      await shot(page, `${destination}-${viewport.tag}-reloaded`);
    });
  }
}

test("generated-file download never attaches to a pending recovery proposal", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedImport("store");
  const state = await fixture(page, "success", {
    importPlan,
    importResult,
    review: "recovery",
  });
  state.setDownload(okDownload());
  await confirmAndSeeDownload(page);
  const cards = page.getByTestId("ai-plan");
  await expect(cards).toHaveCount(2);
  // A (executed import) owns the control; pending proposal B does not.
  const cardA = cards.first();
  const cardB = cards.nth(1);
  await expect(cardA.getByTestId("ai-generated-file-download")).toHaveCount(1);
  await expect(cardB.getByTestId("ai-generated-file-download")).toHaveCount(0);
});

test("generated-file download shows localized alerts on 404/403 and retries cleanly", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedImport("store");
  const state = await fixture(page, "success", {
    importPlan,
    importResult,
  });
  await confirmAndSeeDownload(page);

  state.setDownload(errorDownload(404));
  await downloadButton(page).press("Enter");
  const alert = page.getByRole("alert");
  await expect(alert).toContainText(
    "Сохранённый файл больше недоступен",
  );
  await expect(alert).not.toContainText("SENSITIVE DOWNLOAD DIAGNOSTIC");
  // Keyboard focus dropped by the disabled state is restored on re-enable.
  await expect(downloadButton(page)).toBeFocused();
  // Same localized alert at mobile width, no horizontal overflow.
  await page.setViewportSize({ width: 390, height: 844 });
  await shot(page, "store-mobile-404-alert");
  const overflow = await page.evaluate(
    () =>
      document.documentElement.scrollWidth -
      document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(0);
  await page.setViewportSize({ width: 1280, height: 800 });
  await shot(page, "store-desktop-404-alert");

  // 403 with a held response: the user moves to the composer while the
  // button is busy — finishing the request must not steal focus back.
  let release403: () => void = () => {};
  const gate403 = new Promise<void>((r) => (release403 = r));
  state.setDownload(async (route: any) => {
    await gate403;
    await errorDownload(403)(route);
  });
  await downloadButton(page).press("Enter");
  await expect(downloadButton(page)).toBeDisabled();
  const composer = page.getByPlaceholder("Введите запрос...");
  await composer.click();
  await expect(composer).toBeFocused();
  release403();
  await expect(alert).toContainText("Нет доступа к скачиванию");
  await expect(alert).not.toContainText("file_not_found");
  await expect(alert).not.toContainText("SENSITIVE DOWNLOAD DIAGNOSTIC");
  await expect(composer).toBeFocused();

  state.setDownload(okDownload());
  const [dl] = await Promise.all([
    page.waitForEvent("download"),
    downloadButton(page).click(),
  ]);
  expect(await downloadBytes(dl)).toEqual(CSV_BYTES);
  expect(state.downloadGets()).toBe(3);
  // The executed plan stays executed; no re-import, no writes.
  await expect(page.getByText("Готово", { exact: true })).toBeVisible();
  expect(state.executions()).toBe(1);
  expect(state.unexpectedWrites).toEqual([]);
});

test("generated-file download rejects a wrong-length 200 body", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedImport("store");
  const state = await fixture(page, "success", {
    importPlan,
    importResult,
  });
  await confirmAndSeeDownload(page);
  state.setDownload(async (route: any) =>
    route.fulfill({
      status: 200,
      contentType: "text/html",
      body: "<html>oops</html>",
    }),
  );
  const noDownload = page.waitForEvent("download", { timeout: 1500 });
  await downloadButton(page).click();
  await expect(page.getByRole("alert")).toContainText(
    "Не удалось скачать файл полностью",
  );
  await expect(noDownload).rejects.toThrow();
  expect(state.downloadGets()).toBe(1);
});

test("generated-file download aborts a late response on conversation switch", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedImport("store");
  const state = await fixture(page, "success", {
    importPlan,
    importResult,
  });
  await confirmAndSeeDownload(page);

  // Register the download counter BEFORE the request starts so a late
  // download can never escape the assertion.
  let downloadEvents = 0;
  page.on("download", () => downloadEvents++);
  // Wrap fetch only for the saved download URL: observe the supplied
  // AbortSignal but forward without it, simulating a transport that
  // ignores cancellation. Track object URL creation the same way.
  await page.evaluate((downloadPath) => {
    const w = window as any;
    w.__f3late = { aborted: false, settled: false, createdUrls: 0 };
    const origFetch = window.fetch.bind(window);
    const origCreate = URL.createObjectURL.bind(URL);
    URL.createObjectURL = (b: any) => {
      w.__f3late.createdUrls++;
      return origCreate(b);
    };
    window.fetch = ((input: any, init: any) => {
      const url = typeof input === "string" ? input : input?.url;
      if (typeof url === "string" && url.includes(downloadPath)) {
        const { signal, ...rest } = init ?? {};
        signal?.addEventListener(
          "abort",
          () => {
            w.__f3late.aborted = true;
          },
          { once: true },
        );
        return origFetch(input, rest).then((r: any) => {
          w.__f3late.settled = true;
          return r;
        });
      }
      return origFetch(input, init);
    }) as typeof fetch;
  }, "/store-storage/files/42/download");

  await page.emulateMedia({ reducedMotion: "reduce" });
  let release: () => void = () => {};
  const gate = new Promise<void>((r) => (release = r));
  state.setDownload(async (route: any) => {
    await gate;
    await route.fulfill({
      status: 200,
      contentType: "text/csv",
      body: CSV_BYTES,
    });
  });
  const clickDone = downloadButton(page).click();
  await expect(downloadButton(page)).toBeDisabled();
  await expect.poll(() => state.downloadGets()).toBe(1);
  // Reduced-motion: the busy spinner carries no rotation animation.
  await expect(downloadButton(page).locator("svg").first()).toHaveCSS(
    "animation-name",
    "none",
  );
  // A repeated click on the disabled control issues no second GET.
  await downloadButton(page).evaluate((el) =>
    (el as HTMLButtonElement).click(),
  );
  await page.waitForTimeout(100);
  expect(state.downloadGets()).toBe(1);

  // Switching conversation unmounts the child; reset() aborts the GET.
  await page.evaluate(async () => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/startNewConversation");
  });
  await page.waitForFunction(() => (window as any).__f3late.aborted);
  // The signal-stripped transport still resolves; the component must
  // ignore the late response — no download, no object URL, no alert.
  release();
  await page.waitForFunction(() => (window as any).__f3late.settled);
  await clickDone;
  await page.waitForTimeout(100);
  expect(downloadEvents).toBe(0);
  expect(
    await page.evaluate(() => (window as any).__f3late.createdUrls),
  ).toBe(0);
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page.getByTestId("ai-generated-file-download")).toHaveCount(0);
});

test("generated-file download absent for forged URL result", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedImport("store");
  await fixture(page, "success", {
    importPlan,
    importResult: { ...importResult, downloadUrl: "https://evil.example/x" },
  });
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(page.getByTestId("ai-generated-file-download")).toHaveCount(0);
  await reloadPlan(page);
  await expect(page.getByTestId("ai-generated-file-download")).toHaveCount(0);
});

test("generated-file download absent for a failed import", async ({
  page,
}) => {
  const { importPlan } = generatedImport("store");
  await fixture(page, "failed", { importPlan });
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(page.getByTestId("ai-generated-file-download")).toHaveCount(0);
  await reloadPlan(page);
  await expect(page.getByTestId("ai-generated-file-download")).toHaveCount(0);
});

// ---- F4: temporary generated-output download control ----

const BIN_BYTES = Buffer.from([0, 1, 2, 3, 250, 251]);
const TMP_ID = "c".repeat(32);
const TMP_ID2 = "e".repeat(32);
const TMP_SHA = "d".repeat(64);
const TMP_SHA2 = "f".repeat(64);
const TMP_URL = `/api/ai/chat/generated-files/${TMP_ID}/download`;
const TMP_URL2 = `/api/ai/chat/generated-files/${TMP_ID2}/download`;

function generatedTemporary({
  binary = true,
  expiresInMs = 24 * 3600 * 1000,
  url = TMP_URL,
  dropUrl = false,
  outcomeUnknown = false,
}: {
  binary?: boolean;
  expiresInMs?: number;
  url?: string | null;
  dropUrl?: boolean;
  outcomeUnknown?: boolean;
} = {}) {
  const expiresAtUtc = new Date(Date.now() + expiresInMs).toISOString();
  const outputs = [
    { fileName: "r.csv", contentType: "text/csv" },
    ...(binary
      ? [{ fileName: "b.bin", contentType: "application/octet-stream" }]
      : []),
  ];
  const artifact = (
    artifactId: string,
    fileName: string,
    contentType: string,
    length: number,
    sha256: string,
    downloadUrl: string | null,
  ) => ({
    artifactId,
    fileName,
    contentType,
    length,
    sha256,
    expiresAtUtc,
    ...(dropUrl ? {} : { downloadUrl }),
  });
  const importPlan = {
    proposalId,
    summary: "Рассчитать отчёт",
    steps: [
      {
        tool: "run_python",
        description: "Рассчитать отчёт",
        args: { code: "print(1)", outputs },
      },
    ],
  };
  const importResult = {
    exitCode: 0,
    stdout: "",
    stderr: "",
    error: null,
    ...(outcomeUnknown ? { outcomeUnknown: true } : {}),
    artifacts: [
      artifact(TMP_ID, "r.csv", "text/csv", CSV_BYTES.length, TMP_SHA, url),
      ...(binary
        ? [
            artifact(
              TMP_ID2,
              "b.bin",
              "application/octet-stream",
              BIN_BYTES.length,
              TMP_SHA2,
              TMP_URL2,
            ),
          ]
        : []),
    ],
  };
  return { importPlan, importResult };
}

const okTemporary =
  () => async (route: any) => {
    const url = route.request().url();
    const isBin = url.includes(TMP_ID2);
    await route.fulfill({
      status: 200,
      contentType: isBin ? "application/octet-stream" : "text/csv",
      headers: {
        "Content-Disposition": `attachment; filename="${isBin ? "b.bin" : "r.csv"}"`,
      },
      body: isBin ? BIN_BYTES : CSV_BYTES,
    });
  };

const temporaryRows = (page: Page) => page.getByTestId("ai-temporary-file");
const temporaryButton = (page: Page, index = 0) =>
  temporaryRows(page).nth(index).getByRole("button");

async function confirmAndSeeTemporary(page: Page, count = 1) {
  await expect(page.getByTestId("ai-generated-file-download")).toHaveCount(0);
  await page.getByRole("button", { name: "Выполнить", exact: true }).click();
  await expect(temporaryRows(page)).toHaveCount(count);
}

for (const viewport of [
  { width: 1280, height: 800, tag: "desktop" },
  { width: 390, height: 844, tag: "mobile" },
]) {
  test(`temporary-output download works after capture at ${viewport.width}px`, async ({
    page,
  }) => {
    const { importPlan, importResult } = generatedTemporary();
    const state = await fixture(page, "success", {
      importPlan,
      importResult,
    });
    state.setDownload(okTemporary());
    await page.setViewportSize(viewport);

    // Before confirmation: the declaration and 24-hour notice are visible,
    // but no download control exists yet.
    const preview = page.getByTestId("ai-temporary-outputs");
    await expect(preview).toContainText("Временные результаты расчёта");
    await expect(preview).toContainText("r.csv");
    await expect(preview).toContainText("b.bin");
    await expect(preview).toContainText("24 часа");
    await expect(
      page.getByTestId("ai-generated-file-download"),
    ).toHaveCount(0);

    await confirmAndSeeTemporary(page, 2);
    // "Temporary file" - never the saved-file label.
    await expect(temporaryRows(page).first()).toContainText("Временный файл");
    await expect(temporaryRows(page).first()).not.toContainText("Файл сохранён");
    await expect(temporaryRows(page).first()).toContainText("r.csv");
    await expect(temporaryRows(page).first()).toContainText(TMP_SHA);
    await expect(temporaryRows(page).first()).toContainText(
      "Можно скачать до",
    );
    await shot(page, `temporary-${viewport.tag}-ready`);

    const [dl] = await Promise.all([
      page.waitForEvent("download"),
      temporaryButton(page, 0).press("Enter"),
    ]);
    expect(dl.suggestedFilename()).toBe("r.csv");
    expect(await downloadBytes(dl)).toEqual(CSV_BYTES);
    const [dl2] = await Promise.all([
      page.waitForEvent("download"),
      temporaryButton(page, 1).press("Enter"),
    ]);
    expect(dl2.suggestedFilename()).toBe("b.bin");
    expect(await downloadBytes(dl2)).toEqual(BIN_BYTES);
    expect(state.downloadGets()).toBe(2);
    expect(state.executions()).toBe(1);
    expect(state.unexpectedWrites).toEqual([]);

    await reloadPlan(page);
    await expect(temporaryRows(page)).toHaveCount(2);
    await expect(temporaryRows(page).first()).toContainText("Временный файл");
    const [dl3] = await Promise.all([
      page.waitForEvent("download"),
      temporaryButton(page, 0).click(),
    ]);
    expect(await downloadBytes(dl3)).toEqual(CSV_BYTES);
    expect(state.downloadGets()).toBe(3);
    expect(state.executions()).toBe(1);
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
    await shot(page, `temporary-${viewport.tag}-reloaded`);
  });
}

test("temporary-output control expires on the clock without a GET or re-run", async ({
  page,
}) => {
  await page.clock.install({ time: Date.now() });
  const { importPlan, importResult } = generatedTemporary({
    binary: false,
    expiresInMs: 60_000,
  });
  const state = await fixture(page, "success", {
    importPlan,
    importResult,
  });
  state.setDownload(okTemporary());
  await confirmAndSeeTemporary(page, 1);
  await expect(temporaryRows(page)).toContainText("Можно скачать до");
  await expect(temporaryButton(page)).toBeEnabled();

  // Advancing past the deadline disables the control and shows the expired
  // state - no GET is made and the computation does not run again.
  await page.clock.runFor(61_000);
  await expect(temporaryButton(page)).toBeDisabled();
  await expect(temporaryRows(page)).toContainText(
    "Срок скачивания истёк",
  );
  await shot(page, "temporary-desktop-expired");
  expect(state.downloadGets()).toBe(0);
  expect(state.executions()).toBe(1);
  expect(state.unexpectedWrites).toEqual([]);

  // A reload after expiry keeps the control disabled and silent.
  await reloadPlan(page);
  await expect(temporaryRows(page)).toHaveCount(1);
  await expect(temporaryButton(page)).toBeDisabled();
  await expect(temporaryRows(page)).toContainText("Срок скачивания истёк");
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(temporaryRows(page)).toContainText("Срок скачивания истёк");
  const mobileOverflow = await page.evaluate(
    () =>
      document.documentElement.scrollWidth -
      document.documentElement.clientWidth,
  );
  expect(mobileOverflow).toBeLessThanOrEqual(0);
  await shot(page, "temporary-mobile-expired");
  expect(state.downloadGets()).toBe(0);
  expect(state.executions()).toBe(1);
});

test("temporary-output download sanitizes 404/410 and retries cleanly after 503", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedTemporary({ binary: false });
  const state = await fixture(page, "success", {
    importPlan,
    importResult,
  });
  await confirmAndSeeTemporary(page, 1);
  const alert = page.getByRole("alert");

  state.setDownload(errorDownload(404));
  await temporaryButton(page).click();
  await expect(alert).toContainText("Временный файл больше недоступен");
  await expect(alert).not.toContainText("SENSITIVE DOWNLOAD DIAGNOSTIC");
  await expect(alert).not.toContainText("file_not_found");

  state.setDownload(errorDownload(410));
  await temporaryButton(page).click();
  await expect(alert).toContainText("Временный файл больше недоступен");

  state.setDownload(errorDownload(503));
  await temporaryButton(page).click();
  await expect(alert).toContainText(
    "Не удалось скачать файл полностью",
  );
  await expect(alert).not.toContainText("SENSITIVE DOWNLOAD DIAGNOSTIC");

  // A retry is a plain GET of the same temporary route - nothing is
  // re-captured, re-imported, or re-executed.
  state.setDownload(okTemporary());
  const [dl] = await Promise.all([
    page.waitForEvent("download"),
    temporaryButton(page).click(),
  ]);
  expect(await downloadBytes(dl)).toEqual(CSV_BYTES);
  expect(state.downloadGets()).toBe(4);
  expect(state.executions()).toBe(1);
  expect(state.unexpectedWrites).toEqual([]);
});

test("temporary-output truncated capture remains downloadable after reload", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedTemporary({ binary: false });
  // The envelope the server emits when the raw capture result is truncated
  // for the model: the root verdict sits beside the promoted artifacts, and
  // the nested important/preview text is ignored.
  const truncatedResult = {
    truncated: true,
    originalLength: 90_000,
    important: {
      exitCode: 1,
      error: "nested lie",
      stdout: "x".repeat(1_000),
    },
    artifacts: importResult.artifacts,
    exitCode: 0,
    error: null,
    preview: `{"exitCode":1,"artifacts":[{"artifactId":"${TMP_ID2}"}]}`,
  };
  const state = await fixture(page, "success", {
    importPlan,
    importResult: truncatedResult,
  });
  state.setDownload(okTemporary());
  await confirmAndSeeTemporary(page, 1);
  const [dl] = await Promise.all([
    page.waitForEvent("download"),
    temporaryButton(page).click(),
  ]);
  expect(await downloadBytes(dl)).toEqual(CSV_BYTES);
  expect(state.executions()).toBe(1);
  expect(state.unexpectedWrites).toEqual([]);

  await reloadPlan(page);
  await expect(temporaryRows(page)).toHaveCount(1);
  const [dl2] = await Promise.all([
    page.waitForEvent("download"),
    temporaryButton(page).click(),
  ]);
  expect(await downloadBytes(dl2)).toEqual(CSV_BYTES);
  expect(state.downloadGets()).toBe(2);
  expect(state.executions()).toBe(1);
  expect(state.unexpectedWrites).toEqual([]);
});

test("temporary-output control absent for foreign, absent or unknown results", async ({
  page,
}) => {
  for (const variant of [
    generatedTemporary({ url: "https://evil.example/x" }),
    generatedTemporary({ dropUrl: true }),
    generatedTemporary({ outcomeUnknown: true }),
  ]) {
    await fixture(page, "success", {
      importPlan: variant.importPlan,
      importResult: variant.importResult,
    });
    await page.getByRole("button", { name: "Выполнить", exact: true }).click();
    await expect(
      page.getByTestId("ai-generated-file-download"),
    ).toHaveCount(0);
    await reloadPlan(page);
    await expect(
      page.getByTestId("ai-generated-file-download"),
    ).toHaveCount(0);
  }
});

test("temporary-output control never attaches to a pending recovery proposal", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedTemporary({ binary: false });
  const state = await fixture(page, "success", {
    importPlan,
    importResult,
    review: "recovery",
  });
  state.setDownload(okTemporary());
  await confirmAndSeeTemporary(page, 1);
  const cards = page.getByTestId("ai-plan");
  await expect(cards).toHaveCount(2);
  // A (executed capture) owns the control; pending proposal B does not,
  // even though it declares the same outputs.
  const cardA = cards.first();
  const cardB = cards.nth(1);
  await expect(cardA.getByTestId("ai-generated-file-download")).toHaveCount(1);
  await expect(cardB.getByTestId("ai-generated-file-download")).toHaveCount(0);
  await expect(cardB).toContainText("Временные результаты расчёта");
});

test("temporary-output download aborts a late response on conversation switch", async ({
  page,
}) => {
  const { importPlan, importResult } = generatedTemporary({ binary: false });
  const state = await fixture(page, "success", {
    importPlan,
    importResult,
  });
  await confirmAndSeeTemporary(page, 1);

  let downloadEvents = 0;
  page.on("download", () => downloadEvents++);
  await page.evaluate((downloadPath) => {
    const w = window as any;
    w.__f4late = { aborted: false, settled: false, createdUrls: 0 };
    const origFetch = window.fetch.bind(window);
    const origCreate = URL.createObjectURL.bind(URL);
    URL.createObjectURL = (b: any) => {
      w.__f4late.createdUrls++;
      return origCreate(b);
    };
    window.fetch = ((input: any, init: any) => {
      const url = typeof input === "string" ? input : input?.url;
      if (typeof url === "string" && url.includes(downloadPath)) {
        const { signal, ...rest } = init ?? {};
        signal?.addEventListener(
          "abort",
          () => {
            w.__f4late.aborted = true;
          },
          { once: true },
        );
        return origFetch(input, rest).then((r: any) => {
          w.__f4late.settled = true;
          return r;
        });
      }
      return origFetch(input, init);
    }) as typeof fetch;
  }, `/ai/chat/generated-files/${TMP_ID}/download`);

  let release: () => void = () => {};
  const gate = new Promise<void>((r) => (release = r));
  state.setDownload(async (route: any) => {
    await gate;
    await route.fulfill({
      status: 200,
      contentType: "text/csv",
      body: CSV_BYTES,
    });
  });
  const clickDone = temporaryButton(page).click();
  await expect(temporaryButton(page)).toBeDisabled();
  await expect.poll(() => state.downloadGets()).toBe(1);

  // Switching conversation unmounts the child; reset() aborts the GET.
  await page.evaluate(async () => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/startNewConversation");
  });
  await page.waitForFunction(() => (window as any).__f4late.aborted);
  // The signal-stripped transport still resolves; the component must ignore
  // the late response - no download, no object URL, no alert.
  release();
  await page.waitForFunction(() => (window as any).__f4late.settled);
  await clickDone;
  await page.waitForTimeout(100);
  expect(downloadEvents).toBe(0);
  expect(
    await page.evaluate(() => (window as any).__f4late.createdUrls),
  ).toBe(0);
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page.getByTestId("ai-generated-file-download")).toHaveCount(0);
});
