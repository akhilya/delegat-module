<script setup>
import { ref, computed, nextTick, watch, onMounted } from "vue";
import { useStore } from "vuex";
import { unrefElement } from "@vueuse/core";
import {
  ArrowDown,
  ArrowUp,
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
  Files,
  AlertTriangle,
} from "lucide-vue-next";
import AiPlanCard from "./AiPlanCard.vue";
import AiChatProcess from "./AiChatProcess.vue";
import AiChatActions from "./AiChatActions.vue";
import AiChatForm from "./AiChatForm.vue";
import OperationContextBubble from "./OperationContextBubble.vue";
import AiChatAttachmentList from "./AiChatAttachmentList.vue";
import AiChatEmpty from "./AiChatEmpty.vue";
import { emptyPromptScope, pickEmptyPromptKeys } from "@/lib/ai/emptyPrompts";
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
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
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
import { useCompanionSheets } from "@/composables/useCompanionSheets";
import { addCalendarDays, calendarDayKey } from "@/utils/dateTime";
import { useI18n } from "vue-i18n";
import {
  filesFromDirectoryInput,
  mergeAttachedFiles,
} from "@/lib/ai/attachments";
import { hasPermission, PERMS } from "@/lib/permissions";
import { getCachedMe } from "@/lib/meCache";
import {
  executionView,
  splitPlanExecution,
  timelineFromLegacy,
} from "@/lib/ai/timeline";
import { isHiddenUserMessage } from "@/lib/ai/persist";
import { renderAiMarkdown } from "@/lib/ai/markdown";
import { planHasContent } from "@/lib/ai/planSteps";
import { MarkdownProse } from "@/components/ui/markdown-prose";
import "./aiMessage.css";

const { t, te, locale } = useI18n();
const store = useStore();
const { edgeOffset } = useCompanionSheets();
const aiEdgeOffset = edgeOffset("ai");
const {
  timezone: storeTimezone,
  relativeDayLabel,
  todayKey,
  loadMemberships,
} = useStoreDateTime();
loadMemberships();
const messagesEl = ref(null);
const inputEl = ref(null);
const filesInputEl = ref(null);
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
const attachedFiles = computed(() => store.state.aiChat.attachedFiles || []);
const attachedCount = computed(() => attachedFiles.value.length);
const canAttach = computed(() =>
  hasPermission(getCachedMe(), PERMS.FilesManage),
);
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

function planErrorText(message) {
  const error = message?.planConfirmationError || message?.requestError;
  const reasonKey = error?.reason
    ? `ai.planConfirmationReasons.${error.reason}`
    : "";
  if (
    error?.code === "plan_not_pending_or_mismatched" &&
    reasonKey &&
    te(reasonKey)
  )
    return t(reasonKey);
  const key = error?.code ? `errors.${error.code}` : "";
  if (key && te(key)) return t(key);
  return t(
    error?.stage === "execution"
      ? "ai.planExecutionStatusUnknown"
      : error?.stage === "confirmation"
        ? "ai.planConfirmationStatusUnknown"
        : "ai.requestStatusUnknown",
  );
}

function planErrorTitle(message) {
  const stage =
    message?.planConfirmationError?.stage || message?.requestError?.stage;
  if (stage === "execution") return t("ai.planExecutionStatusUnknownTitle");
  if (stage === "confirmation") return t("ai.planConfirmationFailedTitle");
  return t("ai.requestFailedTitle");
}

function planErrorNextStep(message) {
  const error = message?.planConfirmationError || message?.requestError;
  const stage = error?.stage;
  if (stage === "execution") return t("ai.planExecutionStatusUnknownNextStep");
  if (stage === "confirmation" && error?.reason === "already_processed")
    return t("ai.planConfirmationAlreadyProcessedNextStep");
  if (stage === "confirmation") return t("ai.planConfirmationNextStep");
  return t("ai.requestNextStep");
}

