import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const nav = readFileSync(
  new URL("../components/navigationBar.vue", import.meta.url),
  "utf8",
);
const search = readFileSync(
  new URL("../components/NavSearchField.vue", import.meta.url),
  "utf8",
);
const toolbar = readFileSync(
  new URL("../components/PageListToolbar.vue", import.meta.url),
  "utf8",
);
const settings = readFileSync(
  new URL("../layouts/SettingsShell.vue", import.meta.url),
  "utf8",
);
const products = readFileSync(
  new URL("../pages/productsPage.vue", import.meta.url),
  "utf8",
);
const barcodes = readFileSync(
  new URL("../pages/barcodesPage.vue", import.meta.url),
  "utf8",
);
const notes = readFileSync(
  new URL("../pages/notesPage.vue", import.meta.url),
  "utf8",
);
const storage = readFileSync(
  new URL("../pages/storeStoragePage.vue", import.meta.url),
  "utf8",
);
const fileManager = readFileSync(
  new URL("../components/products/ProductFileManager.vue", import.meta.url),
  "utf8",
);
const breadcrumbs = readFileSync(
  new URL("../components/breadcrumbs.vue", import.meta.url),
  "utf8",
);
const chrome = readFileSync(
  new URL("../components/AppPageChrome.vue", import.meta.url),
  "utf8",
);
const style = readFileSync(new URL("../style.css", import.meta.url), "utf8");
const page = readFileSync(
  new URL("../components/page.vue", import.meta.url),
  "utf8",
);
const app = readFileSync(new URL("../app.vue", import.meta.url), "utf8");
const orders = readFileSync(
  new URL("../pages/orders/OrdersPage.vue", import.meta.url),
  "utf8",
);
const assembly = readFileSync(
  new URL("../pages/orders/OrdersAssemblyPage.vue", import.meta.url),
  "utf8",
);
const connection = readFileSync(
  new URL("../pages/marketplace/connectionPage.vue", import.meta.url),
  "utf8",
);
const errorLogs = readFileSync(
  new URL("../pages/admin/errorLogsPage.vue", import.meta.url),
  "utf8",
);
const adapterLogs = readFileSync(
  new URL("../pages/marketplace/adapterLogsPage.vue", import.meta.url),
  "utf8",
);
const apiLogs = readFileSync(
  new URL("../components/logs/ApiRequestLogsPanel.vue", import.meta.url),
  "utf8",
);
const categoryBrowser = readFileSync(
  new URL(
    "../components/marketplace/ExternalCategoryBrowser.vue",
    import.meta.url,
  ),
  "utf8",
);
const input = readFileSync(
  new URL("../components/ui/input/Input.vue", import.meta.url),
  "utf8",
);
const selectTrigger = readFileSync(
  new URL("../components/ui/select/SelectTrigger.vue", import.meta.url),
  "utf8",
);
const searchableSelect = readFileSync(
  new URL(
    "../components/ui/searchable-select/SearchableSelect.vue",
    import.meta.url,
  ),
  "utf8",
);
const multiSelect = readFileSync(
  new URL(
    "../components/ui/multi-select/MultiSelectDropdown.vue",
    import.meta.url,
  ),
  "utf8",
);
const storeSwitcher = readFileSync(
  new URL("../components/sidebar/AppSidebarStoreSwitcher.vue", import.meta.url),
  "utf8",
);
const languageSwitcher = readFileSync(
  new URL("../components/LanguageSwitcher.vue", import.meta.url),
  "utf8",
);
const fabGroup = readFileSync(
  new URL("../components/FabGroup.vue", import.meta.url),
  "utf8",
);

describe("navbar center", () => {
  it("keeps the center on the pane midline with equal side tracks", () => {
    assert.match(
      nav.replace(/\s+/g, ""),
      /grid-template-columns:minmax\(0,1fr\)minmax\(0,min\(40rem,50%\)\)minmax\(0,1fr\)/,
    );
    assert.match(nav, /container-type: inline-size/);
    assert.match(nav, /@container \(min-width: 64rem\)/);
  });

  it("stacks search in narrow panels and gives tabs a separate scrollable row", () => {
    assert.match(nav, /grid-column: 1 \/ -1/);
    assert.match(nav, /grid-row: 2/);
    assert.match(nav, /data-slot="navbar-tabs"/);
    assert.match(nav, /overflow-x-auto/);
    assert.match(chrome, /name="nav-tabs"/);
  });
});

