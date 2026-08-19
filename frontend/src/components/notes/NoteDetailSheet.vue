<script setup>
/**
 * Bar sheet to view/edit a product note (store Notes list or product Notes tab).
 * Autosaves text/importance; bar chrome per DESIGN/MEMORY.
 */
import { computed, nextTick, ref, watch } from "vue";
import { useStore } from "vuex";
import { ExternalLink, Trash2 } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetHeaderClose,
  SheetTitle,
} from "@/components/ui/sheet";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { confirm, isConfirmOverlayActive } from "@/composables/useConfirm";
import { t } from "@/i18n";
import { notePreviewTitle } from "@/lib/notesUi";

const props = defineProps({
  open: { type: Boolean, default: false },
  note: { type: Object, default: null },
  /** Store Notes list shows product name + a footer link. Product tab hides both. */
  showProductLink: { type: Boolean, default: true },
});

const emit = defineEmits(["update:open", "updated", "deleted"]);

const store = useStore();
const { formatDateTime: formatStoreDateTime, loadMemberships } =
  useStoreDateTime();
loadMemberships();

const draftText = ref("");
const draftImportance = ref("Medium");
const isSaving = ref(false);
const isDeleting = ref(false);
const saveTimer = ref(null);
const skipNextWatch = ref(false);

const importanceOptions = computed(() => [
  { value: "Low", label: t("components.note.importanceLow") },
  { value: "Medium", label: t("components.note.importanceMedium") },
  { value: "High", label: t("components.note.importanceHigh") },
  { value: "Critical", label: t("components.note.importanceCritical") },
  { value: "Emergency", label: t("components.note.importanceEmergency") },
]);

const title = computed(() => {
  if (props.showProductLink) {
    return (
      props.note?.productName?.trim() || t("pages.notesPage.sheetTitleFallback")
    );
  }
  return notePreviewTitle(
    { text: draftText.value },
    t("pages.notesPage.sheetTitleFallback"),
  );
});

const metaLine = computed(() => {
  if (!props.note) return "";
  const parts = [];
  if (props.showProductLink && props.note.productSku) {
    parts.push(props.note.productSku);
  }
  if (props.showProductLink || props.note.authorUserName) {
    parts.push(props.note.authorUserName || t("pages.notesPage.authorUnknown"));
  }
  const created = formatStoreDateTime(props.note.createdAt);
  if (created && created !== "-") {
    parts.push(t("pages.notesPage.sheetCreated", { date: created }));
  }
  const updated = formatStoreDateTime(props.note.updatedAt);
  if (
    updated &&
    updated !== "-" &&
    props.note.updatedAt &&
    props.note.createdAt &&
    props.note.updatedAt !== props.note.createdAt
  ) {
    parts.push(t("pages.notesPage.sheetUpdated", { date: updated }));
  }
  return parts.join(" · ");
});

const productTo = computed(() => {
  if (!props.note?.productId) return null;
  return {
    path: `/products/${props.note.productId}/edit`,
    query: { tab: "notes" },
  };
});

function syncFromNote(note) {
  if (!note) {
    draftText.value = "";
    draftImportance.value = "Medium";
    return;
  }
  skipNextWatch.value = true;
  draftText.value = note.text ?? "";
  draftImportance.value = note.importance || "Medium";
}

watch(
  () => [props.open, props.note?.id],
  () => {
    if (props.open && props.note) {
      syncFromNote(props.note);
      nextTick(() => document.getElementById("note-sheet-text")?.focus());
    }
    if (!props.open && saveTimer.value) {
      clearTimeout(saveTimer.value);
      saveTimer.value = null;
    }
  },
  { immediate: true },
);

watch([draftText, draftImportance], () => {
  if (!props.open || !props.note) return;
  if (skipNextWatch.value) {
    skipNextWatch.value = false;
    return;
  }
  if (saveTimer.value) clearTimeout(saveTimer.value);
  saveTimer.value = setTimeout(() => {
    saveTimer.value = null;
    save();
  }, 1000);
});