const allModels = computed(() => store.state.aiModels?.models ?? []);
const pickerModels = computed(() => store.state.aiModels?.pickerModels ?? []);
const enabledModels = computed(() => {
  const src = pickerModels.value.length ? pickerModels.value : allModels.value;
  return src.filter((m) => m.isEnabled !== false && (m.id ?? m.Id) != null);
});
const hostedAiStatus = computed(() => store.state.aiModels?.hostedAiStatus);
const showHostedAiUsage = computed(
  () => hostedAiStatus.value?.configured && hostedAiStatus.value?.entitled,
);
const hostedAiUsageText = computed(() => {
  const status = hostedAiStatus.value;
  if (!status?.configured || !status?.entitled) return "";
  if (status.usageUnavailable) return t("ai.hostedUsageUnavailable");
  const used =
    (Number(status.usedTokens) || 0) + (Number(status.reservedTokens) || 0);
  return t("ai.hostedUsage", {
    used: formatTokenCount(used),
    quota: formatTokenCount(status.quotaTokens),
  });
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

watch([enabledModels, selectedModelId], () => {
  if (
    selectedModelId.value &&
    !enabledModels.value.some(
      (model) => String(model.id ?? model.Id) === String(selectedModelId.value),
    )
  ) {
    store.commit("aiChat/setSelectedModel", null);
  }
});

const emptyPromptKeys = computed(() =>
  pickEmptyPromptKeys({
    page: store.state.aiChat.pageContext?.page ?? null,
    seed: `${sessionId.value}:${emptyPromptScope(store.state.aiChat.pageContext)}`,
  }),
);
const examplePrompts = computed(() =>
  emptyPromptKeys.value.map((key) => t(key)),
);

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
function confirmPlan(proposalId) {
  store.dispatch("aiChat/confirmPlan", proposalId);
}
function cancelPlan(proposalId) {
  store.dispatch("aiChat/cancelPlan", proposalId);
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
function userClipboard(msg) {
  const names = (msg.attachments || [])
    .map((a) => a.fileName || a.relativePath)
    .filter(Boolean);
  return [msg.content, names.join("\n")].filter(Boolean).join("\n");
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
    const res = await http.get("/ai/conversations", { cache: false });
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
  if (
    (!text && !attachedCount.value) ||
    isLoading.value ||
    !hasEnabledModels.value
  )
    return;
  inputText.value = "";
  const sendPromise = store.dispatch("aiChat/sendMessage", text);
  await nextTick();
  const el = inputDom();
  if (el?.style) el.style.height = "auto";
  await sendPromise;
}

function onChatAsk(prompt) {
  if (isLoading.value || !hasEnabledModels.value || !prompt) return;
  store.dispatch("aiChat/sendMessage", prompt);
}

function onFormSubmit(msg, payload) {
  const prompt = payload?.prompt;
  if (isLoading.value || !hasEnabledModels.value || !prompt) return;
  store.commit("aiChat/setFormReply", {
    messageId: msg.id,
    formId: payload.formId,
    answers: payload.answers || {},
  });
  store.dispatch("aiChat/sendMessage", {
    content: prompt,
    formReply: {
      formId: payload.formId,
      answers: payload.answers || {},
    },
  });
}

function pickFiles() {
  filesInputEl.value?.click();
}

function onFilesPicked(e) {
  const extra = filesFromDirectoryInput(e.target?.files);
  store.commit(
    "aiChat/setAttachedFiles",
    mergeAttachedFiles(attachedFiles.value, extra),
  );
  e.target.value = "";
}

function clearAttachments() {
  store.commit("aiChat/setAttachedFiles", []);
}

function submitExample(example) {
  inputText.value = example;
  submit();
}

function onComposerKeydown(e) {
  if (e.key !== "Enter" || e.shiftKey || e.isComposing) return;
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
  store.dispatch("aiModels/getHostedAiStatus", { onError: () => {} });
});

watch(isOpen, (open) => {
  if (open) store.dispatch("aiModels/getHostedAiStatus", { onError: () => {} });
});

watch(isLoading, (loading, wasLoading) => {
  if (wasLoading && !loading)
    store.dispatch("aiModels/getHostedAiStatus", { onError: () => {} });
});

function formatTokenCount(value) {
  return new Intl.NumberFormat(locale.value).format(
    Math.max(0, Number(value) || 0),
  );
}

function onPanelWidth(w) {
  store.commit("aiChat/setPanelWidth", w);
}

function messageTimeline(msg) {
  return msg.timeline?.length
    ? msg.timeline
    : timelineFromLegacy(msg.content, msg.toolCalls);
}

function hasPlanExec(msg) {
  return Number.isInteger(msg.planExecStart);
}

function planExecutionTools(msg) {
  if (!hasPlanExec(msg)) return [];
  return messageTimeline(msg)
    .slice(msg.planExecStart)
    .filter((item) => item.type === "tool" && item.tool)
    .map((item) => item.tool);
}

function turnLayout(msg) {
  const timeline = messageTimeline(msg);
  if (!hasPlanExec(msg)) {
    return {
      proposal: executionView(timeline, { done: !!msg.done }),
      execution: { process: [], reply: "", actions: [] },
    };
  }
  return splitPlanExecution(timeline, msg.planExecStart, { done: !!msg.done });
}

function proposalView(msg) {
  return turnLayout(msg).proposal;
}

function executionPart(msg) {
  return turnLayout(msg).execution;
}

function replyText(msg) {
  const layout = turnLayout(msg);
  return hasPlanExec(msg)
    ? layout.execution.reply
    : msg.plan
      ? ""
      : layout.proposal.reply;
}

function chatForms(msg) {
  const layout = turnLayout(msg);
  const forms = [
    ...(layout.proposal.forms || []),
    ...(layout.execution.forms || []),
  ];
  const seen = new Set();
  return forms.filter((form) => {
    const key = `${form.kind}|${form.adapterKey || ""}|${form.id || ""}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function chatActions(msg) {
  const layout = turnLayout(msg);
  const actions = [...layout.proposal.actions, ...layout.execution.actions];
  const seen = new Set();
  return actions.filter((action) => {
    const key = `${action.url}|${action.prompt}|${action.label}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function isLatestTurn(msg) {
  return msg.id === messages.value.at(-1)?.id;
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
      :edge-offset="aiEdgeOffset"
      :aria-label="t('ai.openAssistant')"
      @update:width="onPanelWidth"
      @escape-key-down="onSheetEscape"
      @open-auto-focus="onOpenAutoFocus"
    >
      <SheetHeader
        v-if="activeView === 'chat'"
        variant="bar"
        class="animate-in fade-in-0 duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
      >
        <div class="flex min-h-8 items-center gap-2">
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

      <SheetHeader
        v-else
        variant="bar"
        class="animate-in fade-in-0 duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
      >
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
        class="min-h-0 flex-1 overflow-y-auto px-4 py-4 animate-in fade-in-0 duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
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
                <p class="truncate leading-snug">
                  {{ c.title || t("ai.untitled") }}
                </p>
                <p class="mt-0.5 text-xs text-muted-foreground">
                  {{ c.messageCount }} {{ t("ai.messagesCountSuffix") }}
                </p>
                <template #trailing>
                  <span class="flex items-center gap-0.5">
                    <span
                      class="px-1 text-xs text-muted-foreground tabular-nums"
                    >
                      {{ formatRelativeDate(c.lastMessageAt) }}
                    </span>
                    <Button
                      variant="ghost"
                      size="icon"
                      reveal-on-row-hover
                      class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                      :title="t('ai.deleteConversationTooltip')"
                      :aria-label="t('ai.deleteConversationTooltip')"
                      @click="openConfirmDelete($event, c.sessionId)"
                    >
                      <Trash2 class="size-4" />
                    </Button>
                  </span>
                </template>
              </InsetGroupItem>
            </InsetGroup>
          </section>
        </div>
      </div>

      <div
        v-else
        class="relative min-h-0 flex-1 bg-background animate-in fade-in-0 duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
      >
        <div
          ref="messagesEl"
          class="h-full overflow-y-auto px-4 py-5"
          @scroll="onScroll"
        >
          <AiChatEmpty
            v-if="messages.length === 0"
            :title="hasEnabledModels ? t('ai.greeting') : t('ai.unavailable')"
            :description="
              hasEnabledModels
                ? t('ai.capabilitiesDescription')
                : t('ai.noModelsEnabled')
            "
            :prompts="hasEnabledModels ? examplePrompts : []"
            @pick="submitExample"
          />

          <div v-else class="ai-thread flex flex-col gap-6 pb-2">
            <template v-for="msg in messages" :key="msg.id">
              <Message
                v-if="msg.role === 'user' && !isHiddenUserMessage(msg)"
                align="end"
              >
                <MessageContent class="gap-1.5">
                  <OperationContextBubble
                    v-if="msg.contextCard"
                    :card="msg.contextCard"
                    :raw="msg.content"
                  />
                  <Bubble
                    v-else-if="
                      msg.content &&
                      !(
                        msg.attachments?.length &&
                        msg.content === t('ai.attachedFilesMessage')
                      )
                    "
                    variant="secondary"
                    align="end"
                  >
                    <BubbleContent>{{ msg.content }}</BubbleContent>
                  </Bubble>
                  <AiChatAttachmentList
                    v-if="msg.attachments?.length"
                    :files="msg.attachments"
                  />
                  <MessageFooter
                    class="opacity-0 transition-opacity duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] group-hover/message:opacity-100 group-focus-within/message:opacity-100"
                  >
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-7 text-muted-foreground"
                      :title="
                        copiedId === msg.id ? t('ai.copied') : t('common.copy')
                      "
                      :aria-label="t('common.copy')"
                      @click="copyText(msg.id, userClipboard(msg))"
                    >
                      <Check
                        v-if="copiedId === msg.id"
                        class="size-3.5 animate-in zoom-in-95 fade-in-0 duration-[var(--duration-press)] ease-[var(--ease-out-quart)]"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                  </MessageFooter>
                </MessageContent>
              </Message>

              <Message v-else-if="msg.role === 'assistant'" align="start">
                <MessageContent class="gap-3">
                  <AiChatProcess
                    :process="proposalView(msg).process"
                    :done="!!msg.done || hasPlanExec(msg)"
                    :enter="isLatestTurn(msg)"
                  />

                  <MarkdownProse
                    v-if="msg.plan && proposalView(msg).reply"
                    density="compact"
                    :html="renderAiMarkdown(proposalView(msg).reply)"
                    class="min-w-0 w-full wrap-break-word"
                    :class="isLatestTurn(msg) && 'ai-block-in'"
                  />

                  <Alert
                    v-if="msg.planConfirmationError || msg.requestError"
                    variant="destructive"
                    class="w-full"
                    :class="isLatestTurn(msg) && 'ai-block-in'"
                  >
                    <AlertTriangle class="size-4" />
                    <AlertTitle>
                      {{ planErrorTitle(msg) }}
                    </AlertTitle>
                    <AlertDescription>
                      <p>{{ planErrorText(msg) }}</p>
                      <p
                        v-if="
                          (msg.planConfirmationError || msg.requestError).status
                        "
                        class="mt-1 text-xs"
                      >
                        {{
                          t("ai.planHttpStatus", {
                            status: (
                              msg.planConfirmationError || msg.requestError
                            ).status,
                          })
                        }}
                      </p>
                      <p class="mt-1">{{ planErrorNextStep(msg) }}</p>
                    </AlertDescription>
                  </Alert>

                  <div
                    v-if="planHasContent(msg.plan)"
                    :class="isLatestTurn(msg) && 'ai-block-in'"
                  >
                    <AiPlanCard
                      :plan="msg.plan"
                      :confirmed="msg.planConfirmed"
                      :cancelled="msg.planCancelled"
                      :superseded="msg.planSuperseded"
                      :executed="msg.planExecuted"
                      :failed="msg.planFailed"
                      :confirming="msg.planConfirming"
                      :confirmation-blocked="
                        msg.planConfirmationError?.stage === 'confirmation' &&
                        !msg.planConfirmed
                      "
                      :tool-calls="planExecutionTools(msg)"
                      :ready="msg.done && !isLoading"
                      @confirm="confirmPlan"
                      @cancel="cancelPlan"
                    />
                  </div>

                  <AiChatProcess
                    v-if="hasPlanExec(msg)"
                    :process="executionPart(msg).process"
                    :done="!!msg.done"
                    :enter="isLatestTurn(msg)"
                  />

                  <MarkdownProse
                    v-if="replyText(msg)"
                    density="compact"
                    :html="renderAiMarkdown(replyText(msg))"
                    class="min-w-0 w-full wrap-break-word"
                    :class="isLatestTurn(msg) && 'ai-block-in'"
                  />

                  <AiChatActions
                    v-if="msg.done && chatActions(msg).length"
                    :actions="chatActions(msg)"
                    :class="isLatestTurn(msg) && 'ai-block-in'"
                    @ask="onChatAsk"
                  />

                  <div
                    v-if="msg.done && chatForms(msg).length"
                    class="flex w-full min-w-0 flex-col gap-2"
                    :class="isLatestTurn(msg) && 'ai-block-in'"
                  >
                    <AiChatForm
                      v-for="form in chatForms(msg)"
                      :key="form.id"
                      :form="form"
                      :submitted="!!msg.formReplies?.[String(form.id)]"
                      :answers="msg.formReplies?.[String(form.id)]?.answers"
                      @submit="onFormSubmit(msg, $event)"
                    />
                  </div>

                  <Transition name="ai-fade">
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
                  </Transition>

                  <MessageFooter
                    v-if="msg.done && (msg.content || msg.toolCalls?.length)"
                    class="gap-1 opacity-0 transition-opacity duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] group-hover/message:opacity-100 group-focus-within/message:opacity-100"
                  >
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-7 text-muted-foreground"
                      :title="
                        copiedId === msg.id ? t('ai.copied') : t('common.copy')
                      "
                      :aria-label="t('common.copy')"
                      @click="copyText(msg.id, messageClipboard(msg))"
                    >
                      <Check
                        v-if="copiedId === msg.id"
                        class="size-3.5 animate-in zoom-in-95 fade-in-0 duration-[var(--duration-press)] ease-[var(--ease-out-quart)]"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-7"
                      :class="
                        msg.feedbackRating === 1
                          ? 'text-foreground'
                          : 'text-muted-foreground'
                      "
                      :title="t('ai.goodResponse')"
                      :aria-label="t('ai.goodResponse')"
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
                          ? 'text-foreground'
                          : 'text-muted-foreground'
                      "
                      :title="t('ai.badResponse')"
                      :aria-label="t('ai.badResponse')"
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

        <Transition name="ai-fade">
          <Button
            v-if="!isAtBottom && messages.length > 0"
            variant="outline"
            size="icon"
            class="absolute bottom-3 left-1/2 z-10 size-8 -translate-x-1/2 rounded-md"
            :title="t('ai.jumpToLatest')"
            :aria-label="t('ai.jumpToLatest')"
            @click="scrollToLatest"
          >
            <ArrowDown class="size-4" />
          </Button>
        </Transition>
      </div>

      <SheetFooter
        v-if="activeView === 'chat'"
        variant="bar"
        class="flex-col items-stretch gap-2 px-4 py-3 sm:flex-col sm:items-stretch sm:justify-start animate-in fade-in-0 duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
      >
        <input
          ref="filesInputEl"
          type="file"
          class="hidden"
          multiple
          @change="onFilesPicked"
        />
        <Transition name="ai-fade">
          <AiChatAttachmentList
            v-if="attachedCount"
            :files="attachedFiles"
            clearable
            @clear="clearAttachments"
          />
        </Transition>
        <InputGroup class="w-full bg-background dark:bg-input/30">
          <InputGroupTextarea
            ref="inputEl"
            v-model="inputText"
            class="min-h-12 max-h-40 overflow-y-auto px-3 py-3 text-sm leading-6"
            :placeholder="
              hasEnabledModels
                ? t('ai.inputPlaceholder')
                : t('ai.noModelsPlaceholder')
            "
            rows="1"
            :disabled="isLoading || !hasEnabledModels"
            @keydown="onComposerKeydown"
          />
          <InputGroupAddon
            align="block-end"
            class="justify-between gap-2 px-2 pb-2 pt-0"
          >
            <div class="flex min-w-0 items-center gap-1">
              <InputGroupButton
                v-if="canAttach"
                variant="ghost"
                size="icon-sm"
                :disabled="isLoading || !hasEnabledModels"
                :title="t('ai.attach')"
                :aria-label="t('ai.attach')"
                @click="pickFiles"
              >
                <Files class="size-4" />
              </InputGroupButton>
              <Select
                v-if="enabledModels.length"
                :model-value="effectiveModelId"
                @update:model-value="
                  (v) => store.commit('aiChat/setSelectedModel', v || null)
                "
              >
                <SelectTrigger
                  size="sm"
                  class="max-w-[min(12rem,45vw)] gap-1 border-0 bg-transparent px-2 text-xs text-muted-foreground shadow-none hover:bg-muted hover:text-foreground focus-visible:border-transparent focus-visible:ring-2 [&>svg]:size-3"
                  :aria-label="t('ai.modelPlaceholder')"
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
                    {{
                      m.isPlatformHosted ? t("ai.platformModelName") : m.name
                    }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <span
                v-else
                class="max-w-[min(12rem,45vw)] truncate px-2 text-xs text-muted-foreground select-none"
                :title="t('ai.noModelsPlaceholder')"
              >
                {{ t("ai.noEnabledModels") }}
              </span>
            </div>
            <InputGroupButton
              v-if="isLoading"
              variant="secondary"
              size="icon-sm"
              :title="t('ai.stopGeneration')"
              :aria-label="t('ai.stopGeneration')"
              @click="stopGeneration"
            >
              <Square class="size-3" />
            </InputGroupButton>
            <InputGroupButton
              v-else
              size="icon-sm"
              :variant="
                inputText.trim() || attachedCount ? 'default' : 'secondary'
              "
              :disabled="
                (!inputText.trim() && !attachedCount) || !hasEnabledModels
              "
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
        <p
          v-if="showHostedAiUsage"
          class="px-1 text-[11px] leading-4 tabular-nums"
          :class="
            hostedAiStatus.usageUnavailable
              ? 'text-warning-foreground'
              : 'text-muted-foreground'
          "
          aria-live="polite"
        >
          {{ hostedAiUsageText }}
        </p>
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
</style>
