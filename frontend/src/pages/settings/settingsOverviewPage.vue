<script setup>
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";

import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";


import Button from "@/components/ui/button/Button.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Input from "@/components/ui/input/Input.vue";
import { PageHeader } from "@/components/ui/page-header";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  AlertTriangle,
  ArrowRight,
  Key,
  Loader2,
  Plug,
  Save,
  Store,
  Trash2,
  UserCog,
} from "lucide-vue-next";
import {
  PAGE_ADMIN_AI,
  PAGE_ADMIN_ERROR_LOGS,
  PAGE_ADMIN_PLUGINS,
  PAGE_ADMIN_STORES,
  PAGE_ADMIN_TOKENS,
  PAGE_ADMIN_USERS,
} from "@/router";

const breadcrumbs = [{ label: "Настройки" }];
const isLoading = ref(false);
const summary = ref(null);
const settingDrafts = ref({});
const isSavingSettings = ref(false);
const clearingKey = ref(null);
const confirmDialog = ref({ open: false, title: "", message: "", resolve: null });

function showConfirm(title, message) {
  return new Promise((r) => { confirmDialog.value = { open: true, title, message, resolve: r }; });
}

const queues = computed(() => summary.value?.queues || []);

const changedSettings = computed(() =>
  (summary.value?.limits || []).filter((setting) => {
    const draft = settingDrafts.value[setting.key] ?? "";
    const saved = setting.value ?? "";
    return setting.isEditable && String(draft) !== String(saved);
  })
);

const hasSettingChanges = computed(() => changedSettings.value.length > 0);

