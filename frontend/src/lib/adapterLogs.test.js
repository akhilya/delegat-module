import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

// Exercise the actual Options API methods without mounting the cabinet or issuing HTTP.
function loadPage() {
  const errors = [];
  const source = readFileSync(
    new URL("../pages/marketplace/adapterLogsPage.vue", import.meta.url),
    "utf8",
  ).match(/<script>([\s\S]*?)<\/script>/)[1];
  const script = source
    .replace(/import\s+([\s\S]*?)\s+from\s+["'][^"']+["'];/g, (_, clause) =>
      clause.trim().startsWith("{")
        ? `const ${clause.replace(/\bas\b/g, ":")} = imports;`
        : `const ${clause.trim()} = imports.${clause.trim()};`,
    )
    .replace("export default", "return");
  const options = new Function("imports", script)({
    mapActions: () => ({}),
    LOG_FILTER_ALL: "all",
    LOG_SORT_DEFAULT: { sortBy: "createdAt", sortDir: "desc" },
    extractResponseData: (response, fallback) => response?.data ?? fallback,
    displaySonnerError: (error) => errors.push(error),
  });
  const page = { ...options.data(), ...options.methods };
  for (const [name, getter] of Object.entries(options.computed)) {
    Object.defineProperty(page, name, { get: () => getter.call(page) });
  }
  const lists = [];
  const details = [];
  page.getAdapterHttpLogs = (args) =>
    new Promise((resolve) => lists.push({ ...args, resolve }));
  page.getAdapterHttpLogDetail = (args) =>
    new Promise((resolve) => details.push({ ...args, resolve }));
  return { page, options, lists, details, errors };
}

function succeed(request, data) {
  request.onSuccess({ data });
  request.resolve();
}

describe("adapter log pagination and request ownership", () => {
  it("loads every page without counting the cumulative rows twice", async () => {
    const { page, lists } = loadPage();
    let pending = page.refresh();
    succeed(lists[0], {
      items: Array.from({ length: 50 }, (_, id) => ({ id })),
      total: 175,
    });
    await pending;
    for (const [offset, count] of [
      [50, 50],
      [100, 50],
      [150, 25],
    ]) {
      assert.equal(page.hasMore, true);
      pending = page.loadMore();
      const request = lists.at(-1);
      assert.equal(request.params.offset, offset);
      succeed(request, {
        items: Array.from({ length: count }, (_, id) => ({ id: offset + id })),
        total: 175,
      });
      await pending;
    }
    assert.equal(page.items.length, 175);
    assert.equal(page.hasMore, false);
    await page.loadMore();
    assert.equal(lists.length, 4);
  });

  it("aborts the previous search and rejects a late stale result", async () => {
    const { page, lists } = loadPage();
    const old = page.refresh();
    page.filters.q = "new-search";
    const latest = page.refresh();
    assert.equal(lists[0].signal.aborted, true);
    assert.equal(lists[1].params.q, "new-search");
    succeed(lists[1], { items: [{ id: 2 }], total: 1, errorCount: 1 });
    await latest;
    succeed(lists[0], { items: [{ id: 1 }], total: 999 });
    await old;
    assert.deepEqual(page.items, [{ id: 2 }]);
    assert.equal(page.total, 1);
    assert.equal(page.errorCount, 1);
  });

  it("an old completion does not hide the current loading state or show a stale error", async () => {
    const { page, lists, errors } = loadPage();
    const old = page.refresh();
    const latest = page.refresh();
    lists[0].onError(new Error("cancelled old request"));
    lists[0].resolve();
    await old;
    assert.equal(page.isLoading, true);
    assert.deepEqual(errors, []);
    succeed(lists[1], { items: [], total: 0 });
    await latest;
    assert.equal(page.isLoading, false);
  });

  it("ignores old append results after changing a filter", async () => {
    const { page, lists } = loadPage();
    page.items = [{ id: 1 }];
    page.total = 3;
    const append = page.loadMore();
    await page.loadMore();
    assert.equal(
      lists.length,
      1,
      "duplicate load-more clicks must not issue another request",
    );
    const refresh = page.refresh();
    succeed(lists[1], { items: [{ id: 9 }], total: 1 });
    await refresh;
    succeed(lists[0], { items: [{ id: 2 }], total: 3 });
    await append;
    assert.deepEqual(page.items, [{ id: 9 }]);
    assert.equal(page.filters.offset, 0);
  });

  it("only the most recently opened record owns the detail sheet", async () => {
    const { page, details, errors } = loadPage();
    const old = page.openDetail({ id: 1 });
    const latest = page.openDetail({ id: 2 });
    assert.equal(details[0].signal.aborted, true);
    succeed(details[1], { id: 2 });
    await latest;
    details[0].onError(new Error("old failure"));
    succeed(details[0], { id: 1 });
    await old;
    assert.deepEqual(page.detail, { id: 2 });
    assert.equal(page.detailOpen, true);
    assert.deepEqual(errors, []);
  });

  it("cancels pending work on unmount and cannot restart requests afterwards", async () => {
    const { page, options, lists, details } = loadPage();
    const list = page.refresh();
    const detail = page.openDetail({ id: 1 });
    options.beforeUnmount.call(page);
    assert.equal(lists[0].signal.aborted, true);
    assert.equal(details[0].signal.aborted, true);
    succeed(lists[0], { items: [{ id: 1 }], total: 1 });
    succeed(details[0], { id: 1 });
    await Promise.all([list, detail]);
    await page.refresh();
    assert.equal(lists.length, 1);
    assert.deepEqual(page.items, []);
    assert.equal(page.detail, null);
  });
});

