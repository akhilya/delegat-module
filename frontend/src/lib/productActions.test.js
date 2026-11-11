import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const read = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

function optionsFromSource(source, imports) {
  const script = source.match(/<script>([\s\S]*?)<\/script>/)?.[1] ?? source;
  const executable = script
    .replace(/^import\s+([\s\S]*?)\s+from\s+["'][^"']+["'];/gm, (_, clause) =>
      clause.trim().startsWith("{")
        ? `const ${clause.replace(/\bas\b/g, ":")} = imports;`
        : `const ${clause.trim()} = imports.${clause.trim()};`,
    )
    .replace("export default", "return");
  return new Function("imports", executable)(imports);
}

const adapterSource = read(
  "../components/products/productMarketplaceIntegrations.vue",
);
const editorSource = read("../pages/productEditPage.vue");
const exportEntries = [
  "handleExportToAll",
  "handleExportPricesToAll",
  "handleExportStocksToAll",
  "handleExportToConnection",
  "handleExportPricesToConnection",
  "handleExportStocksToConnection",
  "submitRecreate",
];

function integration(beforeExport) {
  const exports = [];
  const operations = [];
  const errors = [];
  const options = optionsFromSource(adapterSource, {
    mapActions: () => ({}),
    t: (key) => key,
    randomUuidCompact: () => "test-operation",
    extractResponseData: (response, fallback) => response?.data ?? fallback,
    displaySonnerError: (error) => errors.push(error),
    displaySonnerSuccess: () => {},
  });
  const conn = {
    connectionId: 1,
    linkId: 2,
    isLinked: true,
    hasMapping: true,
    canExport: true,
    canExportPrices: true,
    canExportStocks: true,
    adapterName: "Test",
    availableMappings: [{ id: 3 }],
  };
  const context = {
    ...options.data(),
    ...options.methods,
    productId: 10,
    beforeExport,
    connections: { allConnections: [conn] },
    $store: { dispatch: async (...args) => operations.push(args) },
    $emit: () => {},
  };
  for (const [name, getter] of Object.entries(options.computed))
    Object.defineProperty(context, name, { get: () => getter.call(context) });
  context.getAvailableConnectionsForProduct = async ({ onSuccess }) =>
    onSuccess({ data: context.connections });
  for (const name of [
    "exportFromLink",
    "exportProduct",
    "exportPrices",
    "exportStocks",
    "recreateProductOnMarketplace",
  ])
    context[name] = async (args) => {
      exports.push({ name, args });
      args.onSuccess?.({ data: {} });
    };
  context.recreateConn = conn;
  context.recreateNewSku = "replacement";
  return { context, conn, exports, operations, errors };
}

function editor({ valid = true, update = true, images = true } = {}) {
  const events = [];
  const saved = [];
  const refs = {
    isProductLoading: { value: false },
    isSubmitting: { value: false },
    hasUnsavedChanges: { value: true },
    canManageCatalog: { value: true },
    productId: { value: 10 },
    selectedCategoryId: { value: 1 },
    allCategoryAttributeValues: { value: {} },
    category: { value: { attributes: [] } },
    storeAttributes: { value: [] },
    sortedImages: { value: [{ fileId: 20 }] },
    nextTick: async () => {},
    form: {
      values: {
        product: { name: "Draft name", price: 250 },
        storeAttributes: {},
      },
      validate: async () => {
        events.push("validate");
        return { valid };
      },
      setErrors: () => {},
    },
    syncCategoryAttributesCacheFromForm: () => {},
    categoryAttributeCacheToValues: () => [],
    formRecordToAttributeValues: () => [],
    applyAttributeUniquenessError: () => {},
    displaySonnerError: () => {},
    displaySonnerSuccess: () => {},
    t: (key) => key,
    store: {
      dispatch: async (action, args) => {
        assert.equal(action, "updateProduct");
        events.push("update");
        if (update) {
          saved.push(args.input);
          args.onSuccess();
        } else args.onError({ response: { data: {} } });
      },
    },
    persistImages: async () => {
      events.push("images");
      return images;
    },
    syncProductAfterSave: async () => events.push("sync"),
  };
  // Execute the real save pipeline with an in-memory form and mocked cabinet HTTP actions.
  const pipeline = editorSource.slice(
    editorSource.indexOf("    let saveInFlight = null;"),
    editorSource.indexOf("    const onSubmit ="),
  );
  const functions = new Function(
    "refs",
    `const {${Object.keys(refs).join(",")}} = refs; ${pipeline}; return {saveProduct, ensureSavedBeforeExport};`,
  )(refs);
  return { ...functions, refs, events, saved };
}