const adminSections = [
  {
    title: "Магазины",
    description: "Создание магазинов, участники, администраторы магазинов и базовые параметры.",
    to: PAGE_ADMIN_STORES,
    icon: Store,
  },
  {
    title: "Пользователи",
    description: "Редактирование имён, паролей, активности и глобальных ролей.",
    to: PAGE_ADMIN_USERS,
    icon: UserCog,
  },
  {
    title: "Токены",
    description: "Приглашения и служебные токены доступа к платформе.",
    to: PAGE_ADMIN_TOKENS,
    icon: Key,
  },
  {
    title: "Плагины",
    description: "Установка и управление адаптерами платформы.",
    to: PAGE_ADMIN_PLUGINS,
    icon: Plug,
  },
  {
    title: "Логи ошибок",
    description: "Сохранённые серверные ошибки с деталями для диагностики.",
    to: PAGE_ADMIN_ERROR_LOGS,
    icon: AlertTriangle,
  },
  {
    title: "AI Модели",
    description: "Подключение AI-моделей по API (Anthropic, OpenAI-compatible) для AI-ассистента.",
    to: PAGE_ADMIN_AI,
    icon: Plug,
  },
];

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
  return status || "-";
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
      : "Очистить завершённые, ошибочные и отменённые записи?"
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
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      title="Общие настройки администратора"
      description="Платформенные параметры, доступ, очереди и диагностика"
    />

    <PageSection :loading="isLoading" class="space-y-5">
      <section class="grid gap-4 lg:grid-cols-[260px_minmax(0,1fr)]">
        <div>
          <h2 class="text-sm font-semibold">Общие параметры</h2>
          <p class="mt-1 text-sm text-muted-foreground">
            Лимиты, rate limit и системные ограничения платформы.
          </p>
        </div>
        <div class="grid gap-4">
          <div class="divide-y border-y">
            <div
              v-for="limit in summary?.limits || []"
              :key="limit.key"
              class="grid gap-3 py-3 md:grid-cols-[minmax(0,1fr)_260px]"
            >
              <div class="min-w-0">
                <div class="text-sm font-medium">{{ limit.label }}</div>
                <div class="text-xs text-muted-foreground">{{ limit.key }}</div>
                <div v-if="limit.description" class="mt-1 text-xs text-muted-foreground">
                  {{ limit.description }}
                </div>
              </div>
              <div class="space-y-1">
                <Input
                  v-model="settingDrafts[limit.key]"
                  :type="limit.valueType === 'integer' || limit.valueType === 'decimal' ? 'number' : 'text'"
                  :min="limit.valueType === 'decimal' ? '0.1' : '1'"
                  :step="limit.valueType === 'decimal' ? '0.1' : '1'"
                  :placeholder="limit.fallbackValue ? `fallback: ${limit.fallbackValue}` : 'не задано'"
                  :disabled="!limit.isEditable || isSavingSettings"
                />
                <div class="flex items-center gap-2 text-xs text-muted-foreground">
                  <span>Активно:</span>
                  <Badge variant="secondary">{{ limit.effectiveValue || "не задано" }}</Badge>
                </div>
              </div>
            </div>
            <div v-if="!summary?.limits?.length" class="py-3 text-sm text-muted-foreground">
              Общие параметры не найдены.
            </div>
          </div>
          <div class="flex items-center justify-end gap-3">
            <span v-if="hasSettingChanges" class="text-sm text-muted-foreground">
              Изменено параметров: {{ changedSettings.length }}
            </span>
            <Button :disabled="!hasSettingChanges || isSavingSettings" @click="saveAllSettings">
              <Loader2 v-if="isSavingSettings" class="mr-2 size-4 animate-spin" />
              <Save v-else class="mr-2 size-4" />
              Сохранить параметры
            </Button>
          </div>
        </div>
      </section>

      <section class="grid gap-4 border-t pt-5 lg:grid-cols-[260px_minmax(0,1fr)]">
          <div>
            <h2 class="text-sm font-semibold">Управление платформой</h2>
            <p class="mt-1 text-sm text-muted-foreground">
              Основные административные разделы встроены в эту страницу как единый каталог настроек.
            </p>
          </div>
          <div class="divide-y border-y">
            <RouterLink
              v-for="section in adminSections"
              :key="section.title"
              :to="section.to"
              class="grid gap-3 py-3 transition-colors hover:bg-muted/40 md:grid-cols-[auto_minmax(0,1fr)_auto]"
            >
              <component :is="section.icon" class="mt-0.5 size-4 text-muted-foreground" />
              <div>
                <div class="text-sm font-medium">{{ section.title }}</div>
                <div class="text-sm text-muted-foreground">{{ section.description }}</div>
              </div>
              <ArrowRight class="mt-0.5 size-4 text-muted-foreground" />
            </RouterLink>
          </div>
      </section>

      <section class="grid gap-4 border-t pt-5 lg:grid-cols-[260px_minmax(0,1fr)]">
          <div>
            <h2 class="text-sm font-semibold">Очереди и процессы</h2>
            <p class="mt-1 text-sm text-muted-foreground">
              Текущие очереди экспорта, импорта, изображений и история фоновых операций.
            </p>
          </div>

          <div class="grid gap-3">
            <div class="border-y py-3">
              <div class="flex items-center justify-between gap-3">
                <div>
                  <div class="text-sm font-medium">История фоновых процессов</div>
                  <div class="text-xs text-muted-foreground">Текущие и завершённые операции магазина</div>
                </div>
                <Badge variant="secondary">{{ summary?.operations?.total ?? 0 }}</Badge>
              </div>
              <div class="mt-3 flex flex-wrap gap-2">
                <Badge
                  v-for="entry in summary?.operations?.statuses || []"
                  :key="entry.status"
                  variant="secondary"
                >
                  {{ statusLabel(entry.status) }}: {{ entry.count }}
                </Badge>
                <Badge v-if="!summary?.operations?.total" variant="secondary">нет записей</Badge>
              </div>
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
            </div>

            <div
              v-for="queue in queues"
              :key="queue.key"
            class="border-y py-3"
            >
              <div class="flex items-center justify-between gap-3">
                <div class="text-sm font-medium">{{ queue.label }}</div>
                <Badge variant="secondary">{{ queue.total }}</Badge>
              </div>
              <div class="mt-3 flex flex-wrap gap-2">
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
            </div>
          </div>
      </section>
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
  </Page>
</template>
