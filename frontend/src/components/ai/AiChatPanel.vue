<script setup>
import { Spinner } from "@/components/ui/spinner";
import { ref, computed, nextTick, watch, onMounted, onUnmounted } from "vue";
import { useStore } from "vuex";
import { unrefElement } from "@vueuse/core";
import {
  X,
  ArrowUp,
  Bot,
  CheckCircle2,
  ExternalLink,
  Square,
  Copy,
  Check,
  ThumbsUp,
  ThumbsDown,
  Plus,
  Pencil,
  Trash2,
  ChevronLeft,
  History,
  Wrench,
  MessageSquare,
  ChevronDown,
  Boxes,
  FolderOpen,
  GitBranch,
  Plug,
  Upload,
  Download,
  Layers,
  HardDrive,
  BarChart2,
  Globe,
  Sparkles,
} from "lucide-vue-next";
import {
  Tooltip, TooltipContent, TooltipProvider, TooltipTrigger,
} from "@/components/ui/tooltip";
import { marked } from "marked";
import DOMPurify from "dompurify";
import AiPlanCard from "./AiPlanCard.vue";
import OperationContextBubble from "./OperationContextBubble.vue";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { ScrollArea } from "@/components/ui/scroll-area";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { addCalendarDays, calendarDayKey } from "@/utils/dateTime";
import { useI18n } from "vue-i18n";

const { t } = useI18n();
const store = useStore();
const {
  timezone: storeTimezone,
  relativeDayLabel,
  todayKey,
  loadMemberships,
} = useStoreDateTime();
loadMemberships();
const messagesEl = ref(null);
const inputEl = ref(null);
const inputText = ref("");
const isResizing = ref(false);
const isAtBottom = ref(true);

// View switching: 'chat' | 'history' | 'tools'
const activeView = ref("chat");

// Conversation list
const conversations = ref([]);
const convListLoading = ref(false);

// Inline title editing
const isEditingTitle = ref(false);
const editingTitleValue = ref("");
const titleInputEl = ref(null);

// Delete conversation confirmation
const confirmDeleteDialog = ref({ open: false, sessionId: null, resolve: null });

// Tools panel: which categories are expanded
const expandedCategories = ref(new Set(["products", "mappings", "analytics"]));

const isOpen = computed(() => store.state.aiChat.isOpen);
const isLoading = computed(() => store.state.aiChat.isLoading);
const messages = computed(() => store.state.aiChat.messages);
const panelWidth = computed(() => store.state.aiChat.panelWidth);
const conversationTitle = computed(() => store.state.aiChat.conversationTitle);
const sessionId = computed(() => store.state.aiChat.sessionId);
const lastAssistant = computed(() =>
  [...messages.value].reverse().find((m) => m.role === "assistant")
);
const hasRunningTool = computed(() =>
  lastAssistant.value?.toolCalls?.some((tc) => tc.status === "running") ?? false
);
const lastModelName = computed(() =>
  [...messages.value].reverse().find((m) => m.modelName)?.modelName ?? null
);

const allModels = computed(() => store.state.aiModels?.models ?? []);
const pickerModels = computed(() => store.state.aiModels?.pickerModels ?? []);
const enabledModels = computed(() => {
  const src = pickerModels.value.length ? pickerModels.value : allModels.value;
  return src.filter((m) => m.isEnabled !== false && (m.id ?? m.Id) != null);
});
const hasEnabledModels = computed(() => enabledModels.value.length > 0);
const selectedModelId = computed(() => store.state.aiChat.selectedModelId);
const defaultModel = computed(
  () =>
    enabledModels.value.find((m) => m.isDefault) ??
    enabledModels.value[0] ??
    null,
);
// The effective model shown in the picker: explicit enabled selection or fallback to default enabled
const effectiveModelId = computed(() => {
  if (selectedModelId.value) {
    const sel = enabledModels.value.find(
      (m) => String(m.id ?? m.Id) === String(selectedModelId.value),
    );
    if (sel) return String(sel.id ?? sel.Id);
  }
  return defaultModel.value ? String(defaultModel.value.id ?? defaultModel.value.Id) : "";
});

// Group conversations by relative date for history view (store calendar days)
const groupedConversations = computed(() => {
  const tz = storeTimezone.value;
  const today = todayKey();
  const yesterday = addCalendarDays(today, -1);
  const weekAgo = addCalendarDays(today, -7);

  const groups = [
    { label: t("ai.today"), items: [] },
    { label: t("ai.yesterday"), items: [] },
    { label: t("ai.thisWeek"), items: [] },
    { label: t("ai.earlier"), items: [] },
  ];
  for (const c of conversations.value) {
    const day = calendarDayKey(c.lastMessageAt, tz);
    if (!day) {
      groups[3].items.push(c);
      continue;
    }
    if (day >= today) groups[0].items.push(c);
    else if (day >= yesterday) groups[1].items.push(c);
    else if (day >= weekAgo) groups[2].items.push(c);
    else groups[3].items.push(c);
  }
  return groups.filter((g) => g.items.length > 0);
});