describe("save before marketplace export", () => {
  it("saves the current form and images before any export operation", async () => {
    const form = editor();
    const { context, conn, exports } = integration(
      form.ensureSavedBeforeExport,
    );
    assert.match(editorSource, /:before-export="ensureSavedBeforeExport"/);
    await context.handleExportToConnection(conn);
    assert.deepEqual(form.events, ["validate", "update", "images", "sync"]);
    assert.equal(form.saved[0].name, "Draft name");
    assert.equal(form.saved[0].price, 250);
    assert.equal(form.refs.hasUnsavedChanges.value, false);
    assert.equal(exports.length, 1);
  });

  for (const entry of exportEntries) {
    it(`${entry} waits for saving and does not export on save failure`, async () => {
      let finish;
      const { context, conn, exports, operations } = integration(
        () =>
          new Promise((resolve) => {
            finish = resolve;
          }),
      );
      const pending = context[entry](conn);
      assert.equal(exports.length, 0);
      assert.equal(operations.length, 0);
      finish(false);
      await pending;
      assert.equal(exports.length, 0);
      assert.equal(operations.length, 0);
    });

    it(`${entry} exports only after a successful save and refresh`, async () => {
      let saved = false;
      const { context, conn, exports } = integration(async () => {
        saved = true;
        return true;
      });
      context.getAvailableConnectionsForProduct = async ({ onSuccess }) => {
        assert.equal(saved, true);
        onSuccess({ data: { allConnections: [{ ...conn, linkId: 30 }] } });
      };
      await context[entry](conn);
      assert.equal(exports.length, 1);
      assert.equal(exports[0].args.linkId ?? exports[0].args.linkIds?.[0], 30);
    });
  }

  for (const failure of [
    { valid: false },
    { update: false },
    { images: false },
  ]) {
    it(`does not export after a failed save stage: ${JSON.stringify(failure)}`, async () => {
      const form = editor(failure);
      const { context, conn, exports } = integration(
        form.ensureSavedBeforeExport,
      );
      await context.handleExportToConnection(conn);
      assert.equal(exports.length, 0);
      assert.equal(form.refs.hasUnsavedChanges.value, true);
    });
  }

  it("waits for an already-running manual save instead of exporting old data", async () => {
    const form = editor();
    let finish;
    form.refs.store.dispatch = (action, args) =>
      new Promise((resolve) => {
        form.events.push("update");
        finish = () => {
          form.saved.push(args.input);
          args.onSuccess();
          resolve();
        };
      });
    const manual = form.saveProduct();
    await Promise.resolve();
    const { context, conn, exports } = integration(
      form.ensureSavedBeforeExport,
    );
    const pending = context.handleExportToConnection(conn);
    assert.equal(exports.length, 0);
    finish();
    await Promise.all([manual, pending]);
    assert.equal(form.saved.length, 1);
    assert.equal(exports.length, 1);
  });

  it("revalidates the mapping after saving a category change", async () => {
    const { context, conn, exports } = integration(async () => true);
    context.getAvailableConnectionsForProduct = async ({ onSuccess }) =>
      onSuccess({
        data: {
          allConnections: [
            { ...conn, hasMapping: false, availableMappings: [] },
          ],
        },
      });
    await context.handleExportToConnection(conn);
    assert.equal(exports.length, 0);
  });

  it("does not discard or export a draft edited again while saving", async () => {
    const form = editor();
    form.refs.persistImages = async () => {
      form.refs.form.values.product.price = 350;
      return true;
    };
    // Re-create the closure with a save-stage hook before it captures the dependencies.
    const pipeline = editorSource.slice(
      editorSource.indexOf("    let saveInFlight = null;"),
      editorSource.indexOf("    const onSubmit ="),
    );
    const functions = new Function(
      "refs",
      `const {${Object.keys(form.refs).join(",")}} = refs; ${pipeline}; return {ensureSavedBeforeExport};`,
    )(form.refs);
    const { context, conn, exports } = integration(
      functions.ensureSavedBeforeExport,
    );
    await context.handleExportToConnection(conn);
    assert.equal(exports.length, 0);
    assert.equal(form.refs.form.values.product.price, 350);
    assert.equal(form.refs.hasUnsavedChanges.value, true);
    assert.ok(!form.events.includes("sync"));
  });

  it("does not export a different product when navigation changes during save", async () => {
    let finish;
    const { context, conn, exports } = integration(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const pending = context.handleExportToConnection(conn);
    context.productId = 11;
    finish(true);
    await pending;
    assert.equal(exports.length, 0);
  });

  it("does not force a product save when the standalone component has no editor", async () => {
    const { context, conn, exports } = integration(null);
    await context.handleExportToConnection(conn);
    assert.equal(exports.length, 1);
  });
});

describe("product group merge cabinet wiring", () => {
  it("posts one atomic merge request with explicit source and target IDs", async () => {
    const calls = [];
    const source = read("../store/modules/productGroupModule.js");
    const module = optionsFromSource(source, {
      http: {
        post: async (...args) => {
          calls.push(args);
          return { data: { groupId: 1 } };
        },
      },
    });
    let result;
    await module.actions.mergeProductGroups(
      {},
      {
        targetGroupId: 1,
        sourceGroupIds: [2, 3],
        onSuccess: (response) => {
          result = response.data;
        },
      },
    );
    assert.deepEqual(calls, [
      ["/productgroups/1/merge", { sourceGroupIds: [2, 3] }],
    ]);
    assert.equal(result.groupId, 1);
  });

  it("requires a target choice and explains that source groups are removed", () => {
    const dialog = read("../components/products/MergeProductGroupsDialog.vue");
    const page = read("../pages/productGroupsPage.vue");
    assert.match(dialog, /targetGroupId = ref\(""\)/);
    assert.match(dialog, /mergeDescription/);
    assert.match(dialog, /mergeNotice/);
    assert.match(dialog, /busy \|\| !target/);
    assert.match(
      page,
      /v-if="canManageCatalog"[\s\S]*v-model:open="showMergeDialog"/,
    );
    assert.match(page, /selectedCount < 2/);
  });
});

describe("product group bulk delete cabinet wiring", () => {
  it("posts selected group IDs in one request", async () => {
    const calls = [];
    const source = read("../store/modules/productGroupModule.js");
    const module = optionsFromSource(source, {
      http: {
        post: async (...args) => {
          calls.push(args);
          return { data: { deletedCount: 2 } };
        },
      },
    });
    let result;
    await module.actions.deleteGroups(
      {},
      {
        groupIds: [4, 5],
        onSuccess: (response) => {
          result = response.data;
        },
      },
    );

    assert.deepEqual(calls, [
      ["/productgroups/bulk-delete", { groupIds: [4, 5] }, { cache: false }],
    ]);
    assert.equal(result.deletedCount, 2);

    const page = read("../pages/productGroupsPage.vue");
    const handler = page.match(
      /async handleBulkDelete\(\) \{([\s\S]*?)\n {4}\},/,
    )?.[1];
    assert.ok(handler);
    assert.match(handler, /await this\.deleteGroups\(/);
    assert.doesNotMatch(handler, /for\s*\(/);
  });
});
