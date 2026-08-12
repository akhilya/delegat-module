<script setup>
/**
 * Right-side sheet to view/edit a store-wide note from the Notes page list.
 * Autosaves text/importance like the product Notes tab; bar chrome per DESIGN/MEMORY.
 */
import { computed, ref, watch } from "vue";
import { useStore } from "vuex";
import { ExternalLink, Trash2 } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import Textarea from "@/components/ui/textarea/Textarea.vue";
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
  SheetTitle,
} from "@/components/ui/sheet";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { t } from "@/i18n";

const props = defineProps({
  open: { type: Boolean, default: false },
  note: { type: Object, default: null },
});

const emit = defineEmits(["update:open", "updated", "deleted"]);

const store = useStore();
const { formatDateTime: formatStoreDateTime, loadMemberships } = useStoreDateTime();
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

const title = computed(
  () =>
    props.note?.productName?.trim() ||
    t("pages.notesPage.sheetTitleFallback"),
);

const metaLine = computed(() => {
  if (!props.note) return "";
  const parts = [];
  if (props.note.productSku) parts.push(props.note.productSku);
  parts.push(
    props.note.authorUserName || t("pages.notesPage.authorUnknown"),
  );
  const created = formatStoreDateTime(props.note.createdAt);
  if (created) {
    parts.push(
      t("pages.notesPage.sheetCreated", { date: created }),
    );
  }
  const updated = formatStoreDateTime(props.note.updatedAt);
  if (
    updated &&
    props.note.updatedAt &&
    props.note.createdAt &&
    props.note.updatedAt !== props.note.createdAt
  ) {
    parts.push(
      t("pages.notesPage.sheetUpdated", { date: updated }),
    );
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
    if (props.open && props.note) syncFromNote(props.note);
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

async function deleteNote() {
  if (!props.note?.id || !props.note?.productId) return;
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
      class="flex w-full flex-col gap-0 overflow-hidden p-0 sm:max-w-lg"
    >
      <SheetHeader class="shrink-0 border-b border-border px-6 py-4 pr-12 text-left">
        <SheetTitle class="truncate">
          {{ title }}
        </SheetTitle>
        <SheetDescription v-if="metaLine" class="text-balance">
          {{ metaLine }}
        </SheetDescription>
      </SheetHeader>

      <div
        v-if="note"
        class="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-6 py-4"
      >
        <FieldGroup class="flex min-h-0 flex-1 flex-col gap-4">
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
              class="min-h-48 flex-1 resize-y"
              :placeholder="t('components.note.textPlaceholder')"
            />
          </SettingsFormField>
        </FieldGroup>

        <div
          v-if="isSaving"
          class="flex items-center gap-2 text-xs text-muted-foreground"
        >
          <Spinner size="xs" class="text-muted-foreground" />
          {{ t("pages.notesPage.sheetSaving") }}
        </div>
      </div>

      <SheetFooter
        class="mt-0 shrink-0 flex-row flex-wrap items-center justify-between gap-2 border-t border-border px-6 py-4 sm:space-x-0"
      >
        <Button
          v-if="productTo"
          as-child
          variant="outline"
          size="sm"
        >
          <RouterLink :to="productTo">
            <ExternalLink class="size-4" />
            {{ t("pages.notesPage.openProduct") }}
          </RouterLink>
        </Button>
        <span v-else />

        <AlertDialog>
          <AlertDialogTrigger as-child>
            <Button
              variant="ghost"
              size="sm"
              class="text-destructive hover:bg-destructive/10 hover:text-destructive"
              :disabled="isDeleting"
            >
              <Trash2 class="size-4" />
              {{ t("pages.notesPage.deleteNote") }}
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>
                {{ t("components.note.deleteConfirmTitle") }}
              </AlertDialogTitle>
              <AlertDialogDescription>
                {{ t("components.note.deleteConfirmDescription") }}
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>{{ t("common.cancel") }}</AlertDialogCancel>
              <AlertDialogAction
                variant="destructive"
                :disabled="isDeleting"
                @click="deleteNote"
              >
                {{ t("common.delete") }}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