// Keep in sync with backend ToolDispatcher + BuildAllToolDefinitions (AiChatService.cs).
const TOOL_CATEGORY_DEFS = [
  { id: "products", icon: Boxes, tools: [
    "list_products", "get_products_count", "get_product", "create_product",
    "create_products", "update_product", "update_product_sku", "update_product_prices",
    "update_product_attribute", "update_product_attributes", "delete_products",
    "restore_products", "list_trash", "generate_barcodes",
  ]},
  { id: "categories", icon: FolderOpen, tools: [
    "list_categories", "get_category", "create_category", "update_category",
    "delete_category", "list_category_fields", "add_category_field",
    "update_category_field", "delete_category_field", "find_categories_with_field",
  ]},
  { id: "mappings", icon: GitBranch, tools: [
    "list_mappings", "get_mapping", "create_mapping", "update_mapping_name",
    "delete_mapping", "duplicate_mapping", "list_external_categories",
    "get_external_fields", "get_price_fields", "get_option_alignment_hints",
    "search_field_options", "get_available_sources", "get_mapping_coverage",
    "connect_field", "connect_fields", "set_field_static_value",
    "set_fields_static_value", "add_field_modifier", "remove_field_connection",
    "merge_adapter_fields", "unmerge_adapter_fields", "preview_mapping_for_product",
    "find_products_without_mapping", "get_product_mapping_summary", "assign_product_mappings",
  ]},
  { id: "connections", icon: Plug, tools: [
    "list_connections", "get_connection", "test_connection",
    "get_connection_limits", "get_adapter_info",
  ]},
  { id: "export", icon: Upload, tools: [
    "export_products_batch", "export_prices", "export_stocks",
    "get_batch_status", "list_product_links",
  ]},
  { id: "import", icon: Download, tools: [
    "preview_marketplace_products", "import_products", "import_prices",
    "import_stocks", "archive_product_on_marketplace",
  ]},
  { id: "groups", icon: Layers, tools: [
    "list_groups", "get_group", "create_group", "update_group",
    "delete_group", "add_products_to_group", "remove_products_from_group",
  ]},
  { id: "files", icon: HardDrive, tools: [
    "list_files", "create_folder", "rename_file", "rename_folder",
    "move_files", "delete_files", "delete_folder", "share_file",
  ]},
  { id: "analytics", icon: BarChart2, tools: [
    "get_store_stats", "find_products_without_images", "find_products_without_barcode",
    "find_products_missing_field", "find_products_with_sync_errors",
    "analyze_mapping_quality", "get_image_download_status",
  ]},
  { id: "store", icon: Globe, tools: [
    "list_store_attributes",
  ]},
  { id: "system", icon: Sparkles, tools: [
    "propose_plan", "rollback_last_operation", "navigate_to",
  ]},
];

const toolCategories = computed(() =>
  TOOL_CATEGORY_DEFS.map((cat) => ({
    name: t(`ai.toolCategories.${cat.id}`),
    id: cat.id,
    icon: cat.icon,
    tools: cat.tools.map((toolName) => ({
      name: toolName,
      desc: t(`ai.toolCatalog.${cat.id}.${toolName}`),
    })),
  })),
);

const totalToolCount = computed(() =>
  toolCategories.value.reduce((s, c) => s + c.tools.length, 0),
);

function toggleCategory(id) {
  const s = new Set(expandedCategories.value);
  if (s.has(id)) s.delete(id);
  else s.add(id);
  expandedCategories.value = s;
}

function close() { store.commit("aiChat/toggle"); }
function stopGeneration() { store.dispatch("aiChat/stopGeneration"); }
function confirmPlan() { store.dispatch("aiChat/confirmPlan"); }
function cancelPlan() { store.dispatch("aiChat/cancelPlan"); }

const copiedId = ref(null);
async function copyText(id, text) {
  try {
    await navigator.clipboard.writeText(text);
    copiedId.value = id;
    setTimeout(() => { if (copiedId.value === id) copiedId.value = null; }, 1500);
  } catch {}
}
function messageClipboard(msg) {
  const parts = [];
  if (msg.toolCalls?.length) {
    parts.push(msg.toolCalls.map((tc) =>
      `[${tc.name}${tc.input ? `(${tc.input})` : ""}${tc.summary ? ` → ${tc.summary}` : ""}]`
    ).join("\n"));
  }
  if (msg.content) parts.push(msg.content);
  return parts.join("\n");
}
function sendFeedback(msg, rating) {
  if (msg.feedbackRating === rating) return;
  store.dispatch("aiChat/sendFeedback", { dbId: msg.dbId, rating });
}

