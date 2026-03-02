<template>
  <Dialog v-model:open="isOpen">
    <DialogContent class="max-w-3xl max-h-[90vh] overflow-hidden flex flex-col">
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
          <div class="bg-green-50 dark:bg-green-950 text-green-700 dark:text-green-300 rounded px-3 py-1.5">
            Изменено: <span class="font-medium">{{ changeset.summary.changedProducts }}</span>
          </div>
          <div v-if="changeset.summary.unchangedProducts > 0" class="bg-muted rounded px-3 py-1.5">
            Без изменений: <span class="font-medium">{{ changeset.summary.unchangedProducts }}</span>
          </div>
          <div v-if="changeset.summary.errorCount > 0" class="bg-red-50 dark:bg-red-950 text-red-700 dark:text-red-300 rounded px-3 py-1.5">
            Ошибок: <span class="font-medium">{{ changeset.summary.errorCount }}</span>
          </div>
        </div>

        <!-- Errors -->
        <div v-if="changeset.errors && changeset.errors.length > 0" class="space-y-1">
          <p class="text-sm font-medium text-destructive">Ошибки:</p>
          <div
            v-for="(err, i) in changeset.errors"
            :key="i"
            class="text-sm bg-red-50 dark:bg-red-950 text-red-700 dark:text-red-300 rounded px-3 py-1.5"
          >
            Строка {{ err.row }}<template v-if="err.productId"> (ID {{ err.productId }})</template>:
            {{ err.field }} &mdash; {{ err.message }}
          </div>
        </div>

        <!-- Changes table -->
        <div v-if="changeset.changes && changeset.changes.length > 0">
          <div
            v-for="product in changeset.changes"
            :key="product.productId"
            class="border rounded-lg mb-3"
          >
            <div class="px-3 py-2 bg-muted/50 border-b text-sm font-medium">
              {{ product.productSku }} &mdash; {{ product.productName }}
            </div>
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b text-left text-muted-foreground">
                  <th class="px-3 py-1.5 font-medium w-1/4">Поле</th>
                  <th class="px-3 py-1.5 font-medium w-[37.5%]">Было</th>
                  <th class="px-3 py-1.5 font-medium w-[37.5%]">Стало</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="(change, j) in product.changes"
                  :key="j"
                  class="border-b last:border-b-0"
                >
                  <td class="px-3 py-1.5 text-muted-foreground">{{ change.fieldName }}</td>
                  <td class="px-3 py-1.5">
                    <span class="bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300 px-1 rounded text-xs break-all">
                      {{ truncate(change.oldValue, 100) || '(пусто)' }}
                    </span>
                  </td>
                  <td class="px-3 py-1.5">
                    <span class="bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300 px-1 rounded text-xs break-all">
                      {{ truncate(change.newValue, 100) || '(пусто)' }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div v-if="!changeset.changes || changeset.changes.length === 0" class="text-center text-sm text-muted-foreground py-8">
          Изменений не обнаружено
        </div>
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
            <CheckCircle2 class="h-10 w-10 mx-auto text-green-600" />
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

      <DialogFooter class="mt-4">
        <Button
          v-if="step === 'upload' || step === 'result'"
          variant="outline"
          @click="isOpen = false"
        >
          {{ step === 'result' ? 'Закрыть' : 'Отмена' }}
        </Button>
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
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
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
