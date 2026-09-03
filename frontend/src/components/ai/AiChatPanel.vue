<script setup>
import { Spinner } from "@/components/ui/spinner";
import { ref, computed, nextTick, watch, onMounted } from "vue";
import { useStore } from "vuex";
import { unrefElement } from "@vueuse/core";
import {
  ArrowDown,
  ArrowUp,
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
  MessageSquare,
} from "lucide-vue-next";
import { marked } from "marked";
import DOMPurify from "dompurify";
import AiPlanCard from "./AiPlanCard.vue";
import OperationContextBubble from "./OperationContextBubble.vue";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetHeaderClose,
  SheetTitle,
} from "@/components/ui/sheet";
import { EmptyState } from "@/components/ui/empty-state";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import {
  Message,
  MessageContent,
  MessageFooter,
} from "@/components/ui/message";
import { Bubble, BubbleContent } from "@/components/ui/bubble";
import { Marker, MarkerContent } from "@/components/ui/marker";
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupTextarea,
} from "@/components/ui/input-group";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { SHEET_WIDTH_STORAGE } from "@/composables/useSheetResize";
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
const isAtBottom = ref(true);
const aiWidthStorageKey = SHEET_WIDTH_STORAGE.ai;

const activeView = ref("chat");

const conversations = ref([]);
const convListLoading = ref(false);

const isEditingTitle = ref(false);
const editingTitleValue = ref("");
const titleInputEl = ref(null);

const confirmDeleteDialog = ref({
  open: false,
  sessionId: null,
  resolve: null,
});

