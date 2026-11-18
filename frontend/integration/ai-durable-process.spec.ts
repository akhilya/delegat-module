import { test, expect } from "@playwright/test";
import { spawn } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Browser-to-real-API durable coverage: the built SPA is served by an owned
 * test-only console host that also runs the real AI controllers, durable
 * worker/store, shared executor and SSE over one file-backed SQLite database.
 * AI flow requests (chat, execute-plan, stop, conversations incl. SSE) reach
 * the real controllers unchanged - only unrelated bootstrap metadata is
 * fulfilled (auth/me, memberships, stores, products, models picker, hosted-ai,
 * branding). The runner kills only its own child process and waits for the
 * real lease deadline without touching the database clock.
 */

const repoRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
  "..",
);
const HOST_DLL = path.join(
  repoRoot,
  "backend.AiHttpTestHost",
  "bin",
  "Debug",
  "net8.0",
  "Delegat.AiHttpTestHost.dll",
);
const UI_DIST = path.join(repoRoot, "frontend", "dist");
const ADMIN_BEARER = "fixture-admin";

class LocalHost {
  static async create() {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "delegat-ai-browser-"));
    const token = crypto.randomBytes(32).toString("hex");
    fs.writeFileSync(path.join(dir, "owner.token"), token, "utf8");
    for (const name of ["native-calls.ndjson", "model-calls.ndjson"])
      fs.writeFileSync(path.join(dir, name), "", "utf8");
    const host = new LocalHost(dir, token);
    host.session = crypto.randomUUID().replaceAll("-", "");
    return host;
  }

  constructor(dir, token) {
    this.dir = dir;
    this.token = token;
    this.session = null;
    this.port = null;
    this.url = null;
    this.ready = null;
    this.child = null;
    this.stdout = "";
    this.stderr = "";
    this._exitGuard = null;
  }

  async start(pauseMode = "none") {
    if (!fs.existsSync(HOST_DLL))
      throw new Error(
        `Built test host missing: ${HOST_DLL}. ` +
          "Run: dotnet build backend.AiHttpTestHost/Delegat.AiHttpTestHost.csproj",
      );
    if (!fs.existsSync(path.join(UI_DIST, "index.html")))
      throw new Error(
        `Built frontend missing: ${path.join(UI_DIST, "index.html")}. ` +
          "Run: MSYS_NO_PATHCONV=1 MSYS2_ENV_CONV_EXCL=VITE_API_SERVER " +
          "VITE_API_SERVER=/api npm --prefix frontend run build",
      );
    if (this.child) throw new Error("A host child is already running.");
    const child = spawn(
      "dotnet",
      [HOST_DLL, this.dir, UI_DIST, this.session, String(this.port ?? 0), pauseMode],
      {
        cwd: repoRoot,
        stdio: ["ignore", "pipe", "pipe"],
        env: { ...process.env },
      },
    );
    this.child = child;
    this.stdout = "";
    this.stderr = "";
    let earlyExit = null;
    let startupError = null;
    child.stdout.on("data", (d) => (this.stdout += d));
    child.stderr.on("data", (d) => (this.stderr += d));
    child.once("error", (error) => {
      startupError = error;
    });
    child.once("exit", (code, signal) => {
      earlyExit = { code, signal };
    });
    this._exitGuard = () => {
      try {
        if (LocalHost._isRunning(child)) child.kill("SIGKILL");
      } catch {
        /* already gone */
      }
    };
    process.once("exit", this._exitGuard);

    const readyPath = path.join(this.dir, "ready.json");
    const deadline = Date.now() + 30_000;
    while (Date.now() < deadline) {
      if (startupError)
        throw new Error(`Test host failed to spawn: ${startupError}`);
      if (earlyExit)
        throw new Error(
          `Test host exited early: ${JSON.stringify(earlyExit)}\n` +
            `stdout:\n${this.stdout}\nstderr:\n${this.stderr}`,
        );
      try {
        const ready = JSON.parse(fs.readFileSync(readyPath, "utf8"));
        if (ready.processId === child.pid) {
          const url = new URL(ready.url);
          if (url.hostname !== "127.0.0.1" && url.hostname !== "localhost")
            throw new Error(`Unexpected host URL: ${ready.url}`);
          this.ready = ready;
          this.url = `${url.protocol}//${url.hostname}:${url.port}`;
          this.port = Number(url.port);
          if (!Number.isInteger(this.port) || this.port <= 0)
            throw new Error(`Unexpected host port in ${ready.url}`);
          return;
        }
      } catch (err) {
        if (err.code !== "ENOENT" && !(err instanceof SyntaxError)) throw err;
      }
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(
      `Test host did not become ready in 30s.\n` +
        `stdout:\n${this.stdout}\nstderr:\n${this.stderr}`,
    );
  }

  async crash() {
    const child = this.child;
    if (!child || !LocalHost._isRunning(child)) return;
    // Ownership and the exit guard are released only after the exit is
    // confirmed; a kill or wait failure throws while the child may live.
    child.kill("SIGKILL");
    await this._awaitExit(child, 10_000);
    this.child = null;
    this._dropExitGuard();
  }

  async dispose() {
    const child = this.child;
    if (child && LocalHost._isRunning(child)) {
      try {
        await fetch(`${this.url}/__test/shutdown`, {
          method: "POST",
          headers: { "X-Ai-Test-Control": this.token },
          signal: AbortSignal.timeout(5000),
        });
      } catch {
        /* already gone or not listening */
      }
      if (LocalHost._isRunning(child)) {
        try {
          await this._awaitExit(child, 5000);
        } catch {
          // Graceful stop failed: escalate to an exact SIGKILL on the
          // still-owned child and give it a bounded window to exit. A
          // failure here propagates with the child handle, exit guard and
          // state directory retained for inspection.
          if (LocalHost._isRunning(child)) child.kill("SIGKILL");
          await this._awaitExit(child, 10_000);
        }
      }
    }
    this.child = null;
    this._dropExitGuard();
    fs.rmSync(this.dir, { recursive: true, force: true });
  }

  // A child that failed to spawn has no PID; one that exited has a code.
  static _isRunning(child) {
    return (
      child.pid !== undefined &&
      child.exitCode === null &&
      child.signalCode === null
    );
  }

  _dropExitGuard() {
    if (this._exitGuard) {
      process.removeListener("exit", this._exitGuard);
      this._exitGuard = null;
    }
  }

  _awaitExit(child, ms) {
    if (!LocalHost._isRunning(child)) return Promise.resolve();
    return new Promise((resolve, reject) => {
      const cleanup = () => {
        clearTimeout(timer);
        child.removeListener("exit", exited);
        child.removeListener("error", failed);
      };
      const exited = () => {
        cleanup();
        resolve();
      };
      const failed = (error) => {
        cleanup();
        reject(error);
      };
      const timer = setTimeout(() => {
        cleanup();
        reject(
          new Error(
            `Owned test host PID ${child.pid} did not exit within ${ms}ms`,
          ),
        );
      }, ms);
      child.once("exit", exited);
      child.once("error", failed);
    });
  }

  async state() {
    const res = await fetch(`${this.url}/__test/state`, {
      headers: { "X-Ai-Test-Control": this.token },
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) throw new Error(`state endpoint returned ${res.status}`);
    return res.json();
  }

  async history() {
    const res = await fetch(
      `${this.url}/api/ai/conversations/${this.session}`,
      {
        headers: {
          authorization: `Bearer ${ADMIN_BEARER}`,
          cookie: `selected_store_id=${this.ready.storeId}`,
        },
        signal: AbortSignal.timeout(5000),
      },
    );
    if (!res.ok) throw new Error(`history returned ${res.status}`);
    return res.json();
  }

  ledger(kind) {
    const file = path.join(this.dir, `${kind}-calls.ndjson`);
    if (!fs.existsSync(file)) return [];
    return fs
      .readFileSync(file, "utf8")
      .split("\n")
      .filter((line) => line.length > 0)
      .map((line) => JSON.parse(line));
  }
}

function isFlowPath(p) {
  return (
    /^\/api\/ai\/(chat|conversations)(\/|$)/.test(p) ||
    p === "/api/ai/execute-plan" ||
    p === "/api/ai/stop"
  );
}

/**
 * Route policy: AI flow requests are continued to the real controllers with
 * only the fixture bearer injected (streaming is forwarded untouched); the
 * listed bootstrap endpoints are fulfilled metadata; every other same-origin
 * request continues to the real host (real 404 for unknown API paths) and all
 * off-origin traffic is aborted.
 */
async function bootstrap(page, host) {
  const counters = {
    executePosts: 0,
    streamGets: 0,
    offOrigin: 0,
    passthroughApi: [],
    failed: [],
    responses: [],
  };
  const origin = new URL(host.url).origin;
  page.on("requestfailed", (request) =>
    counters.failed.push(
      `${request.method()} ${request.url()} :: ${request.failure()?.errorText}`,
    ),
  );
  page.on("response", (response) => {
    const url = new URL(response.url());
    if (url.pathname.startsWith("/api/"))
      counters.responses.push(
        `${response.status()} ${response.request().method()} ${url.pathname}`,
      );
  });
  await page.addInitScript(() => {
    localStorage.setItem("delegat.locale", "ru");
    localStorage.setItem("mobile-warning-dismissed", "1");
  });
  await page.context().addCookies([
    {
      name: "selected_store_id",
      value: String(host.ready.storeId),
      url: host.url,
    },
  ]);
  await page.route("**/*", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.origin === origin && url.pathname.startsWith("/api/"))
      counters.responses.push(`req ${request.method()} ${url.pathname}`);
    if (url.origin !== origin) {
      counters.offOrigin++;
      return route.abort();
    }
    const p = url.pathname;
    if (isFlowPath(p)) {
      if (request.method() === "POST" && p === "/api/ai/execute-plan")
        counters.executePosts++;
      if (request.method() === "GET" && p === "/api/ai/chat/stream")
        counters.streamGets++;
      return route.continue({
        headers: {
          ...request.headers(),
          authorization: `Bearer ${ADMIN_BEARER}`,
        },
      });
    }
    if (p.startsWith("/hubs/")) return route.abort();
    if (!p.startsWith("/api/")) return route.continue();
    const api = p.replace(/^\/api/, "").replace(/\/+$/, "") || "/";
    let body;
    if (api === "/auth/isAuthenticated") body = { isAuthenticated: true };
    else if (api === "/auth/me")
      body = {
        id: host.ready.userId,
        userName: "agent-eval",
        isAdmin: true,
        permissions: [
          "ai.use",
          "catalog.manage",
          "files.manage",
          "marketplace.manage",
        ],
        selectedStoreId: host.ready.storeId,
        aiEnabled: true,
      };
    else if (api === "/health")
      body = { status: "healthy", version: "local-durable" };
    else if (api === "/users/me/memberships")
      body = [
        {
          id: host.ready.storeId,
          name: "Eval store",
          onboardingCompletedAt: "2026-01-01T00:00:00Z",
        },
      ];
    else if (api === "/stores")
      body = {
        items: [
          {
            id: host.ready.storeId,
            name: "Eval store",
            onboardingCompletedAt: "2026-01-01T00:00:00Z",
          },
        ],
        totalCount: 1,
      };
    else if (api === "/products") body = { items: [], totalCount: 0 };
    else if (api === "/ai/models/picker")
      body = [{ id: 1, name: "scripted", isEnabled: true, isDefault: true }];
    else if (api === "/ai/hosted-ai") body = { configured: false };
    else if (api === "/settings/branding") body = {};
    else {
      counters.passthroughApi.push(`${request.method()} ${api}`);
      return route.continue();
    }
    return route.fulfill({ json: body });
  });
  return counters;
}