describe("toolbar search", () => {
  it("matches the default input field height and corner radius", () => {
    assert.match(input, /\bh-9\b/);
    assert.match(input, /rounded-control/);
    assert.match(search, /data-slot="nav-search"/);
    assert.match(search, /\bh-9\b/);
    assert.match(search, /rounded-control/);
    assert.doesNotMatch(search, /\bh-10\b/);
    assert.doesNotMatch(search, /rounded-full/);
    assert.match(search, /bg-transparent/);
    assert.match(search, /dark:bg-input\/30/);
    assert.match(search, /border-input/);
    assert.doesNotMatch(search, /bg-muted/);
    assert.doesNotMatch(search, /bg-background/);
    assert.match(toolbar, /NavSearchField/);
    assert.match(products, /PageListToolbar/);
    assert.match(products, /#filters/);
    assert.match(barcodes, /PageListToolbar/);
    assert.match(notes, /PageListToolbar/);
    assert.doesNotMatch(
      products,
      /PopoverTrigger as-child>\s*<Button\s+variant="outline"/,
    );
  });

  it("uses one fluid search width without page-specific desktop overrides", () => {
    assert.match(search, /w-full min-w-0/);
    assert.doesNotMatch(nav, /nav-search\]\]:max-w-64/);
    assert.match(toolbar, /flex w-full min-w-0 max-w-md items-center gap-2/);
    assert.doesNotMatch(search, /lg:w-64/);
    assert.doesNotMatch(products, /sm:w-72/);
    assert.doesNotMatch(products, /lg:!max-w-none/);
    assert.doesNotMatch(products, /lg:!w-\[min\(32rem,42vw\)\]/);
  });

  it("does not paint a 3px ring on the field", () => {
    assert.doesNotMatch(search, /ring-3/);
    assert.doesNotMatch(search, /has-\[:focus-visible\]:ring-/);
  });
});

describe("navbar help", () => {
  it("shows the help label only when the panel itself has enough room", () => {
    assert.match(nav, /CircleHelp/);
    assert.match(nav, /docsSheet\.needHelp/);
    assert.match(nav, /@container \(min-width: 88rem\)/);
    assert.match(nav, /navbar-help-label/);
    assert.doesNotMatch(nav, /hidden sm:inline/);
    assert.doesNotMatch(nav, /BookOpen/);
    assert.doesNotMatch(nav, /hidden size-9 shrink-0 md:inline-flex/);
  });

  it("does not add a second docs icon in the compact navbar FAB cluster", () => {
    const compact = fabGroup.slice(fabGroup.indexOf("lg:hidden"));
    assert.match(compact, /backgroundJobsTitle/);
    assert.doesNotMatch(compact, /documentationTitle/);
    assert.match(fabGroup, /documentationTitle/);
  });
});

