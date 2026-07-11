import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const path = fileURLToPath(new URL("../src/components/ai/AiChatPanel.vue", import.meta.url));
let c = readFileSync(path, "utf8");

c = c.replace(/<button\b/g, "<Button");
c = c.replace(/<\/button>/g, "</Button>");

// Icon toolbar buttons missing variant
c = c.replace(
  /<Button\r?\n            type="button"\r?\n            class="h-7 w-7 flex items-center justify-center rounded-md text-muted-foreground hover:text-foreground hover:bg-accent transition-colors([^"]*)"/g,
  '<Button variant="ghost" size="icon" class="size-7$1"',
);
c = c.replace(/type="button"\r?\n            /g, "");

// Title edit button
c = c.replace(
  /<Button\r?\n              v-if="messages.length > 0"\r?\n              type="button"\r?\n              class="pointer-events-auto group\/title flex items-center gap-1 max-w-\[180px\]"/,
  '<Button v-if="messages.length > 0" variant="ghost" class="pointer-events-auto group/title flex items-center gap-1 max-w-[180px] h-auto px-1 py-0"',
);

// Link-style empty state
c = c.replace(
  /<Button\r?\n            type="button"\r?\n            class="mt-1 flex items-center gap-1\.5 text-xs text-primary hover:underline"/,
  '<Button variant="link" class="mt-1 h-auto p-0 text-xs"',
);

// Delete conv button
c = c.replace(
  /<Button\r?\n                type="button"\r?\n                class="shrink-0 opacity-0 group-hover:opacity-100 h-6 w-6 flex items-center justify-center rounded text-muted-foreground hover:text-destructive hover:bg-destructive\/10 transition-all"/,
  '<Button variant="ghost" size="icon" class="shrink-0 opacity-0 group-hover:opacity-100 size-6 text-muted-foreground hover:text-destructive hover:bg-destructive/10"',
);

// Category toggle
c = c.replace(
  /<Button\r?\n              type="button"\r?\n              class="w-full flex items-center gap-2 px-2 py-1\.5 text-left hover:bg-accent transition-colors rounded-md"/,
  '<Button variant="ghost" class="w-full h-auto justify-start gap-2 px-2 py-1.5"',
);

// Example chips
c = c.replace(
  /<Button\r?\n              v-for="example in \[/,
  '<Button v-for="example in [',
);
c = c.replace(
  /              type="button"\r?\n              class="rounded-full border bg-background px-2\.5 py-1 text-xs text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"/g,
  '              variant="outline" size="sm" class="rounded-full h-auto px-2.5 py-1 text-xs"',
);

// Copy / feedback icon buttons
c = c.replace(
  /<Button\r?\n              type="button"\r?\n              class="opacity-0 group-hover:opacity-100 transition-opacity text-muted-foreground hover:text-foreground"/g,
  '<Button variant="ghost" size="icon" class="opacity-0 group-hover:opacity-100 size-7"',
);
c = c.replace(
  /<Button\r?\n                type="button"\r?\n                class="text-muted-foreground hover:text-foreground transition-colors"/g,
  '<Button variant="ghost" size="icon" class="size-7"',
);
c = c.replace(
  /<Button\r?\n                type="button"\r?\n                :class="\['transition-colors', msg\.feedbackRating === 1 \? 'text-green-600' : 'text-muted-foreground hover:text-green-600'\]"/g,
  '<Button variant="ghost" size="icon" class="size-7" :class="[\'transition-colors\', msg.feedbackRating === 1 ? \'text-green-600\' : \'text-muted-foreground hover:text-green-600\']"',
);
c = c.replace(
  /<Button\r?\n                type="button"\r?\n                :class="\['transition-colors', msg\.feedbackRating === -1 \? 'text-red-500' : 'text-muted-foreground hover:text-red-500'\]"/g,
  '<Button variant="ghost" size="icon" class="size-7" :class="[\'transition-colors\', msg.feedbackRating === -1 ? \'text-red-500\' : \'text-muted-foreground hover:text-red-500\']"',
);

// Submit / stop
c = c.replace(
  /<Button\r?\n              v-if="isLoading"\r?\n              type="button"\r?\n              class="size-7 rounded-full bg-foreground text-background flex items-center justify-center hover:bg-foreground\/80 transition-colors shrink-0"/,
  '<Button v-if="isLoading" size="icon" class="size-7 rounded-full bg-foreground text-background hover:bg-foreground/80 shrink-0"',
);
c = c.replace(
  /<Button\r?\n              v-else\r?\n              type="button"\r?\n              class="size-7 rounded-full flex items-center justify-center transition-colors shrink-0 disabled:pointer-events-none"/,
  '<Button v-else size="icon" class="size-7 rounded-full shrink-0 disabled:pointer-events-none"',
);

// Strip leftover type="button"
c = c.replace(/\r?\n\s*type="button"/g, "");

writeFileSync(path, c, "utf8");
console.log("Fixed AiChatPanel button tags.");