async function openConversation(page, host, { reload = false } = {}) {
  if (reload) await page.reload();
  else await page.goto(`${host.url}/products`);
  await page.waitForFunction(
    () =>
      !!document.querySelector("#app")?.__vue_app__?.config?.globalProperties
        ?.$store,
  );
  await page.evaluate(async (sid) => {
    const store = document.querySelector("#app").__vue_app__.config
      .globalProperties.$store;
    await store.dispatch("aiChat/loadConversation", sid);
    store.commit("aiChat/setOpen", true);
  }, host.session);
}

/** Loads the conversation and asserts the store actually received messages. */
async function openConversationStrict(page, host, counters, options) {
  await openConversation(page, host, options);
  await page
    .waitForFunction(
      (sid) => {
        const store = document.querySelector("#app")?.__vue_app__?.config
          ?.globalProperties?.$store;
        return (
          store?.state?.aiChat?.sessionId === sid &&
          (store?.state?.aiChat?.messages || []).length > 0
        );
      },
      host.session,
      { timeout: 10_000 },
    )
    .catch(async (error) => {
      const state = await page
        .evaluate(() => {
          const store = document.querySelector("#app")?.__vue_app__?.config
            ?.globalProperties?.$store;
          return {
            sessionId: store?.state?.aiChat?.sessionId ?? null,
            messageCount: store?.state?.aiChat?.messages?.length ?? null,
          };
        })
        .catch((e) => ({ readError: String(e) }));
      throw new Error(
        `conversation did not load; store=${JSON.stringify(state)} ` +
          `diagnostics=${JSON.stringify(counters)}`,
        { cause: error },
      );
    });
}

