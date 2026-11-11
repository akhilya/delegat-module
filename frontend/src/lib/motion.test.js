import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const style = readFileSync(new URL("../style.css", import.meta.url), "utf8");
const nginx = readFileSync(
  new URL("../../nginx.conf", import.meta.url),
  "utf8",
);
const brand = readFileSync(new URL("../brand.js", import.meta.url), "utf8");
const button = readFileSync(
  new URL("../components/ui/button/index.js", import.meta.url),
  "utf8",
);
const connectionPage = readFileSync(
  new URL("../pages/marketplace/connectionPage.vue", import.meta.url),
  "utf8",
);
const mappingCanvas = readFileSync(
  new URL("../components/marketplace/graph/MappingCanvas.vue", import.meta.url),
  "utf8",
);
const productMarketplaceIntegrations = readFileSync(
  new URL(
    "../components/products/productMarketplaceIntegrations.vue",
    import.meta.url,
  ),
  "utf8",
);
const sheet = readFileSync(
  new URL("../components/ui/sheet/SheetContent.vue", import.meta.url),
  "utf8",
);
const aiChat = readFileSync(
  new URL("../components/ai/AiChatPanel.vue", import.meta.url),
  "utf8",
);
const aiMessageCss = readFileSync(
  new URL("../components/ai/aiMessage.css", import.meta.url),
  "utf8",
);
const aiToolStep = readFileSync(
  new URL("../components/ai/AiToolStep.vue", import.meta.url),
  "utf8",
);
const aiPython = readFileSync(
  new URL("../components/ai/AiPythonRunBlock.vue", import.meta.url),
  "utf8",
);
const aiProcess = readFileSync(
  new URL("../components/ai/AiChatProcess.vue", import.meta.url),
  "utf8",
);
const aiTrigger = readFileSync(
  new URL("../components/ai/AiTriggerButton.vue", import.meta.url),
  "utf8",
);

const sheetHandle = readFileSync(
  new URL("../components/ui/sheet/SheetResizeHandle.vue", import.meta.url),
  "utf8",
);
const sidebar = readFileSync(
  new URL("../components/ui/sidebar/Sidebar.vue", import.meta.url),
  "utf8",
);
const sidebarContent = readFileSync(
  new URL("../components/ui/sidebar/SidebarContent.vue", import.meta.url),
  "utf8",
);
const sidebarMenuSub = readFileSync(
  new URL("../components/ui/sidebar/SidebarMenuSub.vue", import.meta.url),
  "utf8",
);
const collapsibleContent = readFileSync(
  new URL(
    "../components/ui/collapsible/CollapsibleContent.vue",
    import.meta.url,
  ),
  "utf8",
);
const emptyState = readFileSync(
  new URL("../components/ui/empty-state/EmptyState.vue", import.meta.url),
  "utf8",
);
const tabsList = readFileSync(
  new URL("../components/ui/tabs/TabsList.vue", import.meta.url),
  "utf8",
);
const tabsTrigger = readFileSync(
  new URL("../components/ui/tabs/TabsTrigger.vue", import.meta.url),
  "utf8",
);
const tabsContent = readFileSync(
  new URL("../components/ui/tabs/TabsContent.vue", import.meta.url),
  "utf8",
);

describe("edge compression", () => {
  it("gzips JSON API responses through nginx, not only static scripts", () => {
    assert.match(nginx, /gzip_types[\s\S]*application\/json/);
    assert.match(nginx, /gzip_proxied any/);
  });
});

