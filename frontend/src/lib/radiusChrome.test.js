import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

function read(rel) {
  return readFileSync(new URL(rel, import.meta.url), "utf8");
}

const productEdit = read("../pages/productEditPage.vue");
const gallery = read("../components/products/ProductGallerySidebar.vue");
const modifiers = read("../components/marketplace/ModifierPillChain.vue");
const aiChat = read("../components/ai/AiChatPanel.vue");
const excel = read("../components/products/importExcelDialog.vue");
const bubble = read("../components/ui/bubble/index.js");
const progress = read("../components/ui/progress/Progress.vue");
const quota = read("../components/settings/QuotaUsagePanel.vue");
const connection = read("../pages/marketplace/connectionPage.vue");
const fabGroup = read("../components/FabGroup.vue");
const strip = read("../components/RightActionStrip.vue");
const avatar = read("../components/ui/avatar/Avatar.vue");
const radio = read("../components/ui/radio-group/RadioGroupItem.vue");
const sw = read("../components/ui/switch/Switch.vue");

describe("token radius instead of pills", () => {
  it("keeps composer, gallery, and lightbox chrome on rounded-md", () => {
    assert.match(modifiers, /rounded-md text-xs/);
    assert.doesNotMatch(modifiers, /rounded-full text-xs/);
    assert.match(aiChat, /size-8 -translate-x-1\/2 rounded-md/);
    assert.doesNotMatch(
      aiChat,
      /InputGroupButton[\s\S]{0,220}class="rounded-full"/,
    );
    assert.match(
      productEdit,
      /size="icon-lg"\s+class="absolute top-4 right-4(?: z-20)? rounded-md/,
    );
    assert.doesNotMatch(productEdit, /lightbox[\s\S]{0,80}rounded-full/);
    assert.match(gallery, /size-6 rounded-md bg-background/);
    assert.match(excel, /size-6 shrink-0[^\n]*rounded-md/);
    assert.match(bubble, /bubbleReactionsVariants[\s\S]*rounded-md/);
    assert.doesNotMatch(bubble, /bubbleReactionsVariants[\s\S]*rounded-full/);
    assert.match(progress, /rounded-md bg-primary\/20/);
    assert.doesNotMatch(progress, /rounded-full bg-primary\/20/);
    assert.match(quota, /overflow-hidden rounded-md bg-muted/);
    assert.match(connection, /overflow-hidden rounded-md bg-muted/);
  });

  it("keeps companion actions compact and geometric in the navbar", () => {
    assert.match(fabGroup, /data-companion-actions/);
    assert.match(fabGroup, /size-9 shrink-0/);
    assert.match(fabGroup, /documentationTitle/);
    assert.match(fabGroup, /CircleHelp/);
    assert.doesNotMatch(fabGroup, /BookOpen/);
    assert.match(fabGroup, /TooltipContent/);
    assert.match(fabGroup, /data-companion-open/);
    assert.match(fabGroup, /aria-pressed/);
    assert.match(strip, /size-10 rounded-full/);
  });

  it("keeps geometric circles for avatars, radios, and switches", () => {
    assert.match(avatar, /overflow-hidden rounded-full/);
    assert.match(radio, /aspect-square size-4 rounded-full/);
    assert.match(sw, /w-8 shrink-0 items-center rounded-full/);
  });
});