async function snapshotMessages(page) {
  return page.evaluate(() => {
    const store = document.querySelector("#app").__vue_app__.config
      .globalProperties.$store;
    return (store.state.aiChat.messages || []).map((m) => ({
      role: m.role,
      content: m.content,
      phase: m.phase ?? null,
      messageId: m.messageId ?? null,
      runId: m.runId ?? null,
      dbId: m.dbId ?? null,
      executionProposalId: m.executionProposalId ?? null,
      proposalId: m.plan?.proposalId ?? null,
      planExecuted: !!m.planExecuted,
      executionOutcome: m.executionOutcome ?? null,
      messageState: m.messageState ?? null,
      lastEventSequence: m.lastEventSequence ?? null,
      done: !!m.done,
      toolIds: (m.toolCalls || []).map((t) => t.id),
      timelineToolIds: (m.timeline || [])
        .filter((i) => i.type === "tool")
        .map((i) => i.id),
    }));
  });
}

async function uiLoading(page) {
  return page.evaluate(
    () =>
      document.querySelector("#app").__vue_app__.config.globalProperties.$store
        .state.aiChat.isLoading,
  );
}

// Waits for a live-consumed UI message of the given phase to reach a terminal
// state with the stream closed - proves the real SSE was consumed before any
// further reload, not just persisted history.
async function waitUiTerminal(page, phase, state) {
  await expect
    .poll(async () => {
      const messages = await snapshotMessages(page);
      const message = messages.find(
        (m) => m.role === "assistant" && m.phase === phase,
      );
      return {
        done: message?.done ?? false,
        state: message?.messageState ?? null,
        loading: await uiLoading(page),
      };
    })
    .toEqual({ done: true, state, loading: false });
}