async function save() {
  if (!props.note?.id || !props.note?.productId) return;
  const text = draftText.value;
  const importance = draftImportance.value;
  if (
    text === (props.note.text ?? "") &&
    importance === (props.note.importance || "Medium")
  ) {
    return;
  }

  isSaving.value = true;
  try {
    await store.dispatch("patchNote", {
      productId: props.note.productId,
      id: props.note.id,
      input: { text, importance },
      onSuccess: () => {
        emit("updated", {
          id: props.note.id,
          text,
          importance,
        });
      },
      onError: (error) => {
        displaySonnerError(error);
      },
    });
  } finally {
    isSaving.value = false;
  }
}

async function onDelete() {
  if (!props.note?.id || !props.note?.productId) return;
  const ok = await confirm({
    title: t("components.note.deleteConfirmTitle"),
    description: t("components.note.deleteConfirmDescription"),
    confirmLabel: t("common.delete"),
  });
  if (!ok) return;

  isDeleting.value = true;
  try {
    await store.dispatch("deleteNote", {
      productId: props.note.productId,
      id: props.note.id,
      onSuccess: () => {
        displaySonnerSuccess(t("components.note.deleteSuccess"));
        emit("deleted", props.note.id);
        emit("update:open", false);
      },
      onError: (error) => {
        displaySonnerError(error);
      },
    });
  } finally {
    isDeleting.value = false;
  }
}

function onOpenChange(value) {
  if (!value && isConfirmOverlayActive()) {
    emit("update:open", true);
    return;
  }
  if (!value && saveTimer.value) {
    clearTimeout(saveTimer.value);
    saveTimer.value = null;
    save();
  }
  emit("update:open", value);
}
</script>

<template>
  <Sheet :open="open" @update:open="onOpenChange">
    <SheetContent
      side="right"
      :show-close="false"
      class="flex w-full flex-col gap-0 overflow-hidden p-0 sm:max-w-lg"
    >
      <SheetHeader variant="bar">
        <div class="flex min-h-8 items-center gap-2">
          <SheetTitle class="min-w-0 flex-1 truncate">
            {{ title }}
          </SheetTitle>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
            :disabled="isDeleting || !note"
            :title="t('pages.notesPage.deleteNote')"
            :aria-label="t('pages.notesPage.deleteNote')"
            @click="onDelete"
          >
            <Spinner v-if="isDeleting" size="sm" />
            <Trash2 v-else class="size-4" />
          </Button>
          <SheetHeaderClose />
        </div>
        <SheetDescription v-if="metaLine">
          {{ metaLine }}
        </SheetDescription>
      </SheetHeader>

      <div
        v-if="note"
        class="flex min-h-0 flex-1 flex-col overflow-y-auto px-6 py-5"
      >
        <FieldGroup class="flex min-h-0 flex-1 flex-col gap-5">
          <SettingsFormField
            :label="t('pages.notesPage.columnImportance')"
            label-for="note-sheet-importance"
          >
            <Select v-model="draftImportance">
              <SelectTrigger id="note-sheet-importance" class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="opt in importanceOptions"
                  :key="opt.value"
                  :value="opt.value"
                >
                  {{ opt.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>

          <SettingsFormField
            class="flex min-h-0 flex-1 flex-col"
            :label="t('pages.notesPage.columnNote')"
            label-for="note-sheet-text"
          >
            <Textarea
              id="note-sheet-text"
              v-model="draftText"
              :maxlength="10000"
              class="min-h-48 flex-1 resize-none [field-sizing:fixed]"
              :placeholder="t('components.note.textPlaceholder')"
            />
          </SettingsFormField>
        </FieldGroup>

        <p
          v-if="isSaving"
          class="mt-3 flex items-center gap-2 px-1 text-xs text-muted-foreground"
        >
          <Spinner size="xs" class="text-muted-foreground" />
          {{ t("pages.notesPage.sheetSaving") }}
        </p>
      </div>

      <SheetFooter v-if="showProductLink && productTo" variant="bar">
        <Button as-child variant="outline">
          <RouterLink :to="productTo">
            <ExternalLink class="size-4" />
            {{ t("pages.notesPage.openProduct") }}
          </RouterLink>
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