describe("product motion tokens", () => {
  it("defines one ease and the press/ui/overlay/drawer durations", () => {
    assert.match(
      style,
      /--ease-out-quart:\s*cubic-bezier\(0\.25, 1, 0\.5, 1\)/,
    );
    assert.match(style, /--duration-press:\s*120ms/);
    assert.match(style, /--duration-ui:\s*150ms/);
    assert.match(style, /--duration-overlay:\s*200ms/);
    assert.match(style, /--duration-drawer:\s*280ms/);
  });

  it("fades region loaders and route enters without a card ring or stagger", () => {
    const app = readFileSync(new URL("../app.vue", import.meta.url), "utf8");
    const settingsShell = readFileSync(
      new URL("../layouts/SettingsShell.vue", import.meta.url),
      "utf8",
    );
    assert.match(style, /@keyframes page-region-in/);
    assert.match(style, /\.page-region-in/);
    assert.match(style, /animation: page-region-in var\(--duration-overlay\)/);
    assert.doesNotMatch(style, /animation-delay:\s*[1-9]/);
    assert.match(
      emptyState,
      /props\.framed && !props\.loading && "rounded-lg border"/,
    );
    assert.match(emptyState, /props\.loading && "page-region-in"/);
    assert.match(emptyState, /data-slot="empty-state-loading"/);
    assert.match(app, /enter-active-class="page-region-in"/);
    assert.match(settingsShell, /page-region-in/);
  });

  it("presses buttons without bounce and keeps sheets under 300ms", () => {
    assert.match(button, /active:scale-\[0\.98\]/);
    assert.doesNotMatch(button, /bounce|elastic/);
    assert.match(sheet, /--duration-drawer/);
    assert.match(sheet, /fade-in-0/);
    assert.match(sheet, /fade-out-0/);
    assert.doesNotMatch(sheet, /duration-500/);
    assert.match(sheet, /isInset/);
    assert.match(sheet, /rounded-xl/);
    assert.match(sheet, /data-inset/);
    assert.match(sheet, /closeOnOutside/);
    assert.match(sheet, /SheetOverlay v-if="overlay"/);
    assert.match(
      sheet,
      /relative flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden rounded-xl/,
    );
    assert.match(sheet, /SheetResizeHandle/);
    assert.match(sheet, /max-lg:hidden/);
    assert.match(sheet, /data-resizable/);
    assert.match(style, /\[data-slot="sheet-content"\]\[data-inset\]/);
    assert.match(style, /0 8px 20px oklch\(0 0 0 \/ 0\.1\)/);
    assert.match(style, /0 3px 8px oklch\(0 0 0 \/ 0\.06\)/);
    assert.doesNotMatch(style, /0 10px 36px/);
    assert.doesNotMatch(style, /0 12px 40px/);
    assert.match(aiChat, /<Sheet[\s\S]*SheetContent/);
    assert.match(aiChat, /:modal="false"/);
    assert.match(aiChat, /:overlay="false"/);
    assert.match(aiChat, /:close-on-outside="false"/);
    assert.match(aiChat, /resizable/);
    assert.match(sheetHandle, /role="separator"/);
    assert.match(sheetHandle, /rounded-l-xl/);
    assert.match(sheetHandle, /overflow-hidden/);
    assert.match(sheetHandle, /common\.resizeSheet/);
    assert.match(sheetHandle, /flush/);
    assert.match(sheetHandle, /translate-x-1\/2/);
    assert.doesNotMatch(sheetHandle, /hover:bg-foreground/);
    assert.match(sidebar, /SheetResizeHandle/);
    assert.match(sidebar, /data-slot="sidebar-panel"/);
    assert.match(sidebar, /data-inset/);
    assert.match(sidebar, /overflow-hidden rounded-xl/);
    assert.match(sidebar, /\bflush\b/);
    assert.match(sidebar, /--duration-ui/);
    assert.match(style, /\[data-slot="sidebar-panel"\]\[data-inset\]/);
    assert.match(style, /shadcn-vue-utilities\.css/);
    assert.match(style, /\[data-slot="sidebar-content"\]\.scroll-fade/);
    assert.match(sidebarContent, /scroll-fade/);
    assert.match(sidebarContent, /no-scrollbar/);
    assert.match(sidebarContent, /overflow-y-auto/);
    assert.match(sidebarContent, /useScrollFade/);
    assert.match(sidebarMenuSub, /sidebar-nav-indicator/);
    assert.match(sidebarMenuSub, /useSlidingIndicator/);
    assert.match(sidebarMenuSub, /edgeEnter:\s*true/);
    assert.match(collapsibleContent, /animate-collapsible-down/);
    assert.match(collapsibleContent, /--duration-ui/);
    assert.match(sheetHandle, /scale-x-150/);
    assert.doesNotMatch(sheetHandle, /h-14 w-1\.5/);
    assert.match(tabsList, /data-slot="tabs-indicator"/);
    assert.match(tabsList, /useTabsIndicator/);
    assert.match(tabsTrigger, /relative z-\[1\]/);
    assert.doesNotMatch(tabsTrigger, /data-\[state=active\]:bg-background/);
    assert.match(tabsContent, /fade-in-0/);
    assert.match(tabsContent, /--duration-ui/);
    assert.match(aiChat, /InputGroup/);
    assert.match(aiChat, /<Message /);
    assert.match(aiChat, /AiChatProcess/);
    assert.match(aiChat, /AiChatActions/);
    assert.match(aiChat, /turnLayout/);
    assert.match(aiChat, /isHiddenUserMessage/);
    assert.match(aiChat, /ai-thread/);
    assert.match(aiChat, /isLatestTurn/);
    assert.match(aiChat, /name="ai-fade"/);
    assert.match(aiMessageCss, /@keyframes ai-turn-in/);
    assert.match(aiMessageCss, /--duration-overlay/);
    assert.match(aiMessageCss, /ai-tool-enter-active/);
    assert.doesNotMatch(aiMessageCss, /animation-delay:\s*[1-9]/);
    assert.match(aiProcess, /enter:\s*\{ type: Boolean/);
    assert.match(aiProcess, /done:\s*\{ type: Boolean/);
    assert.match(aiProcess, /TransitionGroup/);
    assert.match(aiProcess, /name="ai-tool"/);
    assert.match(aiProcess, /processUsed/);
    assert.doesNotMatch(aiProcess, /InsetGroup/);
    assert.doesNotMatch(aiToolStep, /InsetGroupItem/);
    assert.doesNotMatch(aiToolStep, /:default-open="running"/);
    assert.doesNotMatch(aiPython, /scriptOpen/);
    assert.doesNotMatch(aiPython, /:default-open=/);
    assert.doesNotMatch(aiPython, /InsetGroup/);
    assert.match(aiTrigger, /--duration-overlay/);
    assert.doesNotMatch(aiTrigger, /ease-in-out/);
    assert.doesNotMatch(aiChat, /openTools|toolCategories|toolsTitle/);
  });
});

describe("button lift (Control Plane)", () => {
  it("adds a short white sheen and hairline drop shadow on default/outline/secondary/destructive", () => {
    for (const variant of ["default", "outline", "secondary", "destructive"]) {
      assert.match(
        style,
        new RegExp(`\\[data-slot="button"\\]\\[data-variant="${variant}"\\]`),
      );
    }
    assert.match(style, /0 1px 2px oklch\(0 0 0 \/ 0\.1\)/);
    assert.match(
      style,
      /linear-gradient\(\s*180deg,\s*oklch\(1 0 0 \/ 0\.08\)/,
    );
  });

  it("uses a dark sheen on inverted (light) fills in dark", () => {
    assert.match(
      style,
      /\.dark \[data-slot="button"\]\[data-variant="default"\][\s\S]*?oklch\(0 0 0 \/ 0\.12\)/,
    );
    assert.match(
      style,
      /\.dark \[data-slot="button"\]\[data-variant="outline"\][\s\S]*?oklch\(0 0 0 \/ 0\.16\)/,
    );
  });

  it("does not pin light-theme primary on html so .dark can invert buttons", () => {
    assert.match(brand, /removeProperty\("--primary"\)/);
    assert.match(brand, /DARK_BRAND_FILL/);
    assert.doesNotMatch(brand, /setProperty\("--primary", CSS_DEFAULTS/);
  });

  it("keeps ghost and link without a drop shadow", () => {
    assert.match(
      style,
      /\[data-slot="button"\]\[data-variant="ghost"\][\s\S]*?box-shadow:\s*none/,
    );
    assert.match(
      style,
      /\[data-slot="button"\]\[data-variant="ghost"\][\s\S]*background-image:\s*none/,
    );
  });
});

describe("joined split buttons", () => {
  it("uses the shared joined ButtonGroup for primary actions with dropdowns", () => {
    for (const source of [
      connectionPage,
      mappingCanvas,
      productMarketplaceIntegrations,
    ]) {
      assert.match(
        source,
        /<ButtonGroup joined[^>]*>[\s\S]*?<DropdownMenu>[\s\S]*?<\/ButtonGroup>/,
      );
    }
    assert.match(
      style,
      /\[data-slot="button-group"\]\[data-interaction="joined"\]:active\s*\{\s*transform: scale\(0\.98\)/,
    );
    assert.match(
      style,
      /\[data-slot="button-group"\]\[data-interaction="joined"\]\s*\[data-slot="button"\]:active\s*\{\s*scale: 1/,
    );
  });
});

describe("tabs chrome", () => {
  it("uses theme surfaces for the track and selected pill, not input/30 on muted", () => {
    assert.match(tabsList, /isolate bg-muted text-muted-foreground/);
    assert.match(tabsList, /dark:bg-muted\/20/);
    assert.match(
      tabsList,
      /data-slot="tabs-indicator"[\s\S]*bg-background shadow-sm dark:bg-muted dark:shadow-none/,
    );
    assert.doesNotMatch(tabsList, /dark:bg-input\/30/);
    assert.doesNotMatch(tabsList, /dark:border-input/);
  });
});

describe("scrollbars", () => {
  it("follows color-scheme and uses a 6px themed thumb on every scroller", () => {
    assert.match(style, /:root\s*\{[\s\S]*?color-scheme:\s*light/);
    assert.match(style, /\.dark \{\s*color-scheme:\s*dark/);
    assert.match(style, /scrollbar-width:\s*thin/);
    assert.match(style, /\*::-webkit-scrollbar \{\s*width:\s*6px/);
    assert.match(
      style,
      /scrollbar-color:\s*color-mix\(in oklch, var\(--muted-foreground\)/,
    );
  });
});

describe("focus rings", () => {
  it("draws the 3px ring inside the control so overflow-hidden chrome cannot clip it", () => {
    assert.match(style, /:focus-visible\s*\{[^}]*--tw-ring-inset:\s*inset/);
    assert.match(
      style,
      /\[data-slot="input-group"\]:has\(:focus-visible\)[\s\S]*?--tw-ring-inset:\s*inset/,
    );
    assert.match(
      style,
      /inset 0 0 0 3px color-mix\(in oklch, var\(--ring\) 50%, transparent\)/,
    );
    assert.match(
      style,
      /\[data-slot="nav-search"\]:has\(:focus-visible\)[\s\S]*?--tw-ring-shadow:\s*0 0 #0000/,
    );
  });
});
