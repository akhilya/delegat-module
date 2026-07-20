<script setup>
import { computed, onMounted, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { PageHeader } from "@/components/ui/page-header";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Loader2, Save, Trash2 } from "lucide-vue-next";
import { t } from "@/i18n";

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

function statusCount(item, status) {
  return item.statuses?.find((entry) => entry.status?.toLowerCase() === status)?.count || 0;
}

function inactiveCount(item) {
  return ["completed", "failed", "cancelled", "partial"]
    .reduce((sum, status) => sum + statusCount(item, status), 0);
}

function statusLabel(status) {
  const value = String(status || "").toLowerCase();
  if (value === "running") return t("operations.status.running");
  if (value === "paused") return t("operations.status.paused");
  if (value === "pending") return t("pages.settingsOverviewPage.statusPending");
  if (value === "inprogress" || value === "in-progress") return t("pages.settingsOverviewPage.statusInProgress");
  if (value === "completed") return t("operations.status.completed");
  if (value === "failed") return t("operations.status.failed");
  if (value === "cancelled") return t("operations.status.cancelled");
  if (value === "partial") return t("operations.status.partial");
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
    displaySonnerSuccess(t("pages.settingsOverviewPage.settingsSaved"));
    await fetchSummary();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isSavingSettings.value = false;
  }
}

async function clearTarget(target, status = "inactive") {
  const confirmed = await showConfirm(
    t("pages.settingsOverviewPage.clearQueueTitle"),
    status === "all"
      ? t("pages.settingsOverviewPage.clearAllDescription")
      : t("pages.settingsOverviewPage.clearInactiveDescription"),
  );
  if (!confirmed) return;

  clearingKey.value = `${target}:${status}`;
  try {
    const response = await http.post("/settings/operations/clear", { target, status }, { cache: false });
    displaySonnerSuccess(t("pages.settingsOverviewPage.deletedRecords", { count: response.data?.deleted ?? 0 }));
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
  <div class="flex flex-col">
    <PageHeader
      :title="$t('nav.overview')"
      :description="$t('pages.settingsOverviewPage.description')"
    />

    <PageSection :loading="isLoading" class="!min-h-0">
      <SettingsFormSection
        :title="$t('pages.settingsOverviewPage.generalParamsTitle')"
        :description="$t('pages.settingsOverviewPage.generalParamsDescription')"
      >
        <SettingsFormField
          v-for="limit in summary?.limits || []"
          :key="limit.key"
          :label="limit.label"
        >
          <Input
            v-model="settingDrafts[limit.key]"
            class="max-w-md"
            :type="limit.valueType === 'integer' || limit.valueType === 'decimal' ? 'number' : 'text'"
            :min="limit.valueType === 'decimal' ? '0.1' : '1'"
            :step="limit.valueType === 'decimal' ? '0.1' : '1'"
            :placeholder="limit.fallbackValue ? `fallback: ${limit.fallbackValue}` : $t('pages.settingsOverviewPage.notSet')"
            :disabled="!limit.isEditable || isSavingSettings"
          />
          <template #description>
            <span v-if="limit.description">{{ limit.description }}</span>
            <span v-if="limit.description && limit.key"> · </span>
            <span class="font-mono text-xs">{{ limit.key }}</span>
            <span> · {{ $t("pages.settingsOverviewPage.activeLabel") }}: </span>
            <Badge variant="secondary" class="align-middle text-xs">
              {{ limit.effectiveValue || $t("pages.settingsOverviewPage.notSet") }}
            </Badge>
          </template>
        </SettingsFormField>

        <p v-if="!summary?.limits?.length" class="py-4 text-sm text-muted-foreground">
          {{ $t("pages.settingsOverviewPage.noParamsFound") }}
        </p>

        <template #footer>
          <span v-if="hasSettingChanges" class="mr-auto text-sm text-muted-foreground">
            {{ $t("pages.settingsOverviewPage.changedParamsCount", { count: changedSettings.length }) }}
          </span>
          <Button :disabled="!hasSettingChanges || isSavingSettings" @click="saveAllSettings">
            <Loader2 v-if="isSavingSettings" class="mr-2 size-4 animate-spin" />
            <Save v-else class="mr-2 size-4" />
            {{ $t("pages.settingsOverviewPage.saveParamsButton") }}
          </Button>
        </template>
      </SettingsFormSection>

      <SettingsFormSection
        :title="$t('pages.settingsOverviewPage.queuesSectionTitle')"
        :description="$t('pages.settingsOverviewPage.queuesSectionDescription')"
      >
        <SettingsFormField :label="$t('pages.settingsOverviewPage.historyLabel')">
          <div class="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">{{ $t("pages.settingsOverviewPage.recordsCount", { count: summary?.operations?.total ?? 0 }) }}</Badge>
            <Badge
              v-for="entry in summary?.operations?.statuses || []"
              :key="entry.status"
              variant="secondary"
            >
              {{ statusLabel(entry.status) }}: {{ entry.count }}
            </Badge>
            <Badge v-if="!summary?.operations?.total" variant="secondary">{{ $t("pages.settingsOverviewPage.noRecords") }}</Badge>
          </div>
          <template #description>
            {{ $t("pages.settingsOverviewPage.historyDescription") }}
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
              {{ $t("pages.settingsOverviewPage.clearCompletedButton") }}
            </Button>
            <Button
              size="sm"
              variant="destructive"
              :disabled="clearingKey === 'operations:all'"
              @click="clearTarget('operations', 'all')"
            >
              <Loader2 v-if="clearingKey === 'operations:all'" class="mr-2 size-4 animate-spin" />
              <Trash2 v-else class="mr-2 size-4" />
              {{ $t("pages.settingsOverviewPage.clearAllButton") }}
            </Button>
          </div>
        </SettingsFormField>

        <SettingsFormField
          v-for="queue in queues"
          :key="queue.key"
          :label="queue.label"
        >
          <div class="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">{{ $t("pages.settingsOverviewPage.recordsCount", { count: queue.total }) }}</Badge>
            <Badge v-for="entry in queue.statuses" :key="entry.status" variant="secondary">
              {{ statusLabel(entry.status) }}: {{ entry.count }}
            </Badge>
            <Badge v-if="queue.total === 0" variant="secondary">{{ $t("pages.settingsOverviewPage.noRecords") }}</Badge>
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
              {{ $t("pages.settingsOverviewPage.clearInactiveButton") }}
            </Button>
            <Button
              size="sm"
              variant="destructive"
              :disabled="queue.total === 0 || clearingKey === `${queue.key}:all`"
              @click="clearTarget(queue.key, 'all')"
            >
              <Loader2 v-if="clearingKey === `${queue.key}:all`" class="mr-2 size-4 animate-spin" />
              <Trash2 v-else class="mr-2 size-4" />
              {{ $t("pages.settingsOverviewPage.clearAllButton") }}
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
          <AlertDialogCancel @click="confirmDialog.resolve(false); confirmDialog.open = false">{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction @click="confirmDialog.resolve(true); confirmDialog.open = false">{{ $t("operations.clearCompletedConfirm") }}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>