function lastJob(state) {
  return state.jobs[state.jobs.length - 1];
}

function parseJson(text, fallback) {
  return text == null ? fallback : JSON.parse(text);
}

function historyRows(conversation) {
  return (conversation.messages || []).map((m) => {
    const turn = m.toolCallsJson ? JSON.parse(m.toolCallsJson) : {};
    return {
      dbId: m.dbId,
      role: m.role,
      content: m.content,
      messageId: turn.messageId ?? null,
      runId: turn.runId ?? null,
      phase: turn.phase ?? null,
      proposalId: turn.plan?.proposalId ?? null,
      executionProposalId: turn.executionProposalId ?? null,
      planExecuted: !!turn.planExecuted,
      executionOutcome: turn.executionOutcome ?? null,
      messageState: turn.messageState ?? null,
      timelineTools: (turn.timeline || [])
        .filter((e) => e.type === "tool")
        .map((e) => e.id),
      text: (turn.timeline || [])
        .filter((e) => e.type === "text")
        .map((e) => e.text)
        .join(""),
    };
  });
}

function executionRow(rows) {
  return rows.find((m) => m.phase === "execution");
}

function reviewRow(rows) {
  return rows.find((m) => m.phase === "review");
}

async function waitJobStatus(host, status, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let state;
  do {
    state = await host.state();
    if (lastJob(state)?.status === status) return lastJob(state);
    await new Promise((resolve) => setTimeout(resolve, 1000));
  } while (Date.now() < deadline);
  throw new Error(
    `Job never reached ${status}: ${JSON.stringify(lastJob(state))}`,
  );
}

