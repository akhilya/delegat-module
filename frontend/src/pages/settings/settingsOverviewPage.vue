<script setup>
import { computed, onMounted, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsPageHeader from "@/components/settings/SettingsPageHeader.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import InlineEditableValue from "@/components/settings/InlineEditableValue.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Loader2, Save, Trash2 } from "lucide-vue-next";

const isLoading = ref(false);
const summary = ref(null);
const settingDrafts = ref({});
const isSavingSettings = ref(false);
const clearingKey = ref(null);
const confirmDialog = ref({ open: false, title: "", message: "", resolve: null });

function showConfirm(title, message) {
  return new Promise((resolve) => {
    confirmDialog.value = { open: true, title, message, resolve };
  });
}

const queues = computed(() => summary.value?.queues || []);

const changedSettings = computed(() =>
  (summary.value?.limits || []).filter((setting) => {
    const draft = settingDrafts.value[setting.key] ?? "";
    const saved = setting.value ?? "";
    return setting.isEditable && String(draft) !== String(saved);
  }),
);

const hasSettingChanges = computed(() => changedSettings.value.length > 0);

function isLimitChanged(limit) {
  const draft = settingDrafts.value[limit.key] ?? "";
  const saved = limit.value ?? "";
  return limit.isEditable && String(draft) !== String(saved);
}

function statusCount(item, status) {
  return item.statuses?.find((entry) => entry.status?.toLowerCase() === status)?.count || 0;
}

function inactiveCount(item) {
  return ["completed", "failed", "cancelled", "partial"]
    .reduce((sum, status) => sum + statusCount(item, status), 0);
}

function statusLabel(status) {
  const value = String(status || "").toLowerCase();
  if (value === "running") return "выполняется";
  if (value === "paused") return "пауза";
  if (value === "pending") return "ожидает";
  if (value === "inprogress" || value === "in-progress") return "в процессе";
  if (value === "completed") return "завершено";
  if (value === "failed") return "ошибка";
  if (value === "cancelled") return "отменено";
  if (value === "partial") return "частично";
  return status || "—";
}

async function fetchSummary() {
  isLoading.value = true;
  try {
    const response = await http.get("/settings/operations/summary", { cache: false });
    summary.value = response.data;
    syncSettingDrafts();
  } catch (error) {
    summary.value = null;
    displaySonnerError(error);
  } finally {
    isLoading.value = false;
  }
}

function syncSettingDrafts() {
  const drafts = {};
  for (const setting of summary.value?.limits || []) {
    drafts[setting.key] = setting.value || "";
  }
  settingDrafts.value = drafts;
}

async function saveAllSettings() {
  if (!hasSettingChanges.value) return;

  isSavingSettings.value = true;
  try {
    for (const setting of changedSettings.value) {
      await http.patch(`/settings/app/${encodeURIComponent(setting.key)}`, {
        value: settingDrafts.value[setting.key],
      });
    }
    displaySonnerSuccess("Общие параметры сохранены");
    await fetchSummary();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isSavingSettings.value = false;
  }
}

async function clearTarget(target, status = "inactive") {
  const confirmed = await showConfirm(
    "Очистить очередь",
    status === "all"
      ? "Очистить все записи выбранной очереди, включая ожидающие и выполняющиеся?"
      : "Очистить завершённые, ошибочные и отменённые записи?",
  );
  if (!confirmed) return;

  clearingKey.value = `${target}:${status}`;
  try {
    const response = await http.post("/settings/operations/clear", { target, status }, { cache: false });
    displaySonnerSuccess(`Удалено записей: ${response.data?.deleted ?? 0}`);
    await fetchSummary();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    clearingKey.value = null;
  }
}

onMounted(fetchSummary);
</script>