describe("adapter log HTTP actions", () => {
  it("passes the caller's cancellation signal to Axios for lists and details", async () => {
    const calls = [];
    const http = {
      get: async (url, config) => {
        calls.push({ url, config });
        return { data: { ok: true } };
      },
    };
    const source = readFileSync(
      new URL("../store/modules/adapterHttpLogModule.js", import.meta.url),
      "utf8",
    );
    const module = new Function(
      "http",
      source
        .replace(/^import[^\n]+\n/gm, "")
        .replace("export default", "return"),
    )(http);
    const controller = new AbortController();
    const results = [];
    const callbacks = {
      signal: controller.signal,
      onSuccess: (response) => results.push(response.data),
    };
    await module.actions.getAdapterHttpLogs(
      {},
      { params: { q: "a b", offset: 50 }, ...callbacks },
    );
    await module.actions.getAdapterHttpLogDetail({}, { id: 12, ...callbacks });
    assert.equal(calls[0].url, "/adapter-http-logs?q=a+b&offset=50");
    assert.equal(calls[1].url, "/adapter-http-logs/12");
    assert.ok(
      calls.every(
        ({ config }) =>
          config.signal === controller.signal && config.cache === false,
      ),
    );
    assert.equal(results.length, 2);
  });
});

describe("adapter log deployment configuration", () => {
  it("forwards both retention settings into the production API container", () => {
    const compose = readFileSync(
      new URL("../../../docker-compose.prod.yml", import.meta.url),
      "utf8",
    );
    const api = compose.slice(
      compose.indexOf("  delegat-api:"),
      compose.indexOf("\n  sandbox:"),
    );
    assert.ok(
      api.includes(
        "ADAPTER_HTTP_LOG_RETENTION_DAYS=${ADAPTER_HTTP_LOG_RETENTION_DAYS:-7}",
      ),
    );
    assert.ok(
      api.includes(
        "ADAPTER_HTTP_LOG_DETAIL_RETENTION_HOURS=${ADAPTER_HTTP_LOG_DETAIL_RETENTION_HOURS:-24}",
      ),
    );
  });
});
