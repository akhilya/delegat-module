<template>
  <Dialog v-model:open="isOpen">
    <DialogContent
      size="3xl"
      class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader variant="bar" class="gap-3">
        <div class="space-y-1">
          <DialogTitle>
            {{ t("products.importExcelDialog.title") }}
          </DialogTitle>
          <DialogDescription>
            <template v-if="step === 'upload' || step === 'processing'">
              {{ t("products.importExcelDialog.stepUploadDescription") }}
            </template>
            <template v-else-if="step === 'preview'">
              {{ t("products.importExcelDialog.stepPreviewDescription") }}
            </template>
            <template v-else-if="step === 'applying'">
              {{ t("products.importExcelDialog.stepApplyingDescription") }}
            </template>
            <template v-else>
              {{ t("products.importExcelDialog.stepResultDescription") }}
            </template>
          </DialogDescription>
        </div>

        <ol class="flex items-center gap-1 sm:gap-2" aria-label="Import steps">
          <li
            v-for="(s, index) in steps"
            :key="s.id"
            class="flex min-w-0 flex-1 items-center gap-1.5 sm:gap-2"
          >
            <div
              class="flex size-6 shrink-0 items-center justify-center rounded-md text-[11px] font-medium tabular-nums transition-colors"
              :class="stepTone(s.id)"
            >
              <Check v-if="isStepDone(s.id)" class="size-3.5" />
              <span v-else>{{ index + 1 }}</span>
            </div>
            <span
              class="hidden truncate text-xs sm:inline"
              :class="
                isStepActive(s.id)
                  ? 'font-medium text-foreground'
                  : 'text-muted-foreground'
              "
            >
              {{ s.label }}
            </span>
            <div
              v-if="index < steps.length - 1"
              class="mx-0.5 hidden h-px flex-1 bg-border sm:block"
              aria-hidden="true"
            />
          </li>
        </ol>
      </DialogHeader>

      <div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <!-- Upload -->
        <div v-if="step === 'upload'" class="space-y-3">
          <Button
            variant="ghost"
            type="button"
            class="group h-auto min-h-0 w-full flex-col rounded-lg border border-dashed bg-transparent px-6 py-12 text-center font-normal whitespace-normal hover:bg-muted/30 active:scale-100"
            :class="
              isDragging
                ? 'border-primary bg-primary/5'
                : 'border-muted-foreground/30 hover:border-primary/50 hover:bg-muted/30'
            "
            @click="triggerFileInput"
            @dragover.prevent="isDragging = true"
            @dragleave.prevent="isDragging = false"
            @drop.prevent="handleDrop"
          >
            <div
              class="mb-3 flex size-11 items-center justify-center rounded-md border bg-background text-muted-foreground transition-colors group-hover:border-primary/40 group-hover:text-foreground"
            >
              <Upload class="size-5" />
            </div>
            <p class="text-sm font-medium">
              {{ t("products.importExcelDialog.dropzoneTitle") }}
            </p>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ t("products.importExcelDialog.dropzoneHint") }}
            </p>
            <span
              class="mt-4 inline-flex h-8 items-center rounded-md border bg-background px-3 text-xs font-medium"
            >
              {{ t("products.importExcelDialog.browseFile") }}
            </span>
          </Button>
          <input
            ref="fileInput"
            type="file"
            accept=".xlsx"
            class="hidden"
            @change="handleFileSelect"
          />
          <p
            v-if="uploadError"
            class="rounded-md border border-destructive/40 bg-destructive/5 px-3 py-2 text-sm text-destructive"
            role="alert"
          >
            {{ uploadError }}
          </p>
        </div>

        <!-- Processing preview -->
        <div
          v-else-if="step === 'processing'"
          class="flex flex-col items-center justify-center gap-3 py-16"
        >
          <Spinner size="2xl" class="text-muted-foreground" />
          <p class="text-sm text-muted-foreground">
            {{ t("products.importExcelDialog.processingFile") }}
          </p>
        </div>

        <!-- Preview -->
        <div v-else-if="step === 'preview'" class="space-y-4">
          <div class="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <div class="rounded-lg border bg-muted/30 px-3 py-2.5">
              <div class="text-[11px] text-muted-foreground">
                {{ t("products.importExcelDialog.totalRows") }}
              </div>
              <div class="mt-0.5 text-lg font-semibold tabular-nums">
                {{ changeset.summary.totalRows ?? 0 }}
              </div>
            </div>
            <div
              class="rounded-lg border border-emerald-500/30 bg-emerald-500/5 px-3 py-2.5"
            >
              <div class="text-[11px] text-muted-foreground">
                {{ t("products.importExcelDialog.changedCount") }}
              </div>
              <div
                class="mt-0.5 text-lg font-semibold tabular-nums text-emerald-700 dark:text-emerald-400"
              >
                {{ changeset.summary.changedProducts ?? 0 }}
              </div>
            </div>
            <div class="rounded-lg border bg-muted/30 px-3 py-2.5">
              <div class="text-[11px] text-muted-foreground">
                {{ t("products.importExcelDialog.unchangedCount") }}
              </div>
              <div class="mt-0.5 text-lg font-semibold tabular-nums">
                {{ changeset.summary.unchangedProducts ?? 0 }}
              </div>
            </div>
            <div
              class="rounded-lg border px-3 py-2.5"
              :class="
                (changeset.summary.errorCount ?? 0) > 0
                  ? 'border-destructive/40 bg-destructive/5'
                  : 'bg-muted/30'
              "
            >
              <div class="text-[11px] text-muted-foreground">
                {{ t("products.importExcelDialog.errorCount") }}
              </div>
              <div
                class="mt-0.5 text-lg font-semibold tabular-nums"
                :class="
                  (changeset.summary.errorCount ?? 0) > 0
                    ? 'text-destructive'
                    : ''
                "
              >
                {{ changeset.summary.errorCount ?? 0 }}
              </div>
            </div>
          </div>

          <div
            v-if="changeset.errors?.length"
            class="space-y-2 rounded-lg border border-destructive/40 bg-destructive/5 p-3"
          >
            <p class="text-sm font-medium text-destructive">
              {{ t("products.importExcelDialog.errorsTitle") }}
            </p>
            <ul
              class="max-h-28 space-y-1.5 overflow-y-auto text-sm text-destructive/90"
            >
              <li v-for="(err, i) in changeset.errors" :key="i">
                {{ t("products.importExcelDialog.rowLabel", { row: err.row })
                }}<template v-if="err.productId">
                  {{
                    t("products.importExcelDialog.productIdSuffix", {
                      id: err.productId,
                    })
                  }}
                </template>
                : {{ err.field }} - {{ err.message }}
              </li>
            </ul>
          </div>

          <FieldChangesTable :items="changesetItems" />
        </div>

        <!-- Applying -->
        <div
          v-else-if="step === 'applying'"
          class="flex flex-col items-center justify-center gap-3 py-16"
        >
          <Spinner size="2xl" class="text-muted-foreground" />
          <p class="text-sm text-muted-foreground">
            {{ t("products.importExcelDialog.stepApplyingDescription") }}
          </p>
        </div>

        <!-- Result -->
        <div
          v-else-if="step === 'result'"
          class="flex flex-col items-center justify-center gap-3 py-12 text-center"
        >
          <template v-if="applyResult">
            <div
              class="flex size-12 items-center justify-center rounded-md border border-emerald-500/40 bg-emerald-500/10"
            >
              <CheckCircle2
                class="size-6 text-emerald-600 dark:text-emerald-400"
              />
            </div>
            <p class="text-base font-medium">
              {{
                t("products.importExcelDialog.updatedCount", {
                  count: applyResult.appliedCount,
                })
              }}
            </p>
            <ul
              v-if="applyResult.errors?.length"
              class="mt-1 max-w-md space-y-1 text-sm text-destructive"
            >
              <li v-for="(err, i) in applyResult.errors" :key="i">
                {{ err.message }}
              </li>
            </ul>
          </template>
          <template v-else-if="applyError">
            <div
              class="flex size-12 items-center justify-center rounded-md border border-destructive/40 bg-destructive/5"
            >
              <AlertCircle class="size-6 text-destructive" />
            </div>
            <p class="max-w-md text-sm text-destructive">{{ applyError }}</p>
          </template>
        </div>
      </div>

      <DialogFooter variant="bar" class="sm:justify-between">
        <div class="flex flex-wrap gap-2">
          <DialogClose v-if="step === 'upload' || step === 'result'" as-child>
            <Button variant="outline">
              {{ step === "result" ? t("common.close") : t("common.cancel") }}
            </Button>
          </DialogClose>
          <Button
            v-if="step === 'preview'"
            variant="outline"
            @click="resetToUpload"
          >
            {{ t("common.back") }}
          </Button>
        </div>
        <Button
          v-if="step === 'preview' && changeset.changes?.length > 0"
          @click="handleApply"
        >
          {{ t("products.importExcelDialog.applyChanges") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup>
import { Spinner } from "@/components/ui/spinner";
import { ref, computed, watch } from "vue";
import { useStore } from "vuex";
import { Upload, CheckCircle2, AlertCircle, Check } from "lucide-vue-next";
import { t } from "@/i18n";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import FieldChangesTable from "@/components/products/FieldChangesTable.vue";
import { toast } from "vue-sonner";
import { resolveApiErrorMessage } from "@/store/sonnerHelper";

// Mirrors ExcelProductService.MaxImportUploadBytes; larger requests are cut off before the API can return its error code.
const MAX_IMPORT_UPLOAD_BYTES = 20 * 1024 * 1024;

const props = defineProps({
  modelValue: Boolean,
});
const emit = defineEmits(["update:modelValue", "saved"]);
const store = useStore();

const isOpen = computed({
  get: () => props.modelValue,
  set: (value) => emit("update:modelValue", value),
});

/** @type {import('vue').Ref<'upload'|'processing'|'preview'|'applying'|'result'>} */
const step = ref("upload");
const isDragging = ref(false);
const uploadError = ref("");
const selectedFile = ref(null);
const changeset = ref({ changes: [], errors: [], summary: {} });
const applyResult = ref(null);
const applyError = ref(null);
const fileInput = ref(null);

const steps = computed(() => [
  { id: "upload", label: t("products.importExcelDialog.stepUpload") },
  { id: "preview", label: t("products.importExcelDialog.stepPreview") },
  { id: "result", label: t("products.importExcelDialog.stepDone") },
]);

const stepOrder = ["upload", "preview", "result"];

function normalizedStepId(current) {
  if (current === "processing") return "upload";
  if (current === "applying") return "preview";
  return current;
}

function isStepActive(id) {
  return normalizedStepId(step.value) === id;
}

function isStepDone(id) {
  const current = normalizedStepId(step.value);
  return stepOrder.indexOf(id) < stepOrder.indexOf(current);
}

function stepTone(id) {
  if (isStepDone(id)) {
    return "bg-primary text-primary-foreground";
  }
  if (isStepActive(id)) {
    return "border border-primary bg-primary/10 text-foreground";
  }
  return "border border-border bg-muted/40 text-muted-foreground";
}

const changesetItems = computed(() => {
  if (!changeset.value.changes || changeset.value.changes.length === 0)
    return [];
  return changeset.value.changes.map((product) => ({
    label: `${product.productSku} - ${product.productName}`,
    fields: product.changes.map((change, j) => ({
      key: `${product.productId}-${j}`,
      label: change.fieldName,
      oldValue: change.oldValue ?? null,
      newValue: change.newValue ?? null,
    })),
  }));
});

function triggerFileInput() {
  fileInput.value?.click();
}

function handleFileSelect(event) {
  const file = event.target.files?.[0];
  if (file) processFile(file);
}

function handleDrop(event) {
  isDragging.value = false;
  const file = event.dataTransfer?.files?.[0];
  if (file) processFile(file);
}

async function processFile(file) {
  if (!file.name.endsWith(".xlsx")) {
    uploadError.value = t("products.importExcelDialog.unsupportedFormat");
    return;
  }
  if (file.size > MAX_IMPORT_UPLOAD_BYTES) {
    uploadError.value = t("errors.excel_import_too_large");
    return;
  }
  uploadError.value = "";
  selectedFile.value = file;
  step.value = "processing";

  await store.dispatch("importProductsPreview", {
    file,
    onSuccess: (data) => {
      changeset.value = data;
      step.value = "preview";
    },
    onError: (error) => {
      uploadError.value = resolveApiErrorMessage(
        error.response?.data,
        "products.importExcelDialog.processError",
      );
      step.value = "upload";
    },
  });
}

async function handleApply() {
  if (!selectedFile.value) return;
  step.value = "applying";

  await store.dispatch("importProductsApply", {
    file: selectedFile.value,
    onSuccess: (data) => {
      applyResult.value = data;
      applyError.value = null;
      step.value = "result";
      if (data.appliedCount > 0) {
        toast.success(
          t("products.importExcelDialog.updatedCount", {
            count: data.appliedCount,
          }),
        );
        emit("saved");
      }
    },
    onError: (error) => {
      applyError.value = resolveApiErrorMessage(
        error.response?.data,
        "products.importExcelDialog.applyError",
      );
      applyResult.value = null;
      step.value = "result";
    },
  });
}

function resetToUpload() {
  step.value = "upload";
  selectedFile.value = null;
  changeset.value = { changes: [], errors: [], summary: {} };
  uploadError.value = "";
  if (fileInput.value) fileInput.value.value = "";
}

watch(isOpen, (val) => {
  if (!val) {
    step.value = "upload";
    selectedFile.value = null;
    changeset.value = { changes: [], errors: [], summary: {} };
    applyResult.value = null;
    applyError.value = null;
    uploadError.value = "";
    isDragging.value = false;
  }
});
</script>
