import { expect, test, type Page } from "@playwright/test";

async function fixture(
  page: Page,
  options: { empty?: boolean; plan?: boolean; stopFails?: boolean } = {},
) {
  let running = true;
  let stopped = false;
  let stopRequests = 0;
  let followup: any = null;
  const unexpectedWrites: string[] = [];
  const title = "Работающий диалог";
  const plan = {
    proposalId: "live-proposal",
    summary: "Обновить карточку",
    steps: [
      {
        tool: "platform_request",
        description: "Обновить карточку",
        args: { method: "PATCH", path: "/products/1", body: {} },
      },
    ],
  };
  const conversation = (sid: string) => ({
    sessionId: sid,
    title: sid === "live-a" ? title : "Другой диалог",
    generating: sid === "live-a" && running,
    messages: [
      {
        dbId: sid === "live-a" ? 1 : 3,
        role: "user",
        content: sid === "live-a" ? "Проверь карточки" : "Другой вопрос",
      },
      ...(sid === "other"
        ? [{ dbId: 4, role: "assistant", content: "Другой ответ" }]
        : options.empty && running
          ? []
          : [
              {
                dbId: 2,
                role: "assistant",
                content: options.empty ? "" : "Проверяю карточки.",
                toolCallsJson: JSON.stringify({
                  _ui: "assistantTurn",
                  turnStopped: stopped,
                  ...(options.plan
                    ? { plan, planConfirmed: true, planExecStart: 1 }
                    : {}),
                  tools: options.empty
                    ? []
                    : [
                        {
                          id: "tool-1",
                          name: "search_catalog",
                          input: "{}",
                          status: stopped
                            ? "stopped"
                            : running
                              ? "running"
                              : "done",
                        },
                      ],
                  timeline: [
                    ...(options.plan
                      ? [{ type: "text", text: "План готов." }]
                      : []),
                    ...(options.empty
                      ? []
                      : [
                          { type: "text", text: "Проверяю карточки." },
                          { type: "tool", id: "tool-1" },
                        ]),
                  ],
                }),
              },
            ]),
      ...(sid === "live-a" && followup
        ? [
            { dbId: 5, role: "user", content: "Новый вопрос" },
            { dbId: 6, role: "assistant", content: "Новый ответ." },
          ]
        : []),
    ],
  });
  await page.addInitScript(
    ({ empty }) => {
      localStorage.setItem("delegat.locale", "ru");
      localStorage.setItem("mobile-warning-dismissed", "1");
      const encode = (type: string, data: unknown) =>
        new TextEncoder().encode(`data: ${JSON.stringify({ type, data })}\n\n`);
      const chunks = empty
        ? []
        : [
            encode("text", { delta: "Проверяю карточки." }),
            encode("tool", {
              id: "tool-1",
              name: "search_catalog",
              status: "running",
              input: "{}",
            }),
          ];
      const listeners = new Set<ReadableStreamDefaultController>();
      const originalFetch = window.fetch.bind(window);
      (window as any).__liveTest = {
        subscriptions: 0,
        emit(type: string, data: unknown) {
          const chunk = encode(type, data);
          chunks.push(chunk);
          for (const listener of listeners) listener.enqueue(chunk);
          if (type === "done" || type === "stopped") {
            for (const listener of listeners) listener.close();
            listeners.clear();
          }
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
          url.searchParams.get("sessionId") === "live-a"
        ) {
          (window as any).__liveTest.subscriptions++;
          return new Response(
            new ReadableStream({
              start(controller) {
                for (const chunk of chunks) controller.enqueue(chunk);
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
    { empty: !!options.empty },
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
        userName: "live-test",
        isAdmin: true,
        aiEnabled: true,
        selectedStoreId: 1,
        permissions: ["ai.use", "catalog.manage", "files.manage"],
      };
    else if (path === "/health") body = { status: "healthy", version: "1.6.0" };
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
    else if (path === "/users/me/memberships")
      body = [
        {
          id: 1,
          name: "Test store",
          onboardingCompletedAt: "2026-01-01T00:00:00Z",
        },
      ];
    else if (path === "/products") body = { items: [], totalCount: 0 };
    else if (path === "/settings/branding") body = {};
    else if (path === "/ai/models/picker")
      body = [{ id: 1, name: "Test model", isEnabled: true, isDefault: true }];
    else if (path === "/ai/hosted-ai") body = { configured: false };
    else if (path === "/ai/conversations/latest") body = null;
    else if (path === "/ai/conversations")
      body = [
        {
          sessionId: "live-a",
          title,
          generating: running,
          stopped,
          messageCount: 2,
          lastMessageAt: new Date().toISOString(),
        },
        {
          sessionId: "other",
          title: "Другой диалог",
          generating: false,
          messageCount: 2,
          lastMessageAt: new Date().toISOString(),
        },
      ];
    else if (path === "/ai/conversations/live-a") body = conversation("live-a");
    else if (path === "/ai/conversations/other") body = conversation("other");
    else if (path === "/ai/stop") {
      stopRequests++;
      expect(request.postDataJSON()).toEqual({ sessionId: "live-a" });
      if (options.stopFails) {
        await route.fulfill({ status: 500, json: { code: "internal_error" } });
        return;
      }
      running = false;
      stopped = true;
      body = { stopped: true };
    } else if (path === "/ai/conversations/messages") body = { saved: [] };
    else if (path === "/ai/chat") {
      followup = request.postDataJSON();
      await route.fulfill({
        contentType: "text/event-stream",
        body: 'data: {"type":"text","data":{"delta":"Новый ответ."}}\n\ndata: {"type":"done","data":{}}\n\n',
      });
      return;
    } else if (request.method() !== "GET")
      unexpectedWrites.push(`${request.method()} ${path}`);
    await route.fulfill({ json: body });
  });
  await page.goto("/products");
  await page.waitForFunction(
    () => !!(document.querySelector("#app") as any)?.__vue_app__,
  );
  await page.evaluate(async () => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/loadConversation", "live-a");
    store.commit("aiChat/setOpen", true);
  });
  await expect(
    page.getByRole("button", { name: "Остановить", exact: false }),
  ).toBeVisible();
  return {
    stopRequests: () => stopRequests,
    followup: () => followup,
    unexpectedWrites,
    async finish(stop = false) {
      running = false;
      stopped = stop;
      await page.evaluate(
        (stop) =>
          (window as any).__liveTest.emit(stop ? "stopped" : "done", {}),
        stop,
      );
    },
  };
}

test("history shows background activity and returning to another running chat does not wait for completion", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.getByRole("button", { name: "История диалогов" }).click();
  await expect(
    page.getByText("Ассистент работает…", { exact: true }),
  ).toBeVisible();
  await page.getByText("Другой диалог", { exact: true }).click();
  await expect(page.getByText("Другой ответ", { exact: true })).toBeVisible();
  expect(state.stopRequests()).toBe(0);
  await page.getByRole("button", { name: "История диалогов" }).click();
  await expect(
    page.getByText("Ассистент работает…", { exact: true }),
  ).toBeVisible();
  await page.getByText("Работающий диалог", { exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Остановить", exact: false }),
  ).toBeVisible();
  await expect(
    page.getByText("Проверяю карточки.", { exact: true }),
  ).toHaveCount(1);
  expect(
    await page.evaluate(() => (window as any).__liveTest.subscriptions),
  ).toBe(2);
  await state.finish();
  await expect(
    page.getByRole("button", { name: "Остановить", exact: false }),
  ).toHaveCount(0);
  expect(state.stopRequests()).toBe(0);
  expect(state.unexpectedWrites).toEqual([]);
});

test("opening history and going back keeps the existing subscription", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.getByRole("button", { name: "История диалогов" }).click();
  await page.getByRole("button", { name: "Назад к чату" }).click();
  await expect(
    page.getByRole("button", { name: "Остановить", exact: false }),
  ).toBeVisible();
  expect(
    await page.evaluate(() => (window as any).__liveTest.subscriptions),
  ).toBe(1);
  expect(state.stopRequests()).toBe(0);
});

for (const options of [{}, { empty: true }, { plan: true }]) {
  test(`stop is visible and persisted ${JSON.stringify(options)}`, async ({
    page,
  }) => {
    const state = await fixture(page, options);
    await page
      .getByRole("button", { name: "Остановить", exact: false })
      .click();
    await expect(page.getByTestId("ai-turn-stopped")).toContainText(
      "Остановлено пользователем",
    );
    await expect(
      page.getByRole("button", { name: "Остановить", exact: false }),
    ).toHaveCount(0);
    if (options.plan)
      await expect(page.getByTestId("ai-plan").getByRole("status")).toHaveText(
        "Остановлено пользователем",
      );
    await page.getByRole("button", { name: "История диалогов" }).click();
    await expect(
      page.getByText("Остановлено пользователем", { exact: true }),
    ).toBeVisible();
    await page.getByText("Другой диалог", { exact: true }).click();
    await page.getByRole("button", { name: "История диалогов" }).click();
    await page.getByText("Работающий диалог", { exact: true }).click();
    await expect(page.getByTestId("ai-turn-stopped")).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Выполнить", exact: true }),
    ).toHaveCount(0);
    await page.reload();
    await page.waitForFunction(
      () => !!(document.querySelector("#app") as any)?.__vue_app__,
    );
    await page.evaluate(async () => {
      const store = (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store;
      await store.dispatch("aiChat/loadConversation", "live-a");
      store.commit("aiChat/setOpen", true);
    });
    await expect(page.getByTestId("ai-turn-stopped")).toBeVisible();
    expect(state.stopRequests()).toBe(1);
    expect(state.unexpectedWrites).toEqual([]);
  });
}

test("a server stop event is rendered without a local Stop click", async ({
  page,
}) => {
  const state = await fixture(page);
  await state.finish(true);
  await expect(page.getByTestId("ai-turn-stopped")).toBeVisible();
  expect(state.stopRequests()).toBe(0);
});

test("starting a new chat does not stop a background reply, and history refreshes its activity", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.getByRole("button", { name: "Новый диалог", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Остановить", exact: false }),
  ).toHaveCount(0);
  await page.getByRole("button", { name: "История диалогов" }).click();
  await expect(
    page.getByText("Ассистент работает…", { exact: true }),
  ).toBeVisible();
  await state.finish();
  await expect(
    page.getByText("Ассистент работает…", { exact: true }),
  ).toHaveCount(0, { timeout: 8000 });
  expect(state.stopRequests()).toBe(0);
  await page.getByText("Работающий диалог", { exact: true }).click();
  await expect(
    page.getByText("Проверяю карточки.", { exact: true }),
  ).toBeVisible();
});

test("a new question after Stop keeps the stopped reply and sends no dangling tool calls", async ({
  page,
}) => {
  const state = await fixture(page);
  await page.getByRole("button", { name: "Остановить", exact: false }).click();
  await expect(page.getByTestId("ai-turn-stopped")).toBeVisible();
  const input = page.getByPlaceholder("Введите запрос...");
  await input.fill("Новый вопрос");
  await input.press("Enter");
  await expect(page.getByText("Новый ответ.", { exact: true })).toBeVisible();
  const request = state.followup();
  expect(request.messages.at(-1)).toEqual({
    role: "user",
    content: "Новый вопрос",
  });
  expect(
    request.messages.some((message: any) => Array.isArray(message.content)),
  ).toBe(false);
  expect(
    request.messages.some((message: any) =>
      message.content.includes("Reply stopped by the user"),
    ),
  ).toBe(true);
  await expect(page.getByTestId("ai-turn-stopped")).toHaveCount(1);
  expect(state.stopRequests()).toBe(1);
  expect(state.unexpectedWrites).toEqual([]);
});

test("failed Stop does not falsely mark the reply as stopped", async ({
  page,
}) => {
  const state = await fixture(page, { stopFails: true });
  await page.getByRole("button", { name: "Остановить", exact: false }).click();
  await expect(
    page.getByText(/Не удалось подтвердить остановку/),
  ).toBeVisible();
  await expect(page.getByTestId("ai-turn-stopped")).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "Остановить", exact: false }),
  ).toBeEnabled();
  await state.finish();
  await expect(
    page.getByRole("button", { name: "Остановить", exact: false }),
  ).toHaveCount(0);
});
