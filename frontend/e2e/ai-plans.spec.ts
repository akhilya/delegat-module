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
            name: "platform_request",
            input: JSON.stringify(plan.steps[0].args),
            summary: ["failed", "partial"].includes(outcome)
              ? "HTTP 400"
              : "Accepted",
            result:
              outcome === "queued"
                ? { operationId: "op-plan", status: "pending" }
                : ["failed", "partial"].includes(outcome)
                  ? { code: "bad_request", status: 400 }
                  : { success: true },
          },
        ]
      : [];
  const reviewPlan = {
    ...plan,
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
            ? { ...plan, generatedFileImports: options.generatedImports }
            : plan,
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
        outcome === "queued"
          ? { operationId: "op-plan", status: "pending" }
          : ["failed", "partial"].includes(outcome)
            ? { code: "bad_request", status: 400 }
            : { success: true };
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
            name: "platform_request",
            status: "running",
            input: JSON.stringify(plan.steps[0].args),
          }) +
          sse("tool", {
            id: "exec-1",
            name: "platform_request",
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