for (const viewport of [
  { width: 1280, height: 900 },
  { width: 390, height: 844 },
]) {
  test(`seeded plan executes over the real API with a persisted review (${viewport.width}x${viewport.height})`, async ({
    page,
  }) => {
    await page.setViewportSize(viewport);
    const host = await LocalHost.create();
    try {
      await host.start("none");
      const counters = await bootstrap(page, host);
      await openConversationStrict(page, host, counters);
      const execute = page.getByRole("button", {
        name: "Выполнить",
        exact: true,
      });
      try {
        await expect(execute).toBeEnabled({ timeout: 10_000 });
      } catch (error) {
        throw new Error(
          `execute button unavailable; diagnostics=${JSON.stringify(counters)}`,
          { cause: error },
        );
      }
      await execute.click();
      await expect(
        page.getByTestId("ai-plan").getByRole("status"),
      ).toHaveText("Готово");
      await expect(page.getByText("HTTP reply")).toBeVisible();
      await waitJobStatus(host, "completed", 30_000);
      await waitUiTerminal(page, "review", "completed");

      const messages = await snapshotMessages(page);
      const assistants = messages.filter((m) => m.role === "assistant");
      const canonicalIds = new Set(
        assistants.map((m) => m.messageId).filter(Boolean),
      );
      expect(canonicalIds.size).toBe(2);
      const execution = executionRow(assistants);
      const review = reviewRow(assistants);
      expect(execution.proposalId).toBeTruthy();
      expect(execution.planExecuted).toBe(true);
      expect(review.executionProposalId).toBe(execution.proposalId);
      expect(review.runId).toBe(execution.runId);
      expect(review.messageId).not.toBe(execution.messageId);
      expect(review.dbId).not.toBe(execution.dbId);
      expect(messages.indexOf(review)).toBeGreaterThan(
        messages.indexOf(execution),
      );
      expect(review.content).toContain("HTTP reply");
      await expect(execute).toHaveCount(0);

      const native = host.ledger("native");
      expect(native.map((r) => r.body?.name)).toEqual(["first", "second"]);
      expect(host.ledger("model")).toHaveLength(1);
      expect(counters.executePosts).toBe(1);

      const firstPid = (await host.state()).processId;
      await openConversation(page, host, { reload: true });
      const reloaded = await snapshotMessages(page);
      expect(reloaded.map((m) => m.dbId)).toEqual(
        messages.map((m) => m.dbId),
      );
      expect(
        reloaded.filter((m) => m.role === "assistant").map((m) => m.messageId),
      ).toEqual(assistants.map((m) => m.messageId));
      const reviewAgain = reviewRow(reloaded.filter((m) => m.role === "assistant"));
      expect(reviewAgain.content).toContain("HTTP reply");
      expect(
        reloaded.filter(
          (m) => m.role === "assistant" && m.content.includes("HTTP reply"),
        ),
      ).toHaveLength(1);
      expect(host.ledger("native").map((r) => r.body?.name)).toEqual([
        "first",
        "second",
      ]);
      expect(host.ledger("model")).toHaveLength(1);
      expect(counters.executePosts).toBe(1);
      expect((await host.state()).processId).toBe(firstPid);
    } finally {
      try {
        await page.close();
      } catch {
        /* already closed */
      }
      await host.dispose();
    }
  });
}

