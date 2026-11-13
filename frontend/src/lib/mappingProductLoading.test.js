import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const source = readFileSync(
  new URL("../pages/marketplace/fieldMappingPage.vue", import.meta.url),
  "utf8",
);

function method(start, end) {
  const body = source.slice(
    source.indexOf(start),
    source.indexOf(end, source.indexOf(start)),
  );
  return new Function("extractResponseData", `return ({ ${body} });`)(
    (response, fallback) => response?.data ?? fallback,
  );
}

test("mapping initialization does not fetch a catalog page or wait for product choices", async () => {
  const { fetchData } = method(
    "    async fetchData() {",
    "    async requestGraph()",
  );
  let previewCalls = 0;
  let selectedCalls = 0;
  const graph = { hasGraph: true, graphVersion: "v1" };
  const context = {
    requestGraph: async () => graph,
    fetchAvailableSources: async () => {},
    fetchStoreFiles: async () => {},
    fetchPreviewProducts: async () => {
      previewCalls++;
    },
    ensureSelectedPreviewInOptions: () => {
      selectedCalls++;
      return new Promise(() => {}); // A slow saved choice cannot block the editor.
    },
  };
  await fetchData.call(context);
  assert.equal(context.isLoading, false);
  assert.equal(context.prefetchedGraph, graph);
  assert.equal(previewCalls, 0);
  assert.equal(selectedCalls, 1);
});

test("preview searches use lean, category-scoped choices and ignore stale responses", async () => {
  const { fetchPreviewProducts } = method(
    "    async fetchPreviewProducts(",
    "    onPreviewProductSearch(",
  );
  const requests = [];
  const context = {
    mappingInfo: { localCategoryId: 7 },
    previewSelectedOption: { value: "1", label: "Selected" },
    mapPreviewProductOption: (p) => ({ value: String(p.id), label: p.name }),
    lookupProducts: async (request) => {
      requests.push(request);
    },
  };
  await fetchPreviewProducts.call(context, "old");
  await fetchPreviewProducts.call(context, "new");
  assert.equal(requests[1].categoryId, 7);
  requests[1].onSuccess({ data: { items: [{ id: 2, name: "New" }] } });
  requests[0].onSuccess({ data: { items: [{ id: 3, name: "Old" }] } });
  assert.deepEqual(
    context.previewProducts.map((p) => p.value),
    ["1", "2"],
  );
  assert.equal(context.previewProductsLoading, false);
});
