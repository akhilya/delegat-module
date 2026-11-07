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
          plan,
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

async function reloadPlan(page: Page) {
  await page.reload();
  await page.waitForFunction(
    () => !!(document.querySelector("#app") as any)?.__vue_app__,
  );
  await page.evaluate(async (sid) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/loadConversation", sid);
    store.commit("aiChat/setOpen", true);
  }, sessionId);
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