test("crash after committed first result resumes only the second step through the real browser API", async ({
  page,
}) => {
  const host = await LocalHost.create();
  try {
    await host.start("first-done");
    const counters = await bootstrap(page, host);
    await openConversation(page, host);
    await page
      .getByRole("button", { name: "Выполнить", exact: true })
      .click();

    let state;
    await expect
      .poll(async () => (state = await host.state()).paused)
      .toBe(true);
    const firstPid = state.processId;
    const job = lastJob(state);
    expect(parseJson(job.checkpointsJson, [])).toHaveLength(1);
    expect(job.startedStep).toBeNull();
    expect(host.ledger("native").map((r) => r.body?.name)).toEqual(["first"]);
    expect(host.ledger("model")).toHaveLength(0);

    // A reload while the first host is still alive replays the committed
    // prefix through the real SSE route without duplicating the first tool.
    await openConversation(page, host, { reload: true });
    await expect.poll(() => counters.streamGets).toBeGreaterThanOrEqual(1);
    const midStreamGets = counters.streamGets;
    let midExecution;
    await expect
      .poll(async () => {
        const midMessages = await snapshotMessages(page);
        midExecution = executionRow(
          midMessages.filter((m) => m.role === "assistant"),
        );
        return midExecution?.toolIds.length;
      })
      .toBe(1);
    expect(midExecution.planExecuted).toBe(false);
    const executionIds = {
      dbId: midExecution.dbId,
      messageId: midExecution.messageId,
      runId: midExecution.runId,
      proposalId: midExecution.proposalId,
    };
    expect(executionIds.messageId).toBeTruthy();

    await host.crash();
    await host.start("none");
    const secondPid = (await host.state()).processId;
    expect(secondPid).not.toBe(firstPid);
    await openConversation(page, host, { reload: true });
    await expect
      .poll(() => counters.streamGets)
      .toBeGreaterThan(midStreamGets);

    // The real one-minute lease expires by wall clock; no database override.
    const doneJob = await waitJobStatus(host, "completed", 120_000);
    expect(doneJob.status).toBe("completed");
    // The live stream opened by the reload above - with no further reload -
    // must carry the recovered turn to a terminal review in the UI itself.
    await waitUiTerminal(page, "review", "completed");
    await expect(page.getByText("HTTP reply")).toBeVisible();

    const messages = await snapshotMessages(page);
    const assistants = messages.filter((m) => m.role === "assistant");
    expect(assistants).toHaveLength(2);
    const execution = executionRow(assistants);
    const review = reviewRow(assistants);
    expect(execution.dbId).toBe(executionIds.dbId);
    expect(execution.messageId).toBe(executionIds.messageId);
    expect(execution.runId).toBe(executionIds.runId);
    expect(execution.proposalId).toBe(executionIds.proposalId);
    expect(execution.planExecuted).toBe(true);
    expect(execution.toolIds).toHaveLength(2);
    expect([...execution.toolIds].sort()).toEqual(
      [...execution.timelineToolIds].sort(),
    );
    expect(review.executionProposalId).toBe(execution.proposalId);
    expect(review.runId).toBe(execution.runId);
    expect(review.messageId).not.toBe(execution.messageId);
    expect(review.content).toContain("HTTP reply");

    const native = host.ledger("native");
    expect(native.map((r) => r.body?.name)).toEqual(["first", "second"]);
    expect(native[0].processId).toBe(firstPid);
    expect(native[1].processId).toBe(secondPid);
    const model = host.ledger("model");
    expect(model).toHaveLength(1);
    expect(model[0].processId).toBe(secondPid);
    expect(counters.executePosts).toBe(1);

    // A final reload replays persisted history only: identical identities,
    // no further dispatch, model call or execute request.
    await openConversation(page, host, { reload: true });
    const reloaded = (await snapshotMessages(page)).filter(
      (m) => m.role === "assistant",
    );
    expect(reloaded.map((m) => m.dbId)).toEqual(
      assistants.map((m) => m.dbId),
    );
    expect(reloaded.map((m) => m.messageId)).toEqual(
      assistants.map((m) => m.messageId),
    );
    expect(host.ledger("native")).toHaveLength(2);
    expect(host.ledger("model")).toHaveLength(1);
    expect(counters.executePosts).toBe(1);
  } finally {
    try {
      await page.close();
    } catch {
      /* already closed */
    }
    await host.dispose();
  }
});

