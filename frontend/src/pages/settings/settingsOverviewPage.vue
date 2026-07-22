<script setup>
import { computed, onMounted, reactive, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { PageHeader } from "@/components/ui/page-header";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { http, invalidateMeCache, loadMe } from "@/main";
import { notifyMeUpdated } from "@/lib/meCache";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Download, Loader2 } from "lucide-vue-next";
import { t } from "@/i18n";

const isDumping = ref(false);
const loggingLoading = ref(true);
const loggingSaving = ref(false);

const savedLogging = reactive({
  logErrorsEnabled: true,
  logApiRequestsEnabled: false,
  logAdapterHttpEnabled: false,
});

const logging = reactive({
  logErrorsEnabled: true,
  logApiRequestsEnabled: false,
  logAdapterHttpEnabled: false,
});

const loggingDirty = computed(
  () =>
    logging.logErrorsEnabled !== savedLogging.logErrorsEnabled
    || logging.logApiRequestsEnabled !== savedLogging.logApiRequestsEnabled
    || logging.logAdapterHttpEnabled !== savedLogging.logAdapterHttpEnabled,
);

function applyLoggingPayload(data) {
  const next = {
    logErrorsEnabled: data.logErrorsEnabled === true,
    logApiRequestsEnabled: data.logApiRequestsEnabled === true,
    logAdapterHttpEnabled: data.logAdapterHttpEnabled === true,
  };
  Object.assign(savedLogging, next);
  Object.assign(logging, next);
}

function parseDumpFilename(disposition, fallback) {
  if (!disposition) return fallback;
  const utf8 = disposition.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8?.[1]) {
    try {
      return decodeURIComponent(utf8[1].trim());
    } catch {
      /* fall through */
    }
  }
  const plain = disposition.match(/filename="?([^";]+)"?/i);
  return plain?.[1]?.trim() || fallback;
}

async function normalizeBlobError(error) {
  const data = error?.response?.data;
  if (!(data instanceof Blob)) return error;
  try {
    const text = await data.text();
    const json = JSON.parse(text);
    return { ...error, response: { ...error.response, data: json } };
  } catch {
    return error;
  }
}

async function downloadDatabaseDump() {
  if (isDumping.value) return;
  isDumping.value = true;
  displaySonnerSuccess(t("pages.settingsOverviewPage.dumpStarted"));
  try {
    const response = await http.get("/settings/database/dump", {
      responseType: "blob",
      cache: false,
      timeout: 10 * 60 * 1000,
    });
    const disposition = response.headers?.["content-disposition"] || "";
    const filename = parseDumpFilename(
      disposition,
      `delegat-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-")}.sql`,
    );
    const url = URL.createObjectURL(response.data);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
    displaySonnerSuccess(t("pages.settingsOverviewPage.dumpDownloaded"));
  } catch (error) {
    displaySonnerError(await normalizeBlobError(error));
  } finally {
    isDumping.value = false;
  }
}

async function loadLoggingSettings() {
  loggingLoading.value = true;
  try {
    const { data } = await http.get("/settings/logging", { cache: false });
    applyLoggingPayload(data);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    loggingLoading.value = false;
  }
}

async function saveLoggingSettings() {
  if (!loggingDirty.value || loggingSaving.value) return;
  loggingSaving.value = true;
  try {
    const { data } = await http.patch(
      "/settings/logging",
      {
        logErrorsEnabled: logging.logErrorsEnabled,
        logApiRequestsEnabled: logging.logApiRequestsEnabled,
        logAdapterHttpEnabled: logging.logAdapterHttpEnabled,
      },
      { cache: false },
    );
    applyLoggingPayload(data);
    invalidateMeCache();
    const me = await loadMe();
    notifyMeUpdated(me);
    displaySonnerSuccess(t("pages.settingsOverviewPage.loggingSaved"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    loggingSaving.value = false;
  }
}

onMounted(loadLoggingSettings);
</script>

<template>
  <div class="flex flex-col">
    <PageHeader
      :title="$t('nav.overview')"
      :description="$t('pages.settingsOverviewPage.description')"
    />

    <PageSection class="!min-h-0">
      <SettingsFormSection
        :title="$t('pages.settingsOverviewPage.loggingTitle')"
        :description="$t('pages.settingsOverviewPage.loggingDescription')"
      >
        <div class="grid gap-3">
          <div class="flex items-center justify-between gap-4 rounded-lg border p-3">
            <div class="min-w-0">
              <div class="text-sm font-medium">{{ $t("pages.settingsOverviewPage.logErrorsLabel") }}</div>
              <div class="text-xs text-muted-foreground">{{ $t("pages.settingsOverviewPage.logErrorsHint") }}</div>
            </div>
            <Switch
              :model-value="logging.logErrorsEnabled"
              :disabled="loggingLoading || loggingSaving"
              @update:model-value="(v) => (logging.logErrorsEnabled = v)"
            />
          </div>
          <div class="flex items-center justify-between gap-4 rounded-lg border p-3">
            <div class="min-w-0">
              <div class="text-sm font-medium">{{ $t("pages.settingsOverviewPage.logApiRequestsLabel") }}</div>
              <div class="text-xs text-muted-foreground">{{ $t("pages.settingsOverviewPage.logApiRequestsHint") }}</div>
            </div>
            <Switch
              :model-value="logging.logApiRequestsEnabled"
              :disabled="loggingLoading || loggingSaving"
              @update:model-value="(v) => (logging.logApiRequestsEnabled = v)"
            />
          </div>
          <div class="flex items-center justify-between gap-4 rounded-lg border p-3">
            <div class="min-w-0">
              <div class="text-sm font-medium">{{ $t("pages.settingsOverviewPage.logAdapterHttpLabel") }}</div>
              <div class="text-xs text-muted-foreground">{{ $t("pages.settingsOverviewPage.logAdapterHttpHint") }}</div>
            </div>
            <Switch
              :model-value="logging.logAdapterHttpEnabled"
              :disabled="loggingLoading || loggingSaving"
              @update:model-value="(v) => (logging.logAdapterHttpEnabled = v)"
            />
          </div>
        </div>

        <div class="mt-4 flex justify-end">
          <Button
            :disabled="loggingLoading || loggingSaving || !loggingDirty"
            @click="saveLoggingSettings"
          >
            <Loader2 v-if="loggingSaving" class="mr-2 size-4 animate-spin" />
            {{ $t("pages.settingsOverviewPage.saveLoggingButton") }}
          </Button>
        </div>
      </SettingsFormSection>

      <SettingsFormSection
        :title="$t('pages.settingsOverviewPage.databaseBackupTitle')"
        :description="$t('pages.settingsOverviewPage.databaseBackupDescription')"
      >
        <SettingsFormField :label="$t('pages.settingsOverviewPage.downloadDumpButton')">
          <Button :disabled="isDumping" @click="downloadDatabaseDump">
            <Loader2 v-if="isDumping" class="mr-2 size-4 animate-spin" />
            <Download v-else class="mr-2 size-4" />
            {{ $t("pages.settingsOverviewPage.downloadDumpButton") }}
          </Button>
        </SettingsFormField>
      </SettingsFormSection>
    </PageSection>
  </div>
</template>
