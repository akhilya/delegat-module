import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const style = readFileSync(new URL("../style.css", import.meta.url), "utf8");
const button = readFileSync(
  new URL("../components/ui/button/index.js", import.meta.url),
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
  new URL("../components/ui/collapsible/CollapsibleContent.vue", import.meta.url),
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

  it("fades region loaders without a card ring or page-load stagger", () => {
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
  });

  it("presses buttons without bounce and keeps sheets under 300ms", () => {
    assert.match(button, /active:scale-\[0\.98\]/);
    assert.doesNotMatch(button, /bounce|elastic/);
    assert.match(sheet, /--duration-drawer/);
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
    assert.match(sheet, /data-resizable/);
    assert.match(style, /\[data-slot="sheet-content"\]\[data-inset\]/);
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
    assert.doesNotMatch(aiChat, /openTools|toolCategories|toolsTitle/);
  });
});

describe("button lift (Control Plane)", () => {
  it("adds a short white sheen and hairline drop shadow on default/outline/secondary/destructive", () => {
    assert.match(
      style,
      /\[data-variant="secondary"\],\s*\[data-slot="button"\]\[data-variant="destructive"\]/,
    );
    assert.match(style, /box-shadow:\s*0 1px 2px oklch\(0 0 0 \/ 0\.1\)/);
    assert.match(
      style,
      /linear-gradient\(\s*180deg,\s*oklch\(1 0 0 \/ 0\.08\)/,
    );
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