test("crash after a persisted start never dispatches the ambiguous step", async ({
  page,
}) => {
  const host = await LocalHost.create();
  try {
    await host.start("first-running");
    const counters = await bootstrap(page, host);
    await openConversation(page, host);
    await page
      .getByRole("button", { name: "Выполнить", exact: true })
      .click();

    let state;
    await expect
      .poll(async () => (state = await host.state()).paused)
      .toBe(true);
    const job = lastJob(state);
    expect(job.startedStep).toBe(0);
    expect(parseJson(job.checkpointsJson, [])).toHaveLength(0);
    expect(host.ledger("native")).toHaveLength(0);
    expect(host.ledger("model")).toHaveLength(0);

    // The ambiguous step is visible live before the kill: one canonical
    // execution message carrying its in-flight tool.
    let preExecution;
    await expect
      .poll(async () => {
        const pre = await snapshotMessages(page);
        preExecution = executionRow(
          pre.filter((m) => m.role === "assistant"),
        );
        return (
          !!preExecution?.messageId && preExecution?.toolIds.length === 1
        );
      })
      .toBe(true);
    const executionIds = {
      dbId: preExecution.dbId,
      messageId: preExecution.messageId,
      runId: preExecution.runId,
      proposalId: preExecution.proposalId,
    };
    const midStreamGets = counters.streamGets;

    await host.crash();
    await host.start("none");
    await openConversation(page, host, { reload: true });
    await expect
      .poll(() => counters.streamGets)
      .toBeGreaterThan(midStreamGets);
    const deadJob = await waitJobStatus(host, "interrupted", 120_000);
    expect(deadJob.status).toBe("interrupted");
    // The live stream opened by the reload - no further reload - delivers
    // the interrupted terminal state to the UI itself.
    await waitUiTerminal(page, "execution", "interrupted");

    const uiAssistants = (await snapshotMessages(page)).filter(
      (m) => m.role === "assistant",
    );
    expect(uiAssistants).toHaveLength(1);
    const uiExecution = uiAssistants[0];
    expect(uiExecution.dbId).toBe(executionIds.dbId);
    expect(uiExecution.messageId).toBe(executionIds.messageId);
    expect(uiExecution.runId).toBe(executionIds.runId);
    expect(uiExecution.proposalId).toBe(executionIds.proposalId);
    expect(uiExecution.executionOutcome).toMatchObject({
      status: "interrupted",
      outcomeUnknown: true,
      succeeded: 0,
      total: 2,
    });
    expect(reviewRow(uiAssistants)).toBeUndefined();

    const conversation = await host.history();
    const rows = historyRows(conversation);
    const execution = executionRow(rows);
    expect(execution.dbId).toBe(executionIds.dbId);
    expect(execution.executionOutcome).toMatchObject({
      status: "interrupted",
      outcomeUnknown: true,
      succeeded: 0,
      total: 2,
    });
    expect(reviewRow(rows)).toBeUndefined();
    expect(host.ledger("native")).toHaveLength(0);
    expect(host.ledger("model")).toHaveLength(0);
    expect(counters.executePosts).toBe(1);
    expect(
      rows.filter((m) => m.role === "assistant" && m.proposalId),
    ).toHaveLength(1);
    await expect(
      page.getByRole("button", { name: "Выполнить", exact: true }),
    ).toHaveCount(0);
  } finally {
    try {
      await page.close();
    } catch {
      /* already closed */
    }
    await host.dispose();
  }
});