async function loadConversations() {
  convListLoading.value = true;
  try {
    const { http } = await import("@/main");
    const res = await http.get("/ai/conversations");
    conversations.value = res.data || [];
  } catch {
    conversations.value = [];
  } finally {
    convListLoading.value = false;
  }
}

async function openHistory() {
  activeView.value = "history";
  await loadConversations();
}

function openTools() {
  activeView.value = "tools";
}

function backToChat() {
  activeView.value = "chat";
}

async function switchConversation(sid) {
  if (sid === sessionId.value) { backToChat(); return; }
  await store.dispatch("aiChat/loadConversation", sid);
  backToChat();
}

function openConfirmDelete(e, sid) {
  e.stopPropagation();
  confirmDeleteDialog.value = { open: true, sessionId: sid };
}

async function confirmDeleteConversation() {
  const sid = confirmDeleteDialog.value.sessionId;
  confirmDeleteDialog.value = { open: false, sessionId: null };
  const prev = conversations.value;
  conversations.value = conversations.value.filter((c) => c.sessionId !== sid);
  try {
    await store.dispatch("aiChat/deleteConversation", sid);
  } catch {
    conversations.value = prev;
  }
}

function newConversation() {
  backToChat();
  store.dispatch("aiChat/startNewConversation");
}

function startEditTitle() {
  if (isLoading.value) return;
  editingTitleValue.value = conversationTitle.value || "";
  isEditingTitle.value = true;
  nextTick(() => {
    const el = unrefElement(titleInputEl);
    el?.focus?.();
    el?.select?.();
  });
}

async function saveTitle() {
  isEditingTitle.value = false;
  const trimmed = editingTitleValue.value.trim();
  if (trimmed && trimmed !== conversationTitle.value) {
    await store.dispatch("aiChat/renameConversation", trimmed);
  }
}

function cancelEditTitle() {
  isEditingTitle.value = false;
}

function formatRelativeDate(iso) {
  const tz = storeTimezone.value;
  const key = calendarDayKey(iso, tz);
  if (!key) return "-";
  const today = todayKey();
  const yesterday = addCalendarDays(today, -1);
  if (key === today) return t("ai.today");
  if (key === yesterday) return t("ai.yesterday");
  const [y, m, d] = key.split("-").map(Number);
  const [ty, tm, td] = today.split("-").map(Number);
  const diffDays = Math.round(
    (Date.UTC(ty, tm - 1, td) - Date.UTC(y, m - 1, d)) / 86400000,
  );
  if (diffDays > 1 && diffDays < 7) return t("ai.daysAgo", { n: diffDays });
  return relativeDayLabel(iso);
}

/** Template refs on shadcn wrappers are component instances - resolve to the real DOM node. */
function inputDom() {
  return unrefElement(inputEl) ?? null;
}

async function submit() {
  const text = inputText.value.trim();
  if (!text || isLoading.value || !hasEnabledModels.value) return;
  inputText.value = "";
  // Start the request first so a DOM resize glitch can never block sending
  const sendPromise = store.dispatch("aiChat/sendMessage", text);
  await nextTick();
  const el = inputDom();
  if (el?.style) el.style.height = "auto";
  await sendPromise;
}

function autoResize() {
  const el = inputDom();
  if (!el?.style) return;
  el.style.height = "auto";
  el.style.height = Math.min(el.scrollHeight, 120) + "px";
}

watch(() => messages.value.length, async () => {
  await nextTick();
  if (messagesEl.value) {
    messagesEl.value.scrollTop = messagesEl.value.scrollHeight;
    isAtBottom.value = true;
  }
});

watch(messages, async () => {
  await nextTick();
  if (isAtBottom.value && messagesEl.value) {
    messagesEl.value.scrollTop = messagesEl.value.scrollHeight;
  }
}, { deep: true });

function onScroll() {
  const el = messagesEl.value;
  if (!el) return;
  isAtBottom.value = el.scrollHeight - el.scrollTop - el.clientHeight < 100;
}

function handleKeydown(e) {
  if (e.key === "Escape" && isOpen.value) {
    if (activeView.value !== "chat") backToChat();
    else close();
  }
}
onMounted(() => {
  window.addEventListener("keydown", handleKeydown);
  store.dispatch("aiChat/loadLastConversation");
  store.dispatch("aiModels/getPickerModels", { onError: () => {} });
});
onUnmounted(() => window.removeEventListener("keydown", handleKeydown));