<template>
  <div>
    <SettingsPageHeader
      title="Общие"
      description="Платформенные параметры, очереди и диагностика"
    />

    <PageSection :loading="isLoading" class="!min-h-0">
      <SettingsFormSection
        title="Общие параметры"
        description="Лимиты, rate limit и системные ограничения платформы."
      >
        <SettingsFormField
          v-for="limit in summary?.limits || []"
          :key="limit.key"
          :label="limit.label"
          :changed="isLimitChanged(limit)"
        >
          <InlineEditableValue
            :model-value="settingDrafts[limit.key]"
            :disabled="!limit.isEditable || isSavingSettings"
            :type="limit.valueType === 'integer' || limit.valueType === 'decimal' ? 'number' : 'text'"
            :min="limit.valueType === 'decimal' ? '0.1' : '1'"
            :step="limit.valueType === 'decimal' ? '0.1' : '1'"
            :placeholder="limit.fallbackValue ? `fallback: ${limit.fallbackValue}` : 'не задано'"
            @update:model-value="settingDrafts[limit.key] = $event"
          />
          <template #description>
            <span v-if="limit.description">{{ limit.description }}</span>
            <span v-if="limit.description && limit.key"> · </span>
            <span class="font-mono text-xs">{{ limit.key }}</span>
            <span> · Активно: </span>
            <Badge variant="secondary" class="align-middle text-xs">
              {{ limit.effectiveValue || "не задано" }}
            </Badge>
          </template>
        </SettingsFormField>

        <p v-if="!summary?.limits?.length" class="py-4 text-sm text-muted-foreground">
          Общие параметры не найдены.
        </p>

        <template #footer>
          <span v-if="hasSettingChanges" class="mr-auto text-sm text-muted-foreground">
            Изменено параметров: {{ changedSettings.length }}
          </span>
          <Button :disabled="!hasSettingChanges || isSavingSettings" @click="saveAllSettings">
            <Loader2 v-if="isSavingSettings" class="mr-2 size-4 animate-spin" />
            <Save v-else class="mr-2 size-4" />
            Сохранить параметры
          </Button>
        </template>
      </SettingsFormSection>

      <SettingsFormSection
        title="Очереди и процессы"
        description="Текущие очереди экспорта, импорта, изображений и история фоновых операций."
      >
        <SettingsFormField label="История фоновых процессов">
          <div class="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">{{ summary?.operations?.total ?? 0 }} записей</Badge>
            <Badge
              v-for="entry in summary?.operations?.statuses || []"
              :key="entry.status"
              variant="secondary"
            >
              {{ statusLabel(entry.status) }}: {{ entry.count }}
            </Badge>
            <Badge v-if="!summary?.operations?.total" variant="secondary">нет записей</Badge>
          </div>
          <template #description>
            Текущие и завершённые операции магазина.
          </template>
          <div class="mt-3 flex flex-wrap gap-2">
            <Button
              size="sm"
              variant="outline"
              :disabled="clearingKey === 'operations:inactive'"
              @click="clearTarget('operations', 'inactive')"
            >
              <Loader2 v-if="clearingKey === 'operations:inactive'" class="mr-2 size-4 animate-spin" />
              <Trash2 v-else class="mr-2 size-4" />
              Очистить завершённые
            </Button>
            <Button
              size="sm"
              variant="destructive"
              :disabled="clearingKey === 'operations:all'"
              @click="clearTarget('operations', 'all')"
            >
              <Loader2 v-if="clearingKey === 'operations:all'" class="mr-2 size-4 animate-spin" />
              <Trash2 v-else class="mr-2 size-4" />
              Очистить всё
            </Button>
          </div>
        </SettingsFormField>

        <SettingsFormField
          v-for="queue in queues"
          :key="queue.key"
          :label="queue.label"
        >
          <div class="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">{{ queue.total }} записей</Badge>
            <Badge v-for="entry in queue.statuses" :key="entry.status" variant="secondary">
              {{ statusLabel(entry.status) }}: {{ entry.count }}
            </Badge>
            <Badge v-if="queue.total === 0" variant="secondary">нет записей</Badge>
          </div>
          <div class="mt-3 flex flex-wrap gap-2">
            <Button
              size="sm"
              variant="outline"
              :disabled="inactiveCount(queue) === 0 || clearingKey === `${queue.key}:inactive`"
              @click="clearTarget(queue.key, 'inactive')"
            >
              <Loader2 v-if="clearingKey === `${queue.key}:inactive`" class="mr-2 size-4 animate-spin" />
              <Trash2 v-else class="mr-2 size-4" />
              Очистить неактивные
            </Button>
            <Button
              size="sm"
              variant="destructive"
              :disabled="queue.total === 0 || clearingKey === `${queue.key}:all`"
              @click="clearTarget(queue.key, 'all')"
            >
              <Loader2 v-if="clearingKey === `${queue.key}:all`" class="mr-2 size-4 animate-spin" />
              <Trash2 v-else class="mr-2 size-4" />
              Очистить всё
            </Button>
          </div>
        </SettingsFormField>
      </SettingsFormSection>
    </PageSection>

    <AlertDialog
      :open="confirmDialog.open"
      @update:open="(v) => { if (!v) confirmDialog.resolve?.(false); confirmDialog.open = v; }"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmDialog.title }}</AlertDialogTitle>
          <AlertDialogDescription>{{ confirmDialog.message }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel @click="confirmDialog.resolve(false); confirmDialog.open = false">Отмена</AlertDialogCancel>
          <AlertDialogAction @click="confirmDialog.resolve(true); confirmDialog.open = false">Очистить</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>