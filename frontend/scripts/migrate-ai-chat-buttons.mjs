import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const path = fileURLToPath(new URL("../src/components/ai/AiChatPanel.vue", import.meta.url));
let content = readFileSync(path, "utf8");

const iconButtonOpen =
  /          <button\r?\n            type="button"\r?\n            class="h-7 w-7 flex items-center justify-center rounded-md text-muted-foreground hover:text-foreground hover:bg-accent transition-colors"/g;
const iconButtonReplacement = `          <Button
            variant="ghost"
            size="icon"
            class="size-7"`;

const before = (content.match(iconButtonOpen) || []).length;
content = content.replace(iconButtonOpen, iconButtonReplacement);

// Close tags for toolbar buttons (10-space indent)
content = content.replace(/          <\/button>/g, "          </Button>");

// Title input
content = content.replace(
  /<input\r?\n              ref="titleInputEl"\r?\n              v-model="editingTitleValue"\r?\n              class="pointer-events-auto text-sm font-medium text-center bg-transparent border-b border-primary\/60 outline-none w-full max-w-\[180px\] leading-tight py-px"\r?\n              placeholder="Название диалога"\r?\n              maxlength="100"\r?\n              @blur="saveTitle"\r?\n              @keydown\.enter\.prevent="saveTitle"\r?\n              @keydown\.escape\.prevent="cancelEditTitle"\r?\n            \/>/,
  `<Input
              ref="titleInputEl"
              v-model="editingTitleValue"
              class="pointer-events-auto h-auto text-sm font-medium text-center bg-transparent border-0 border-b border-primary/60 rounded-none shadow-none focus-visible:ring-0 w-full max-w-[180px] leading-tight py-px px-0"
              placeholder="Название диалога"
              maxlength="100"
              @blur="saveTitle"
              @keydown.enter.prevent="saveTitle"
              @keydown.escape.prevent="cancelEditTitle"
            />`,
);

// Textarea
content = content.replace(
  /<textarea\r?\n            ref="inputEl"\r?\n            v-model="inputText"\r?\n            class="w-full resize-none bg-transparent px-4 pt-3 pb-1 text-sm outline-none placeholder:text-muted-foreground\/60 disabled:opacity-50 font-\[inherit\] leading-relaxed"\r?\n            style="min-height: 44px; max-height: 120px;"\r?\n            :placeholder="hasEnabledModels \? 'Введите запрос\.\.\.' : 'Нет включённых AI-моделей'"\r?\n            rows="1"\r?\n            :disabled="isLoading \|\| !hasEnabledModels"\r?\n            @keydown\.enter\.exact\.prevent="submit"\r?\n            @input="autoResize"\r?\n          \/>/,
  `<Textarea
            ref="inputEl"
            v-model="inputText"
            class="min-h-[44px] max-h-[120px] resize-none border-0 bg-transparent px-4 pt-3 pb-1 shadow-none focus-visible:ring-0 font-[inherit] leading-relaxed"
            :placeholder="hasEnabledModels ? 'Введите запрос...' : 'Нет включённых AI-моделей'"
            rows="1"
            :disabled="isLoading || !hasEnabledModels"
            @keydown.enter.exact.prevent="submit"
            @input="autoResize"
          />`,
);

writeFileSync(path, content, "utf8");
console.log(`Replaced ${before} toolbar icon buttons.`);