function startResize(e) {
  e.preventDefault();
  isResizing.value = true;
  const startX = e.clientX;
  const startWidth = panelWidth.value;

  function onMove(ev) {
    const w = Math.min(1000, Math.max(300, startWidth + (startX - ev.clientX)));
    store.commit("aiChat/setPanelWidth", w);
  }
  function onUp() {
    isResizing.value = false;
    window.removeEventListener("mousemove", onMove);
    window.removeEventListener("mouseup", onUp);
    localStorage.setItem("ai-panel-width", String(panelWidth.value));
  }
  window.addEventListener("mousemove", onMove);
  window.addEventListener("mouseup", onUp);
}

function renderMarkdown(text) {
  if (!text) return "";
  return DOMPurify.sanitize(marked.parse(text));
}
</script>

<template>
  <!-- Resizable right panel - part of the flex layout (second window).
       No width transition: animating width runs on the main thread and reflows the page. -->
  <div
    class="flex-none sticky top-0 h-screen bg-background border-l flex flex-col overflow-hidden"
    :style="{ width: isOpen ? panelWidth + 'px' : '0px' }"
  >
    <!-- Resize handle -->
    <div
      class="absolute left-0 top-0 bottom-0 w-1 z-10 cursor-col-resize hover:bg-primary/40 active:bg-primary/60 transition-colors"
      @mousedown="startResize"
    />

    <!-- Content (min-width prevents content reflow during collapse) -->
    <div class="flex flex-col h-full" :style="{ minWidth: panelWidth + 'px' }">

      <!-- ── Header: Chat view ── -->
      <div
        v-if="activeView === 'chat'"
        class="relative flex items-center h-11 px-2 shrink-0 border-b border-border/40"
      >
        <!-- Left: history + new + tools -->
        <div class="flex items-center gap-0.5 z-10">
          <Button
            variant="ghost"
            size="icon"
            class="size-7"
            :title="t('ai.historyTooltip')"
            @click="openHistory"
          >
            <History class="size-3.5" />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            class="size-7"
            :title="t('ai.newChatTooltip')"
            @click="newConversation"
          >
            <Plus class="size-3.5" />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            class="size-7"
            :title="t('ai.toolsTooltip')"
            @click="openTools"
          >
            <Wrench class="size-3.5" />
          </Button>
        </div>

        <!-- Center: title -->
        <div class="absolute inset-x-0 flex items-center justify-center px-24 pointer-events-none">
          <template v-if="isEditingTitle">
            <Input
              ref="titleInputEl"
              v-model="editingTitleValue"
              class="pointer-events-auto h-8 text-sm font-medium text-center bg-background border border-input rounded-md shadow-none focus-visible:ring-1 focus-visible:ring-ring/40 w-full max-w-[200px] leading-tight py-1 px-2"
              :placeholder="t('ai.conversationTitlePlaceholder')"
              maxlength="100"
              @blur="saveTitle"
              @keydown.enter.prevent="saveTitle"
              @keydown.escape.prevent="cancelEditTitle"
            />
          </template>
          <template v-else>
            <Button
              v-if="messages.length > 0"
              variant="ghost"
              class="pointer-events-auto group/title h-auto flex items-center gap-1 max-w-[180px] px-2 py-1 font-normal hover:bg-accent"
              :title="conversationTitle ? t('ai.renameTooltip') : undefined"
              @click="conversationTitle ? startEditTitle() : undefined"
            >
              <span class="text-sm font-medium truncate text-foreground/80 group-hover/title:text-foreground transition-colors">
                {{ conversationTitle || t("ai.newConversation") }}
              </span>
              <Pencil v-if="conversationTitle" class="size-3 opacity-0 group-hover/title:opacity-40 transition-opacity shrink-0" />
            </Button>
          </template>
        </div>

        <!-- Right: close -->
        <Button
          variant="ghost"
          size="icon"
          class="size-7 ml-auto z-10 text-muted-foreground"
          :title="t('ai.closeTooltip')"
          @click="close"
        >
          <X class="size-4" />
        </Button>
      </div>

      <!-- ── Header: History view ── -->
      <div
        v-else-if="activeView === 'history'"
        class="flex items-center h-11 px-2 shrink-0 border-b border-border/40 gap-1"
      >
        <Button
          variant="ghost"
          size="icon"
          class="size-7 text-muted-foreground"
          :title="t('ai.backToChat')"
          @click="backToChat"
        >
          <ChevronLeft class="size-4" />
        </Button>
        <span class="text-sm font-medium text-foreground/80 flex-1 ml-1">{{ t("ai.conversationHistory") }}</span>
        <Button
          variant="ghost"
          size="icon"
          class="size-7 text-muted-foreground"
          :title="t('ai.newChatTooltip')"
          @click="newConversation"
        >
          <Plus class="size-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          class="size-7 text-muted-foreground"
          :title="t('ai.closeTooltip')"
          @click="close"
        >
          <X class="size-4" />
        </Button>
      </div>

      <!-- ── Header: Tools view ── -->
      <div
        v-else
        class="flex items-center h-11 px-2 shrink-0 border-b border-border/40 gap-1"
      >
        <Button
          variant="ghost"
          size="icon"
          class="size-7 text-muted-foreground"
          :title="t('ai.backToChat')"
          @click="backToChat"
        >
          <ChevronLeft class="size-4" />
        </Button>
        <span class="text-sm font-medium text-foreground/80 flex-1 ml-1">{{ t("ai.toolsTitle") }}</span>
        <Button
          variant="ghost"
          size="icon"
          class="size-7 text-muted-foreground"
          :title="t('ai.closeTooltip')"
          @click="close"
        >
          <X class="size-4" />
        </Button>
      </div>

      <!-- ── Content: History view ── -->
      <div v-if="activeView === 'history'" class="flex-1 overflow-y-auto">
        <!-- Loading -->
        <div v-if="convListLoading" class="flex items-center justify-center py-10">
          <Spinner size="lg" class="text-muted-foreground" />
        </div>

        <!-- Empty state -->
        <div v-else-if="!conversations.length" class="flex flex-col items-center justify-center py-10 gap-2 text-center px-6">
          <MessageSquare class="size-8 text-muted-foreground/30" />
          <p class="text-sm text-muted-foreground">{{ t("ai.noConversations") }}</p>
          <Button
            variant="link"
            class="mt-1 h-auto p-0 flex items-center gap-1.5 text-xs"
            @click="newConversation"
          >
            <Plus class="size-3.5" /> {{ t("ai.startNew") }}
          </Button>
        </div>

        <!-- Conversation groups -->
        <template v-else>
          <div v-for="group in groupedConversations" :key="group.label">
            <div class="px-3 py-1.5 text-[10px] font-medium text-muted-foreground/60 uppercase tracking-wider sticky top-0 bg-background/95 backdrop-blur-sm">
              {{ group.label }}
            </div>
            <div
              v-for="c in group.items"
              :key="c.sessionId"
              class="group flex items-center gap-2 px-3 py-2 hover:bg-accent transition-colors cursor-pointer"
              :class="{ 'bg-accent': c.sessionId === sessionId }"
              @click="switchConversation(c.sessionId)"
            >
              <MessageSquare class="size-3.5 shrink-0 text-muted-foreground/50" />
              <div class="flex-1 min-w-0">
                <p class="text-sm truncate leading-snug">{{ c.title || t("ai.untitled") }}</p>
                <p class="text-[10px] text-muted-foreground mt-0.5">
                  {{ formatRelativeDate(c.lastMessageAt) }} · {{ c.messageCount }} {{ t("ai.messagesCountSuffix") }}
                </p>
              </div>
              <Button
                variant="ghost"
                size="icon"
                class="shrink-0 opacity-0 group-hover:opacity-100 size-6 text-destructive hover:bg-destructive/10 hover:text-destructive"
                :title="t('ai.deleteConversationTooltip')"
                @click="openConfirmDelete($event, c.sessionId)"
              >
                <Trash2 class="size-3.5" />
              </Button>
            </div>
          </div>
        </template>
      </div>

      <!-- ── Content: Tools view ── -->
      <div v-else-if="activeView === 'tools'" class="flex-1 overflow-y-auto">
        <div class="px-3 pt-3 pb-1">
          <p class="text-[10px] text-muted-foreground/60 uppercase tracking-wider">
            {{ t("ai.toolsCountLabel", { count: totalToolCount, categories: toolCategories.length }) }}
          </p>
        </div>
        <div class="px-2 pb-3 space-y-0.5">
          <div v-for="cat in toolCategories" :key="cat.id" class="rounded-md overflow-hidden">
            <!-- Category header -->
            <Button
              variant="ghost"
              class="w-full h-auto justify-start flex items-center gap-2 px-2 py-1.5 font-normal rounded-md"
              @click="toggleCategory(cat.id)"
            >
              <component :is="cat.icon" class="size-3.5 shrink-0 text-muted-foreground" />
              <span class="text-xs font-medium flex-1">{{ cat.name }}</span>
              <span class="text-[10px] text-muted-foreground tabular-nums">{{ cat.tools.length }}</span>
              <ChevronDown
                class="size-3 text-muted-foreground/60 transition-transform duration-150"
                :class="{ 'rotate-180': expandedCategories.has(cat.id) }"
              />
            </Button>
            <!-- Tool rows -->
            <div
              v-show="expandedCategories.has(cat.id)"
              class="px-2 pb-2 pt-0.5 flex flex-col gap-px"
            >
              <TooltipProvider
                v-for="tool in cat.tools"
                :key="tool.name"
                :delay-duration="300"
              >
                <Tooltip>
                  <TooltipTrigger as-child>
                    <div
                      class="flex items-center gap-1.5 text-xs text-muted-foreground font-mono
                             py-0.5 px-1 rounded hover:bg-accent/50 hover:text-foreground
                             transition-colors w-full select-none cursor-default"
                    >
                      <Wrench class="size-3 shrink-0 opacity-40" />
                      <span>{{ tool.name }}</span>
                    </div>
                  </TooltipTrigger>
                  <TooltipContent side="left" class="max-w-[200px]">
                    {{ tool.desc }}
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            </div>
          </div>
        </div>
      </div>

      <!-- ── Content: Chat view ── -->
      <div
        v-else
        ref="messagesEl"
        class="flex-1 overflow-y-auto p-4 flex flex-col gap-3"
        @scroll="onScroll"
      >
        <!-- Welcome state -->
        <div
          v-if="messages.length === 0"
          class="flex h-full flex-col items-center justify-center gap-3 text-center"
        >
          <div class="grid size-12 place-items-center rounded-full border bg-muted/40">
            <Bot class="size-6 text-muted-foreground" />
          </div>
          <div>
            <p class="text-sm font-medium">{{ hasEnabledModels ? t("ai.greeting") : t("ai.unavailable") }}</p>
            <p class="mx-auto mt-1 max-w-[260px] text-xs text-muted-foreground">
              <template v-if="hasEnabledModels">
                {{ t("ai.capabilitiesDescription") }}
              </template>
              <template v-else>
                {{ t("ai.noModelsEnabled") }}
              </template>
            </p>
          </div>
          <div
            v-if="hasEnabledModels"
            class="flex max-w-[280px] flex-wrap justify-center gap-1.5"
          >
            <Button
              v-for="example in [
                t('ai.exampleStats'),
                t('ai.exampleFillMapping'),
                t('ai.exampleFindNoPrice'),
              ]"
              :key="example"
              variant="outline"
              size="sm"
              class="h-auto rounded-full px-2.5 py-1 text-xs text-muted-foreground font-normal"
              @click="inputText = example; submit()"
            >
              {{ example }}
            </Button>
          </div>
        </div>

        <!-- Message list -->
        <template v-for="msg in messages" :key="msg.id">
          <!-- User message: context card or right-aligned bubble -->
          <div
            v-if="msg.role === 'user'"
            class="group flex flex-col gap-0.5"
            :class="msg.contextCard ? 'items-stretch w-full' : 'items-end'"
          >
            <OperationContextBubble
              v-if="msg.contextCard"
              :card="msg.contextCard"
              :raw="msg.content"
            />
            <div
              v-else
              class="bg-muted text-foreground rounded-2xl rounded-br-none px-4 py-2.5 text-sm max-w-[80%] break-words"
            >
              {{ msg.content }}
            </div>
            <Button
              variant="ghost"
              size="icon"
              class="size-7 opacity-0 group-hover:opacity-100 text-muted-foreground"
              :class="msg.contextCard ? 'self-end' : ''"
              :title="copiedId === msg.id ? t('ai.copied') : t('common.copy')"
              @click="copyText(msg.id, msg.content)"
            >
              <Check v-if="copiedId === msg.id" class="size-3.5 text-green-600" />
              <Copy v-else class="size-3.5" />
            </Button>
          </div>

          <!-- Assistant message: full width -->
          <div v-else class="group flex flex-col gap-2 w-full">
            <!-- Tool calls -->
            <div v-if="msg.toolCalls?.length" class="flex flex-col gap-px">
              <template v-for="tc in msg.toolCalls" :key="tc.id || tc.name">
                <details>
                  <summary class="list-none cursor-pointer">
                    <div class="flex items-center gap-1.5 text-xs text-muted-foreground font-mono py-0.5 hover:text-foreground transition-colors w-fit select-none">
                      <Spinner v-if="tc.status === 'running'" size="xs" class="shrink-0" />
                      <CheckCircle2 v-else class="size-3 shrink-0 text-green-500/80" />
                      <span>{{ tc.name }}</span>
                      <span v-if="tc.summary" class="opacity-50 truncate max-w-[200px]">→ {{ tc.summary }}</span>
                    </div>
                  </summary>
                  <div v-if="tc.input || tc.summary" class="ml-5 mt-0.5 mb-0.5 pl-2 border-l border-border/40 text-xs font-mono text-muted-foreground/60 space-y-0.5">
                    <div v-if="tc.input" class="break-all">{{ tc.input.length > 300 ? tc.input.slice(0, 300) + '…' : tc.input }}</div>
                  </div>
                </details>
                <RouterLink
                  v-if="tc.name === 'navigate_to' && tc.result?.navigate"
                  :to="tc.result.url"
                  class="ml-5 mt-1 inline-flex items-center gap-1.5 text-xs px-2.5 py-1 rounded-md bg-primary/10 text-primary hover:bg-primary/20 transition-colors font-medium w-fit"
                >
                  <ExternalLink class="size-3 shrink-0" />
                  {{ tc.result.label || tc.result.url }}
                </RouterLink>
              </template>
            </div>

            <!-- Plan card -->
            <AiPlanCard
              v-if="msg.plan"
              :plan="msg.plan"
              :confirmed="msg.planConfirmed"
              :cancelled="msg.planCancelled"
              @confirm="confirmPlan"
              @cancel="cancelPlan"
            />

            <!-- Text -->
            <div
              v-if="msg.content"
              class="ai-message text-sm leading-relaxed break-words"
              v-html="renderMarkdown(msg.content)"
            />

            <!-- Thinking indicator -->
            <div
              v-if="!msg.done && isLoading && msg === lastAssistant && !hasRunningTool && !msg.plan"
              class="flex items-center gap-1 h-5 mt-0.5"
            >
              <span class="ai-dot" /><span class="ai-dot" style="animation-delay:.2s" /><span class="ai-dot" style="animation-delay:.4s" />
            </div>

            <!-- Action row -->
            <div
              v-if="msg.done && (msg.content || msg.toolCalls?.length)"
              class="flex items-center gap-2.5 opacity-0 group-hover:opacity-100 transition-opacity"
            >
              <Button
                variant="ghost"
                size="icon"
                class="size-7 text-muted-foreground"
                :title="copiedId === msg.id ? t('ai.copied') : t('common.copy')"
                @click="copyText(msg.id, messageClipboard(msg))"
              >
                <Check v-if="copiedId === msg.id" class="size-3.5 text-green-600" />
                <Copy v-else class="size-3.5" />
              </Button>
              <Button
                variant="ghost"
                size="icon"
                class="size-7"
                :class="msg.feedbackRating === 1 ? 'text-green-600' : 'text-muted-foreground hover:text-green-600'"
                :title="t('ai.goodResponse')"
                @click="sendFeedback(msg, 1)"
              >
                <ThumbsUp class="size-3.5" />
              </Button>
              <Button
                variant="ghost"
                size="icon"
                class="size-7"
                :class="msg.feedbackRating === -1 ? 'text-red-500' : 'text-muted-foreground hover:text-red-500'"
                :title="t('ai.badResponse')"
                @click="sendFeedback(msg, -1)"
              >
                <ThumbsDown class="size-3.5" />
              </Button>
            </div>
          </div>
        </template>
      </div>

      <!-- ── Input area - only in chat view ── -->
      <div v-if="activeView === 'chat'" class="px-3 pb-3 pt-1.5 shrink-0">
        <div class="rounded-2xl border border-border/60 bg-background shadow-sm focus-within:ring-1 focus-within:ring-ring/40 transition-shadow overflow-hidden">
          <Textarea
            ref="inputEl"
            v-model="inputText"
            class="min-h-[44px] max-h-[120px] resize-none border-0 bg-transparent px-4 pt-3 pb-1 shadow-none focus-visible:ring-0 font-[inherit] leading-relaxed"
            :placeholder="hasEnabledModels ? t('ai.inputPlaceholder') : t('ai.noModelsPlaceholder')"
            rows="1"
            :disabled="isLoading || !hasEnabledModels"
            @keydown.enter.exact.prevent="submit"
            @input="autoResize"
          />
          <div class="flex items-center justify-between px-3 pb-2.5 pt-0.5">
            <Select
              v-if="enabledModels.length"
              :model-value="effectiveModelId"
              @update:model-value="(v) => store.commit('aiChat/setSelectedModel', v || null)"
            >
              <SelectTrigger
                class="h-auto border-0 shadow-none p-0 gap-1 text-[10px] font-mono
                       text-muted-foreground/50 hover:text-muted-foreground focus:ring-0
                       w-auto bg-transparent [&>svg]:size-2.5 [&>svg]:opacity-50"
              >
                <SelectValue :placeholder="lastModelName ?? t('ai.modelPlaceholder')" />
              </SelectTrigger>
              <SelectContent align="start" side="top" class="min-w-[160px]">
                <SelectItem
                  v-for="m in enabledModels"
                  :key="m.id"
                  :value="String(m.id)"
                  class="text-xs"
                >
                  {{ m.name }}
                </SelectItem>
              </SelectContent>
            </Select>
            <span
              v-else
              class="text-[10px] text-muted-foreground/60 font-mono select-none leading-none max-w-[200px] truncate"
              :title="t('ai.noModelsPlaceholder')"
            >
              {{ t("ai.noEnabledModels") }}
            </span>
            <Button
              v-if="isLoading"
              variant="secondary"
              size="icon"
              class="size-7 rounded-full shrink-0"
              :title="t('ai.stopGeneration')"
              @click="stopGeneration"
            >
              <Square class="size-3" />
            </Button>
            <Button
              v-else
              size="icon"
              class="size-7 rounded-full shrink-0"
              :variant="inputText.trim() ? 'default' : 'secondary'"
              :disabled="!inputText.trim() || !hasEnabledModels"
              :title="hasEnabledModels ? t('ai.send') : t('ai.noModelsPlaceholder')"
              @click="submit"
            >
              <ArrowUp class="size-4" />
            </Button>
          </div>
        </div>
      </div>

    </div>

    <!-- Delete conversation confirmation -->
    <AlertDialog
      :open="confirmDeleteDialog.open"
      @update:open="(v) => { if (!v) confirmDeleteDialog.open = false; }"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ t("ai.deleteConversationTitle") }}</AlertDialogTitle>
          <AlertDialogDescription>{{ t("ai.deleteConversationDescription") }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel @click="confirmDeleteDialog.open = false">{{ t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            @click="confirmDeleteConversation"
          >{{ t("common.delete") }}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>

<style scoped>
.ai-dot {
  display: inline-block;
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: currentColor;
  opacity: 0.4;
  animation: ai-bounce 1s ease-in-out infinite;
}
@keyframes ai-bounce {
  0%, 80%, 100% { transform: translateY(0); opacity: 0.4; }
  40% { transform: translateY(-4px); opacity: 1; }
}

/* Markdown content styling */
.ai-message :deep(h1),
.ai-message :deep(h2),
.ai-message :deep(h3),
.ai-message :deep(h4) {
  font-weight: 600;
  margin-top: 0.6em;
  margin-bottom: 0.25em;
  line-height: 1.3;
}
.ai-message :deep(h1) { font-size: 1.05em; }
.ai-message :deep(h2) { font-size: 1em; }
.ai-message :deep(h3) { font-size: 0.95em; }
.ai-message :deep(p) { margin: 0.3em 0; }
.ai-message :deep(p:first-child) { margin-top: 0; }
.ai-message :deep(p:last-child) { margin-bottom: 0; }
.ai-message :deep(ul),
.ai-message :deep(ol) {
  margin: 0.3em 0;
  padding-left: 1.4em;
}
.ai-message :deep(li) { margin: 0.1em 0; }
.ai-message :deep(strong) { font-weight: 600; }
.ai-message :deep(em) { font-style: italic; }
.ai-message :deep(code) {
  font-family: ui-monospace, monospace;
  font-size: 0.85em;
  background: hsl(var(--background));
  border: 1px solid hsl(var(--border));
  border-radius: 3px;
  padding: 0.1em 0.35em;
}
.ai-message :deep(pre) {
  background: hsl(var(--background));
  border: 1px solid hsl(var(--border));
  border-radius: 6px;
  padding: 0.6em 0.8em;
  margin: 0.4em 0;
  overflow-x: auto;
  font-size: 0.82em;
}
.ai-message :deep(pre code) {
  background: none;
  border: none;
  padding: 0;
  font-size: 1em;
}
.ai-message :deep(table) {
  width: 100%;
  border-collapse: collapse;
  margin: 0.4em 0;
  font-size: 0.9em;
}
.ai-message :deep(th),
.ai-message :deep(td) {
  border: 1px solid hsl(var(--border));
  padding: 0.25em 0.5em;
  text-align: left;
}
.ai-message :deep(th) { font-weight: 600; background: hsl(var(--background)); }
.ai-message :deep(hr) { border: none; border-top: 1px solid hsl(var(--border)); margin: 0.5em 0; }
.ai-message :deep(blockquote) {
  border-left: 3px solid hsl(var(--border));
  margin: 0.3em 0;
  padding-left: 0.7em;
  color: hsl(var(--muted-foreground));
}
</style>