test("crash during review keeps the completed execution and never restarts the review model", async ({
  page,
}) => {
  const host = await LocalHost.create();
  try {
    await host.start("review-text");
    const counters = await bootstrap(page, host);
    await openConversation(page, host);
    await page
      .getByRole("button", { name: "Выполнить", exact: true })
      .click();

    let state;
    await expect
      .poll(async () => (state = await host.state()).paused)
      .toBe(true);
    const job = lastJob(state);
    const binding = parseJson(job.bindingJson, {});
    expect(binding.phase).toBe("review");
    expect(job.executionEnded).toBe(true);
    expect(host.ledger("native").map((r) => r.body?.name)).toEqual([
      "first",
      "second",
    ]);
    expect(host.ledger("model")).toHaveLength(1);
    await expect(page.getByText("HTTP reply")).toBeVisible();

    // The live UI already shows a fully completed execution phase whose
    // review is in flight; record both UI and server identities.
    let uiA;
    await expect
      .poll(async () => {
        const ui = await snapshotMessages(page);
        uiA = executionRow(ui.filter((m) => m.role === "assistant"));
        return uiA?.messageState;
      })
      .toBe("completed");
    expect(uiA.planExecuted).toBe(true);
    expect(uiA.executionOutcome).toMatchObject({
      status: "completed",
      succeeded: 2,
      total: 2,
    });

    const before = historyRows(await host.history());
    const executionA = executionRow(before);
    const reviewB = reviewRow(before);
    expect(executionA.planExecuted).toBe(true);
    expect(executionA.messageState).toBe("completed");
    expect(executionA.executionOutcome).toMatchObject({
      status: "completed",
      succeeded: 2,
      total: 2,
    });
    expect(reviewB.executionProposalId).toBe(executionA.proposalId);
    expect(reviewB.runId).toBe(executionA.runId);
    const identities = {
      execution: {
        dbId: executionA.dbId,
        messageId: executionA.messageId,
        runId: executionA.runId,
        proposalId: executionA.proposalId,
      },
      review: {
        dbId: reviewB.dbId,
        messageId: reviewB.messageId,
      },
    };
    const midStreamGets = counters.streamGets;

    await host.crash();
    await host.start("none");
    await openConversation(page, host, { reload: true });
    await expect
      .poll(() => counters.streamGets)
      .toBeGreaterThan(midStreamGets);
    const deadJob = await waitJobStatus(host, "interrupted", 120_000);
    expect(deadJob.status).toBe("interrupted");
    // The live stream delivers the interrupted review with no further
    // reload; the completed execution segment is never rewritten.
    await waitUiTerminal(page, "review", "interrupted");

    const uiAfter = (await snapshotMessages(page)).filter(
      (m) => m.role === "assistant",
    );
    expect(new Set(uiAfter.map((m) => m.messageId)).size).toBe(2);
    const uiExecution = executionRow(uiAfter);
    const uiReview = reviewRow(uiAfter);
    expect(uiExecution.dbId).toBe(identities.execution.dbId);
    expect(uiExecution.messageId).toBe(identities.execution.messageId);
    expect(uiExecution.messageState).toBe("completed");
    expect(uiExecution.planExecuted).toBe(true);
    expect(uiExecution.executionOutcome).toMatchObject({
      status: "completed",
      succeeded: 2,
      total: 2,
    });
    expect(uiReview.dbId).toBe(identities.review.dbId);
    expect(uiReview.messageId).toBe(identities.review.messageId);
    expect(uiReview.messageState).toBe("interrupted");
    expect(uiReview.done).toBe(true);
    expect(uiReview.executionProposalId).toBe(identities.execution.proposalId);
    expect(
      uiAfter.filter((m) => (m.content || "").includes("HTTP reply")),
    ).toHaveLength(1);

    const after = historyRows(await host.history());
    const executionAfter = executionRow(after);
    const reviewAfter = reviewRow(after);
    expect(executionAfter.dbId).toBe(identities.execution.dbId);
    expect(executionAfter.messageId).toBe(identities.execution.messageId);
    expect(executionAfter.runId).toBe(identities.execution.runId);
    expect(executionAfter.planExecuted).toBe(true);
    expect(executionAfter.messageState).toBe("completed");
    expect(executionAfter.executionOutcome).toMatchObject({
      status: "completed",
      succeeded: 2,
      total: 2,
    });
    expect(reviewAfter.dbId).toBe(identities.review.dbId);
    expect(reviewAfter.messageId).toBe(identities.review.messageId);
    expect(reviewAfter.messageState).toBe("interrupted");
    expect(reviewAfter.text).toContain("HTTP reply");
    expect(
      after.filter(
        (m) =>
          m.role === "assistant" &&
          (m.text || m.content).includes("HTTP reply"),
      ),
    ).toHaveLength(1);

    expect(host.ledger("native")).toHaveLength(2);
    expect(host.ledger("model")).toHaveLength(1);
    expect(counters.executePosts).toBe(1);

    // A final reload replays persisted history only: same identities and
    // terminal states, the prior text once, no extra dispatch or model call.
    await openConversation(page, host, { reload: true });
    await page.waitForFunction(
      () =>
        (document.querySelector("#app")?.__vue_app__?.config?.globalProperties
          ?.$store?.state?.aiChat?.messages || []).length > 0,
    );
    const reloaded = (await snapshotMessages(page)).filter(
      (m) => m.role === "assistant",
    );
    expect(reloaded.map((m) => m.dbId)).toEqual(uiAfter.map((m) => m.dbId));
    expect(reloaded.map((m) => m.messageId)).toEqual(
      uiAfter.map((m) => m.messageId),
    );
    expect(executionRow(reloaded).messageState).toBe("completed");
    expect(reviewRow(reloaded).messageState).toBe("interrupted");
    expect(
      reloaded.filter((m) => (m.content || "").includes("HTTP reply")),
    ).toHaveLength(1);
    expect(host.ledger("native")).toHaveLength(2);
    expect(host.ledger("model")).toHaveLength(1);
    expect(historyRows(await host.history())).toHaveLength(after.length);
  } finally {
    try {
      await page.close();
    } catch {
      /* already closed */
    }
    await host.dispose();
  }
});