const isOpen = computed(() => store.state.aiChat.isOpen);
const isLoading = computed(() => store.state.aiChat.isLoading);
const messages = computed(() => store.state.aiChat.messages);
const panelWidth = computed(() => store.state.aiChat.panelWidth);
const conversationTitle = computed(() => store.state.aiChat.conversationTitle);
const sessionId = computed(() => store.state.aiChat.sessionId);
const lastAssistant = computed(() =>
  [...messages.value].reverse().find((m) => m.role === "assistant"),
);
const hasRunningTool = computed(
  () =>
    lastAssistant.value?.toolCalls?.some((tc) => tc.status === "running") ??
    false,
);
const lastModelName = computed(
  () =>
    [...messages.value].reverse().find((m) => m.modelName)?.modelName ?? null,
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
const effectiveModelId = computed(() => {
  if (selectedModelId.value) {
    const sel = enabledModels.value.find(
      (m) => String(m.id ?? m.Id) === String(selectedModelId.value),
    );
    if (sel) return String(sel.id ?? sel.Id);
  }
  return defaultModel.value
    ? String(defaultModel.value.id ?? defaultModel.value.Id)
    : "";
});

const examplePrompts = computed(() => [
  t("ai.exampleStats"),
  t("ai.exampleFillMapping"),
  t("ai.exampleFindNoPrice"),
]);

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

function onOpenChange(open) {
  if (open) return;
  store.commit("aiChat/setOpen", false);
  activeView.value = "chat";
}

function onSheetEscape(event) {
  if (activeView.value === "history") {
    event.preventDefault();
    backToChat();
  }
}

function onOpenAutoFocus(event) {
  if (activeView.value !== "chat") return;
  event.preventDefault();
  nextTick(() => inputDom()?.focus());
}

function stopGeneration() {
  store.dispatch("aiChat/stopGeneration");
}
function confirmPlan() {
  store.dispatch("aiChat/confirmPlan");
}
function cancelPlan() {
  store.dispatch("aiChat/cancelPlan");
}

const copiedId = ref(null);
async function copyText(id, text) {
  try {
    await navigator.clipboard.writeText(text);
    copiedId.value = id;
    setTimeout(() => {
      if (copiedId.value === id) copiedId.value = null;
    }, 1500);
  } catch {
    /* clipboard may be blocked */
  }
}
function messageClipboard(msg) {
  const parts = [];
  if (msg.toolCalls?.length) {
    parts.push(
      msg.toolCalls
        .map(
          (tc) =>
            `[${tc.name}${tc.input ? `(${tc.input})` : ""}${tc.summary ? ` → ${tc.summary}` : ""}]`,
        )
        .join("\n"),
    );
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

function backToChat() {
  activeView.value = "chat";
}

async function switchConversation(sid) {
  if (sid === sessionId.value) {
    backToChat();
    return;
  }
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

function inputDom() {
  return unrefElement(inputEl) ?? null;
}

async function submit() {
  const text = inputText.value.trim();
  if (!text || isLoading.value || !hasEnabledModels.value) return;
  inputText.value = "";
  const sendPromise = store.dispatch("aiChat/sendMessage", text);
  await nextTick();
  const el = inputDom();
  if (el?.style) el.style.height = "auto";
  await sendPromise;
}

function submitExample(example) {
  inputText.value = example;
  submit();
}

function onComposerKeydown(e) {
  if (e.key !== "Enter" || e.shiftKey) return;
  e.preventDefault();
  submit();
}

watch(
  () => messages.value.length,
  async () => {
    await nextTick();
    if (messagesEl.value) {
      messagesEl.value.scrollTop = messagesEl.value.scrollHeight;
      isAtBottom.value = true;
    }
  },
);

watch(
  messages,
  async () => {
    await nextTick();
    if (isAtBottom.value && messagesEl.value) {
      messagesEl.value.scrollTop = messagesEl.value.scrollHeight;
    }
  },
  { deep: true },
);

function onScroll() {
  const el = messagesEl.value;
  if (!el) return;
  isAtBottom.value = el.scrollHeight - el.scrollTop - el.clientHeight < 100;
}

function scrollToLatest() {
  const el = messagesEl.value;
  if (!el) return;
  el.scrollTop = el.scrollHeight;
  isAtBottom.value = true;
}

onMounted(() => {
  store.dispatch("aiChat/loadLastConversation");
  store.dispatch("aiModels/getPickerModels", { onError: () => {} });
});

function onPanelWidth(w) {
  store.commit("aiChat/setPanelWidth", w);
}

function renderMarkdown(text) {
  if (!text) return "";
  return DOMPurify.sanitize(marked.parse(text));
}
</script>

<template>
  <Sheet :open="isOpen" :modal="false" @update:open="onOpenChange">
    <SheetContent
      side="right"
      inset
      resizable
      :overlay="false"
      :close-on-outside="false"
      :show-close="false"
      class="gap-0 p-0"
      :width="panelWidth"
      :default-width="400"
      :storage-key="aiWidthStorageKey"
      :aria-label="t('ai.openAssistant')"
      @update:width="onPanelWidth"
      @escape-key-down="onSheetEscape"
      @open-auto-focus="onOpenAutoFocus"
    >
      <SheetHeader v-if="activeView === 'chat'" variant="bar">
        <div class="flex min-h-8 items-center gap-2">
          <Button
            variant="ghost"
            size="icon"
            class="size-8 shrink-0"
            :title="t('ai.historyTooltip')"
            :aria-label="t('ai.historyTooltip')"
            @click="openHistory"
          >
            <History class="size-4" />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            class="size-8 shrink-0"
            :title="t('ai.newChatTooltip')"
            :aria-label="t('ai.newChatTooltip')"
            @click="newConversation"
          >
            <Plus class="size-4" />
          </Button>
          <div class="min-w-0 flex-1">
            <Input
              v-if="isEditingTitle"
              ref="titleInputEl"
              v-model="editingTitleValue"
              class="h-8 shadow-none"
              :placeholder="t('ai.conversationTitlePlaceholder')"
              maxlength="100"
              @blur="saveTitle"
              @keydown.enter.prevent="saveTitle"
              @keydown.escape.prevent="cancelEditTitle"
            />
            <SheetTitle v-else class="min-w-0 truncate">
              {{ conversationTitle || t("ai.newConversation") }}
            </SheetTitle>
          </div>
          <Button
            v-if="!isEditingTitle && messages.length > 0 && conversationTitle"
            variant="ghost"
            size="icon"
            class="size-8 shrink-0"
            :title="t('ai.renameTooltip')"
            :aria-label="t('ai.renameTooltip')"
            @click="startEditTitle"
          >
            <Pencil class="size-4" />
          </Button>
          <SheetHeaderClose />
        </div>
        <SheetDescription class="sr-only">
          {{ t("ai.capabilitiesDescription") }}
        </SheetDescription>
      </SheetHeader>

      <SheetHeader v-else variant="bar">
        <div class="flex min-h-8 items-center gap-2">
          <Button
            variant="ghost"
            size="icon"
            class="size-8 shrink-0"
            :title="t('ai.backToChat')"
            :aria-label="t('ai.backToChat')"
            @click="backToChat"
          >
            <ChevronLeft class="size-4" />
          </Button>
          <SheetTitle class="min-w-0 flex-1 truncate">
            {{ t("ai.conversationHistory") }}
          </SheetTitle>
          <Button
            variant="ghost"
            size="icon"
            class="size-8 shrink-0"
            :title="t('ai.newChatTooltip')"
            :aria-label="t('ai.newChatTooltip')"
            @click="newConversation"
          >
            <Plus class="size-4" />
          </Button>
          <SheetHeaderClose />
        </div>
        <SheetDescription class="sr-only">
          {{ t("ai.conversationHistory") }}
        </SheetDescription>
      </SheetHeader>

      <div
        v-if="activeView === 'history'"
        class="min-h-0 flex-1 overflow-y-auto px-4 py-4"
      >
        <EmptyState v-if="convListLoading" loading :framed="false" />
        <EmptyState
          v-else-if="!conversations.length"
          :framed="false"
          :title="t('ai.noConversations')"
        >
          <template #icon>
            <MessageSquare />
          </template>
          <Button variant="outline" size="sm" @click="newConversation">
            <Plus class="size-3.5" />
            {{ t("ai.startNew") }}
          </Button>
        </EmptyState>
        <div v-else class="flex flex-col gap-4">
          <section v-for="group in groupedConversations" :key="group.label">
            <InsetGroupLabel>{{ group.label }}</InsetGroupLabel>
            <InsetGroup>
              <InsetGroupItem
                v-for="c in group.items"
                :key="c.sessionId"
                :selected="c.sessionId === sessionId"
                @click="switchConversation(c.sessionId)"
              >
                <MessageSquare class="size-4 shrink-0" />
                <div class="min-w-0 flex-1">
                  <p class="truncate leading-snug">
                    {{ c.title || t("ai.untitled") }}
                  </p>
                  <p class="mt-0.5 text-xs text-muted-foreground">
                    {{ formatRelativeDate(c.lastMessageAt) }}
                    · {{ c.messageCount }} {{ t("ai.messagesCountSuffix") }}
                  </p>
                </div>
                <template #trailing>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                    :title="t('ai.deleteConversationTooltip')"
                    :aria-label="t('ai.deleteConversationTooltip')"
                    @click="openConfirmDelete($event, c.sessionId)"
                  >
                    <Trash2 class="size-4" />
                  </Button>
                </template>
              </InsetGroupItem>
            </InsetGroup>
          </section>
        </div>
      </div>

      <div v-else class="relative min-h-0 flex-1">
        <div
          ref="messagesEl"
          class="h-full overflow-y-auto px-4 py-4"
          @scroll="onScroll"
        >
          <EmptyState
            v-if="messages.length === 0"
            :framed="false"
            :title="hasEnabledModels ? t('ai.greeting') : t('ai.unavailable')"
            :description="
              hasEnabledModels
                ? t('ai.capabilitiesDescription')
                : t('ai.noModelsEnabled')
            "
          >
            <div
              v-if="hasEnabledModels"
              class="flex max-w-[280px] flex-wrap justify-center gap-1.5"
            >
              <Bubble
                v-for="example in examplePrompts"
                :key="example"
                variant="outline"
                as="button"
                type="button"
                @click="submitExample(example)"
              >
                <BubbleContent
                  class="px-2.5 py-1 text-xs text-muted-foreground"
                >
                  {{ example }}
                </BubbleContent>
              </Bubble>
            </div>
          </EmptyState>

          <div v-else class="flex flex-col gap-4">
            <template v-for="msg in messages" :key="msg.id">
              <Message v-if="msg.role === 'user'" align="end">
                <MessageContent>
                  <OperationContextBubble
                    v-if="msg.contextCard"
                    :card="msg.contextCard"
                    :raw="msg.content"
                  />
                  <Bubble v-else variant="muted" align="end">
                    <BubbleContent>{{ msg.content }}</BubbleContent>
                  </Bubble>
                  <MessageFooter
                    class="opacity-0 transition-opacity group-hover/message:opacity-100"
                  >
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-7 text-muted-foreground"
                      :title="
                        copiedId === msg.id ? t('ai.copied') : t('common.copy')
                      "
                      @click="copyText(msg.id, msg.content)"
                    >
                      <Check
                        v-if="copiedId === msg.id"
                        class="size-3.5 text-green-600"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                  </MessageFooter>
                </MessageContent>
              </Message>

              <Message v-else align="start">
                <MessageContent>
                  <div
                    v-if="msg.toolCalls?.length"
                    class="flex w-full flex-col gap-1"
                  >
                    <template
                      v-for="tc in msg.toolCalls"
                      :key="tc.id || tc.name"
                    >
                      <details>
                        <summary class="list-none cursor-pointer">
                          <Marker>
                            <Spinner
                              v-if="tc.status === 'running'"
                              size="xs"
                              class="shrink-0"
                            />
                            <CheckCircle2
                              v-else
                              class="size-3.5 shrink-0 text-green-500/80"
                            />
                            <MarkerContent class="font-mono text-xs">
                              {{ tc.name }}
                              <span v-if="tc.summary" class="opacity-50">
                                → {{ tc.summary }}</span
                              >
                            </MarkerContent>
                          </Marker>
                        </summary>
                        <div
                          v-if="tc.input || tc.summary"
                          class="ml-6 mt-0.5 mb-0.5 border-l border-border/40 pl-2 font-mono text-xs text-muted-foreground/60"
                        >
                          <div v-if="tc.input" class="break-all">
                            {{
                              tc.input.length > 300
                                ? tc.input.slice(0, 300) + "…"
                                : tc.input
                            }}
                          </div>
                        </div>
                      </details>
                      <RouterLink
                        v-if="tc.name === 'navigate_to' && tc.result?.navigate"
                        :to="tc.result.url"
                        class="ml-6 mt-1 inline-flex w-fit items-center gap-1.5 rounded-md bg-primary/10 px-2.5 py-1 text-xs font-medium text-primary transition-colors hover:bg-primary/20"
                      >
                        <ExternalLink class="size-3 shrink-0" />
                        {{ tc.result.label || tc.result.url }}
                      </RouterLink>
                    </template>
                  </div>

                  <AiPlanCard
                    v-if="msg.plan"
                    :plan="msg.plan"
                    :confirmed="msg.planConfirmed"
                    :cancelled="msg.planCancelled"
                    @confirm="confirmPlan"
                    @cancel="cancelPlan"
                  />

                  <Bubble v-if="msg.content" variant="ghost">
                    <BubbleContent class="w-full max-w-none p-0">
                      <div
                        class="ai-message text-sm leading-relaxed"
                        v-html="renderMarkdown(msg.content)"
                      />
                    </BubbleContent>
                  </Bubble>

                  <Marker
                    v-if="
                      !msg.done &&
                      isLoading &&
                      msg === lastAssistant &&
                      !hasRunningTool &&
                      !msg.plan
                    "
                    class="h-5"
                  >
                    <MarkerContent class="flex items-center gap-1">
                      <span class="ai-dot" />
                      <span class="ai-dot" style="animation-delay: 0.2s" />
                      <span class="ai-dot" style="animation-delay: 0.4s" />
                    </MarkerContent>
                  </Marker>

                  <MessageFooter
                    v-if="msg.done && (msg.content || msg.toolCalls?.length)"
                    class="gap-1 opacity-0 transition-opacity group-hover/message:opacity-100"
                  >
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-7 text-muted-foreground"
                      :title="
                        copiedId === msg.id ? t('ai.copied') : t('common.copy')
                      "
                      @click="copyText(msg.id, messageClipboard(msg))"
                    >
                      <Check
                        v-if="copiedId === msg.id"
                        class="size-3.5 text-green-600"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-7"
                      :class="
                        msg.feedbackRating === 1
                          ? 'text-green-600'
                          : 'text-muted-foreground hover:text-green-600'
                      "
                      :title="t('ai.goodResponse')"
                      @click="sendFeedback(msg, 1)"
                    >
                      <ThumbsUp class="size-3.5" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-7"
                      :class="
                        msg.feedbackRating === -1
                          ? 'text-red-500'
                          : 'text-muted-foreground hover:text-red-500'
                      "
                      :title="t('ai.badResponse')"
                      @click="sendFeedback(msg, -1)"
                    >
                      <ThumbsDown class="size-3.5" />
                    </Button>
                  </MessageFooter>
                </MessageContent>
              </Message>
            </template>
          </div>
        </div>

        <Button
          v-if="!isAtBottom && messages.length > 0"
          variant="outline"
          size="icon"
          class="absolute bottom-3 left-1/2 z-10 size-8 -translate-x-1/2 rounded-full"
          :title="t('ai.jumpToLatest')"
          :aria-label="t('ai.jumpToLatest')"
          @click="scrollToLatest"
        >
          <ArrowDown class="size-4" />
        </Button>
      </div>

      <SheetFooter v-if="activeView === 'chat'" variant="bar">
        <InputGroup class="w-full rounded-xl">
          <InputGroupTextarea
            ref="inputEl"
            v-model="inputText"
            class="min-h-[44px] max-h-[120px] field-sizing-content"
            :placeholder="
              hasEnabledModels
                ? t('ai.inputPlaceholder')
                : t('ai.noModelsPlaceholder')
            "
            rows="1"
            :disabled="isLoading || !hasEnabledModels"
            @keydown="onComposerKeydown"
          />
          <InputGroupAddon align="block-end" class="justify-between">
            <Select
              v-if="enabledModels.length"
              :model-value="effectiveModelId"
              @update:model-value="
                (v) => store.commit('aiChat/setSelectedModel', v || null)
              "
            >
              <SelectTrigger
                class="h-auto w-auto border-0 bg-transparent p-0 shadow-none focus:ring-0 gap-1 text-[10px] font-mono text-muted-foreground/50 hover:text-muted-foreground [&>svg]:size-2.5 [&>svg]:opacity-50"
              >
                <SelectValue
                  :placeholder="lastModelName ?? t('ai.modelPlaceholder')"
                />
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
              class="max-w-[200px] truncate font-mono text-[10px] leading-none text-muted-foreground/60 select-none"
              :title="t('ai.noModelsPlaceholder')"
            >
              {{ t("ai.noEnabledModels") }}
            </span>
            <InputGroupButton
              v-if="isLoading"
              variant="secondary"
              size="icon-sm"
              class="rounded-full"
              :title="t('ai.stopGeneration')"
              :aria-label="t('ai.stopGeneration')"
              @click="stopGeneration"
            >
              <Square class="size-3" />
            </InputGroupButton>
            <InputGroupButton
              v-else
              size="icon-sm"
              class="rounded-full"
              :variant="inputText.trim() ? 'default' : 'secondary'"
              :disabled="!inputText.trim() || !hasEnabledModels"
              :title="
                hasEnabledModels ? t('ai.send') : t('ai.noModelsPlaceholder')
              "
              :aria-label="t('ai.send')"
              @click="submit"
            >
              <ArrowUp class="size-4" />
            </InputGroupButton>
          </InputGroupAddon>
        </InputGroup>
      </SheetFooter>
    </SheetContent>
  </Sheet>

  <AlertDialog
    :open="confirmDeleteDialog.open"
    @update:open="
      (v) => {
        if (!v) confirmDeleteDialog.open = false;
      }
    "
  >
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{
          t("ai.deleteConversationTitle")
        }}</AlertDialogTitle>
        <AlertDialogDescription>{{
          t("ai.deleteConversationDescription")
        }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel @click="confirmDeleteDialog.open = false">{{
          t("common.cancel")
        }}</AlertDialogCancel>
        <AlertDialogAction
          variant="destructive"
          @click="confirmDeleteConversation"
        >
          {{ t("common.delete") }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>

<style scoped>
.ai-dot {
  display: inline-block;
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: currentColor;
  opacity: 0.4;
  animation: ai-pulse 1s var(--ease-out-quart) infinite;
}
@keyframes ai-pulse {
  0%,
  100% {
    opacity: 0.35;
  }
  50% {
    opacity: 1;
  }
}

.ai-message :deep(h1),
.ai-message :deep(h2),
.ai-message :deep(h3),
.ai-message :deep(h4) {
  font-weight: 600;
  margin-top: 0.6em;
  margin-bottom: 0.25em;
  line-height: 1.3;
}
.ai-message :deep(h1) {
  font-size: 1.05em;
}
.ai-message :deep(h2) {
  font-size: 1em;
}
.ai-message :deep(h3) {
  font-size: 0.95em;
}
.ai-message :deep(p) {
  margin: 0.3em 0;
}
.ai-message :deep(p:first-child) {
  margin-top: 0;
}
.ai-message :deep(p:last-child) {
  margin-bottom: 0;
}
.ai-message :deep(ul),
.ai-message :deep(ol) {
  margin: 0.3em 0;
  padding-left: 1.4em;
}
.ai-message :deep(li) {
  margin: 0.1em 0;
}
.ai-message :deep(strong) {
  font-weight: 600;
}
.ai-message :deep(em) {
  font-style: italic;
}
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
.ai-message :deep(th) {
  font-weight: 600;
  background: hsl(var(--background));
}
.ai-message :deep(hr) {
  border: none;
  border-top: 1px solid hsl(var(--border));
  margin: 0.5em 0;
}
.ai-message :deep(blockquote) {
  border-left: 3px solid hsl(var(--border));
  margin: 0.3em 0;
  padding-left: 0.7em;
  color: hsl(var(--muted-foreground));
}
</style>