describe("navbar compact chrome", () => {
  it("shows only the current page title in the trail below lg", () => {
    assert.match(breadcrumbs, /max-lg:hidden/);
  });

  it("keeps the home crumb inside the trail so hover fill is not clipped", () => {
    assert.match(breadcrumbs, /class="size-7"/);
    assert.doesNotMatch(breadcrumbs, /-ml-1\.5/);
  });

  it("grows the sticky offset when tools stack under the title", () => {
    assert.match(chrome, /has-nav-tools/);
    assert.match(style, /--app-navbar-tools/);
    assert.match(style, /\.has-nav-tools/);
    assert.match(style, /--app-navbar-row:\s*4rem/);
    assert.match(nav, /useResizeObserver\(navbarElement/);
    assert.match(chrome, /'--app-navbar-height': `\$\{navbarHeight\}px`/);
  });

  it("docks the bar above the page scroller so it never pans with the table", () => {
    assert.match(page, /min-w-0 w-full/);
    assert.doesNotMatch(page, /min-w-full w-max/);
    assert.match(nav, /data-slot="app-navbar"/);
    assert.doesNotMatch(nav, /sticky top-0/);
    assert.match(chrome, /Teleport defer to="#app-navbar-host"/);
    assert.match(chrome, /bg-background adaptive-padding/);
    assert.doesNotMatch(style, /100cqi/);
    assert.doesNotMatch(style, /--page-scroll-client-width/);
    assert.match(app, /id="app-navbar-host"/);
    assert.match(app, /data-slot="app-navbar-host"/);
    assert.match(app, /data-slot="page-scroll"/);
    assert.match(app, /overflow-y-auto/);
    assert.doesNotMatch(app, /bindPageScrollClientWidth/);
  });

  it("keeps store storage inside the pane so the file list scrolls and the footer stays put", () => {
    assert.match(storage, /fill-viewport/);
    assert.match(storage, /min-h-0 flex-1 flex-col overflow-hidden/);
    assert.doesNotMatch(storage, /100vh/);
    assert.match(fileManager, /overflow-auto/);
    assert.match(fileManager, /data-slot="table-actions-footer"/);
    assert.match(fileManager, /shrink-0 items-center gap-2 bg-background/);
  });

  it("uses one bottom inset for fill panes and product-edit side columns", () => {
    assert.match(style, /--app-fill-pane-height/);
    assert.match(style, /--page-scroll-end:\s*1\.25rem/);
    assert.match(chrome, /pb-\[var\(--page-scroll-end\)\]/);
    assert.doesNotMatch(chrome, /!fillViewport && 'pb-/);
    const edit = readFileSync(
      new URL("../pages/productEditPage.vue", import.meta.url),
      "utf8",
    );
    const gallery = readFileSync(
      new URL(
        "../components/products/ProductGallerySidebar.vue",
        import.meta.url,
      ),
      "utf8",
    );
    assert.match(edit, /h-\[var\(--app-fill-pane-height\)\]/);
    assert.doesNotMatch(edit, /100vh - 8\.5rem/);
    assert.match(gallery, /h-\[var\(--app-fill-pane-height\)\]/);
  });
});

describe("shared list search", () => {
  it("uses PageListToolbar across catalog, orders, notes, barcodes and logs", () => {
    for (const source of [
      products,
      orders,
      assembly,
      connection,
      notes,
      barcodes,
      errorLogs,
      adapterLogs,
      apiLogs,
    ]) {
      assert.match(source, /PageListToolbar/);
      assert.doesNotMatch(source, /sm:max-w-(?:sm|xs)/);
    }
    assert.match(categoryBrowser, /NavSearchField/);
    assert.doesNotMatch(orders, /relative h-9 min-w-0 flex-1 sm:max-w-sm/);
  });

  it("uses standard dropdown menu primitives without custom popup chrome", () => {
    assert.match(toolbar, /SlidersHorizontal/);
    assert.match(toolbar, /DropdownMenuTrigger as-child/);
    assert.match(toolbar, /common\.activeFilters/);
    assert.doesNotMatch(toolbar, /active-filter-count/);
    assert.match(toolbar, /emit\('reset'\)/);
    assert.match(toolbar, /DropdownMenuItem/);
    assert.doesNotMatch(toolbar, /FieldGroup|PopoverContent|mx-auto/);
  });

  it("lets settings children render tools without duplicating Page chrome", () => {
    assert.match(settings, /id="settings-navbar-tools"/);
    assert.match(settings, /hasListTools/);
    for (const source of [errorLogs, adapterLogs]) {
      assert.match(source, /SettingsNavbarTools/);
      assert.doesNotMatch(source, /<LogToolbar/);
    }
    assert.match(apiLogs, /v-if="toolbarVisible"/);
    assert.doesNotMatch(apiLogs, /SettingsNavbarTools/);
    assert.match(settings, /:has-nav-center="isApiPage \|\| hasListTools"/);
  });
});

describe("select field chrome", () => {
  it("uses the same control corners as Input, not a pill or xl stadium", () => {
    assert.match(selectTrigger, /rounded-control/);
    assert.match(searchableSelect, /rounded-control/);
    assert.match(multiSelect, /rounded-control/);
    assert.match(storeSwitcher, /SelectTrigger class="h-9 w-full/);
    assert.doesNotMatch(storeSwitcher, /SelectTrigger class="[^"]*rounded-xl/);
    assert.match(languageSwitcher, /\bh-9\b/);
    assert.doesNotMatch(languageSwitcher, /class="h-8"/);
  });
});
