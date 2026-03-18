<template>
  <Dialog v-model:open="isOpen">
    <DialogContent class="sm:max-w-3xl max-h-[85vh] overflow-hidden flex flex-col">
      <DialogHeader>
        <DialogTitle>Импорт из Excel</DialogTitle>
        <DialogDescription>
          <template v-if="step === 'upload'">
            Загрузите ранее экспортированный .xlsx файл с изменениями
          </template>
          <template v-else-if="step === 'preview'">
            Проверьте изменения перед применением
          </template>
          <template v-else-if="step === 'applying'">
            Применение изменений...
          </template>
          <template v-else-if="step === 'result'">
            Результат импорта
          </template>
        </DialogDescription>
      </DialogHeader>

      <!-- Upload step -->
      <div v-if="step === 'upload'" class="flex-1 flex flex-col items-center justify-center py-8">
        <div
          class="border-2 border-dashed rounded-lg p-12 text-center cursor-pointer transition-colors w-full"
          :class="isDragging ? 'border-primary bg-primary/5' : 'border-muted-foreground/25 hover:border-primary/50'"
          @click="triggerFileInput"
          @dragover.prevent="isDragging = true"
          @dragleave.prevent="isDragging = false"
          @drop.prevent="handleDrop"
        >
          <Upload class="h-10 w-10 mx-auto mb-3 text-muted-foreground" />
          <p class="text-sm font-medium">Перетащите .xlsx файл сюда</p>
          <p class="text-xs text-muted-foreground mt-1">или нажмите для выбора</p>
        </div>
        <input
          ref="fileInput"
          type="file"
          accept=".xlsx"
          class="hidden"
          @change="handleFileSelect"
        />
        <p v-if="uploadError" class="text-sm text-destructive mt-3">{{ uploadError }}</p>
      </div>

      <!-- Preview step -->
      <div v-else-if="step === 'preview'" class="flex-1 overflow-y-auto space-y-4">
        <!-- Summary -->
        <div class="flex gap-3 text-sm">
          <div class="bg-muted rounded px-3 py-1.5">
            Всего строк: <span class="font-medium">{{ changeset.summary.totalRows }}</span>
          </div>
          <div class="bg-success-muted text-success-foreground rounded px-3 py-1.5">
            Изменено: <span class="font-medium">{{ changeset.summary.changedProducts }}</span>
          </div>
          <div v-if="changeset.summary.unchangedProducts > 0" class="bg-muted rounded px-3 py-1.5">
            Без изменений: <span class="font-medium">{{ changeset.summary.unchangedProducts }}</span>
          </div>
          <div v-if="changeset.summary.errorCount > 0" class="bg-error-muted text-error-foreground rounded px-3 py-1.5">
            Ошибок: <span class="font-medium">{{ changeset.summary.errorCount }}</span>
          </div>
        </div>

        <!-- Errors -->
        <div v-if="changeset.errors && changeset.errors.length > 0" class="space-y-1">
          <p class="text-sm font-medium text-destructive">Ошибки:</p>
          <div
            v-for="(err, i) in changeset.errors"
            :key="i"
            class="text-sm bg-error-muted text-error-foreground rounded px-3 py-1.5"
          >
            Строка {{ err.row }}<template v-if="err.productId"> (ID {{ err.productId }})</template>:
            {{ err.field }} &mdash; {{ err.message }}
          </div>
        </div>

        <!-- Changes table -->
        <FieldChangesTable :items="changesetItems" />
      </div>

      <!-- Applying step -->
      <div v-else-if="step === 'applying'" class="flex-1 flex flex-col items-center justify-center py-8">
        <Loader2 class="h-8 w-8 animate-spin text-primary mb-3" />
        <p class="text-sm text-muted-foreground">Применение изменений...</p>
      </div>

      <!-- Result step -->
      <div v-else-if="step === 'result'" class="flex-1 flex flex-col items-center justify-center py-8">
        <template v-if="applyResult">
          <div class="text-center space-y-2">
            <CheckCircle2 class="h-10 w-10 mx-auto text-success" />
            <p class="text-lg font-medium">
              Обновлено товаров: {{ applyResult.appliedCount }}
            </p>
            <div
              v-if="applyResult.errors && applyResult.errors.length > 0"
              class="text-sm text-destructive mt-2"
            >
              <p v-for="(err, i) in applyResult.errors" :key="i">
                {{ err.message }}
              </p>
            </div>
          </div>
        </template>
        <template v-else-if="applyError">
          <div class="text-center space-y-2">
            <AlertCircle class="h-10 w-10 mx-auto text-destructive" />
            <p class="text-sm text-destructive">{{ applyError }}</p>
          </div>
        </template>
      </div>

      <DialogFooter>
        <DialogClose v-if="step === 'upload' || step === 'result'" as-child>
          <Button variant="outline">{{ step === 'result' ? 'Закрыть' : 'Отмена' }}</Button>
        </DialogClose>
        <Button
          v-if="step === 'preview'"
          variant="outline"
          @click="resetToUpload"
        >
          Назад
        </Button>
        <Button
          v-if="step === 'preview' && changeset.changes && changeset.changes.length > 0"
          @click="handleApply"
        >
          Применить изменения
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useStore } from "vuex";
import { Upload, Loader2, CheckCircle2, AlertCircle } from "lucide-vue-next";
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

const props = defineProps({
  modelValue: Boolean,
});
const emit = defineEmits(["update:modelValue", "saved"]);
const store = useStore();

const isOpen = computed({
  get: () => props.modelValue,
  set: (value) => emit("update:modelValue", value),
});

const step = ref("upload"); // upload | preview | applying | result
const isDragging = ref(false);
const uploadError = ref("");
const selectedFile = ref(null);
const changeset = ref({ changes: [], errors: [], summary: {} });
const applyResult = ref(null);
const applyError = ref(null);
const fileInput = ref(null);

function truncate(str, max) {
  if (!str) return str;
  return str.length > max ? str.slice(0, max) + "..." : str;
}

const changesetItems = computed(() => {
  if (!changeset.value.changes || changeset.value.changes.length === 0) return [];
  return changeset.value.changes.map((product) => ({
    label: `${product.productSku} — ${product.productName}`,
    fields: product.changes.map((change, j) => ({
      key: `${product.productId}-${j}`,
      label: change.fieldName,
      oldValue: truncate(change.oldValue, 100),
      newValue: truncate(change.newValue, 100),
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
    uploadError.value = "Поддерживается только формат .xlsx";
    return;
  }
  uploadError.value = "";
  selectedFile.value = file;
  step.value = "applying";

  await store.dispatch("importProductsPreview", {
    file,
    onSuccess: (data) => {
      changeset.value = data;
      step.value = "preview";
    },
    onError: (error) => {
      uploadError.value =
        error.response?.data?.message || "Ошибка при обработке файла";
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
        toast.success(`Обновлено товаров: ${data.appliedCount}`);
        emit("saved");
      }
    },
    onError: (error) => {
      applyError.value =
        error.response?.data?.message || "Ошибка при применении изменений";
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

// Reset state when dialog closes
watch(isOpen, (val) => {
  if (!val) {
    step.value = "upload";
    selectedFile.value = null;
    changeset.value = { changes: [], errors: [], summary: {} };
    applyResult.value = null;
    applyError.value = null;
    uploadError.value = "";
  }
});
</script>
