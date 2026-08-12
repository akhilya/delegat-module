<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed, onMounted, reactive, ref, watch } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { http, invalidateMeCache, loadMe } from "@/main";
import { notifyMeUpdated } from "@/lib/meCache";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Download,
  Save,
} from "lucide-vue-next";
import { t } from "@/i18n";
import SettingsSettingRow from "@/components/settings/SettingsSettingRow.vue";
import QuotaUsagePanel from "@/components/settings/QuotaUsagePanel.vue";

const isDumping = ref(false);
const loggingLoading = ref(true);
const loggingSaving = ref(false);
const publicApiLoading = ref(true);
const publicApiSaving = ref(false);
/** Mount switches only after both fetches so they never animate into the saved state. */
const settingsLoaded = ref(false);

const savedLogging = reactive({
  logErrorsEnabled: false,
  logApiRequestsEnabled: false,
  logAdapterHttpEnabled: false,
});

const logging = reactive({
  logErrorsEnabled: false,
  logApiRequestsEnabled: false,
  logAdapterHttpEnabled: false,
});

const savedPublicApi = reactive({
  publicApiEnabled: false,
});

const publicApi = reactive({
  publicApiEnabled: false,
});

/** Plan flag from /auth/me (`API_USAGE_ENABLED`). Default true until me loads. */
const apiUsageEnabled = ref(true);

const loggingDirty = computed(
  () =>
    logging.logErrorsEnabled !== savedLogging.logErrorsEnabled
    || logging.logApiRequestsEnabled !== savedLogging.logApiRequestsEnabled
    || logging.logAdapterHttpEnabled !== savedLogging.logAdapterHttpEnabled,
);

const publicApiLocked = computed(() => apiUsageEnabled.value === false);

const publicApiDirty = computed(
  () =>
    !publicApiLocked.value
    && publicApi.publicApiEnabled !== savedPublicApi.publicApiEnabled,
);

const apiLoggingDisabled = computed(
  () =>
    loggingLoading.value
    || loggingSaving.value
    || publicApiLoading.value
    || publicApiLocked.value
    || !publicApi.publicApiEnabled,
);

watch(
  () => publicApi.publicApiEnabled,
  (enabled) => {
    if (!enabled) logging.logApiRequestsEnabled = false;
  },
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

function applyPublicApiPayload(data) {
  const enabled = data.publicApiEnabled !== false;
  savedPublicApi.publicApiEnabled = enabled;
  publicApi.publicApiEnabled = enabled;
  if (!enabled) logging.logApiRequestsEnabled = false;
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

async function refreshMe() {
  invalidateMeCache();
  const me = await loadMe();
  notifyMeUpdated(me);
}

async function loadPlanFlags() {
  try {
    const me = await loadMe();
    apiUsageEnabled.value = me?.apiUsageEnabled !== false;
  } catch {
    apiUsageEnabled.value = true;
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

async function loadPublicApiSettings() {
  publicApiLoading.value = true;
  try {
    const { data } = await http.get("/settings/public-api", { cache: false });
    applyPublicApiPayload(data);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    publicApiLoading.value = false;
  }
}

async function saveLoggingSettings() {
  if (!loggingDirty.value || loggingSaving.value) return;
  loggingSaving.value = true;
  try {
    const payload = {
      logErrorsEnabled: logging.logErrorsEnabled,
      logApiRequestsEnabled: publicApi.publicApiEnabled
        ? logging.logApiRequestsEnabled
        : false,
      logAdapterHttpEnabled: logging.logAdapterHttpEnabled,
    };
    const { data } = await http.patch("/settings/logging", payload, { cache: false });
    applyLoggingPayload(data);
    await refreshMe();
    displaySonnerSuccess(t("pages.settingsOverviewPage.loggingSaved"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    loggingSaving.value = false;
  }
}

async function savePublicApiSettings() {
  if (!publicApiDirty.value || publicApiSaving.value) return;
  publicApiSaving.value = true;
  try {
    const { data } = await http.patch(
      "/settings/public-api",
      { publicApiEnabled: publicApi.publicApiEnabled },
      { cache: false },
    );
    applyPublicApiPayload(data);
    await loadLoggingSettings();
    await refreshMe();
    displaySonnerSuccess(t("pages.settingsOverviewPage.publicApiSaved"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    publicApiSaving.value = false;
  }
}

onMounted(async () => {
  await Promise.all([loadPlanFlags(), loadPublicApiSettings(), loadLoggingSettings()]);
  settingsLoaded.value = true;
});
</script>

<template>
  <div class="flex flex-col">
    <PageSection class="!min-h-0">
      <SettingsFormSection
        :title="$t('pages.settingsOverviewPage.publicApiTitle')"
        :description="$t('pages.settingsOverviewPage.publicApiDescription')"
      >
        <SettingsSettingRow
          :title="$t('pages.settingsOverviewPage.publicApiEnabledLabel')"
          :description="
            publicApiLocked
              ? $t('pages.settingsOverviewPage.publicApiPlanLockedHint')
              : $t('pages.settingsOverviewPage.publicApiEnabledHint')
          "
        >
          <Switch
            v-if="settingsLoaded"
            :model-value="publicApiLocked ? false : publicApi.publicApiEnabled"
            :disabled="publicApiLoading || publicApiSaving || publicApiLocked"
            @update:model-value="(v) => (publicApi.publicApiEnabled = v)"
          />
          <div
            v-else
            class="h-[1.15rem] w-8 shrink-0 rounded-full bg-muted"
            aria-hidden="true"
          />
        </SettingsSettingRow>

        <div
          v-if="!publicApiLocked"
          class="mt-4 flex justify-end"
        >
          <Button
            :disabled="!settingsLoaded || publicApiLoading || publicApiSaving || !publicApiDirty"
            @click="savePublicApiSettings"
          >
            <Spinner v-if="publicApiSaving" class="mr-2" />
            <Save v-else class="mr-2 size-4" />
            {{ $t("pages.settingsOverviewPage.savePublicApiButton") }}
          </Button>
        </div>
      </SettingsFormSection>

      <SettingsFormSection
        :title="$t('pages.settingsOverviewPage.loggingTitle')"
        :description="$t('pages.settingsOverviewPage.loggingDescription')"
      >
        <div class="grid gap-3">
          <SettingsSettingRow
            :title="$t('pages.settingsOverviewPage.logErrorsLabel')"
            :description="$t('pages.settingsOverviewPage.logErrorsHint')"
          >
            <Switch
              v-if="settingsLoaded"
              :model-value="logging.logErrorsEnabled"
              :disabled="loggingLoading || loggingSaving"
              @update:model-value="(v) => (logging.logErrorsEnabled = v)"
            />
            <div
              v-else
              class="h-[1.15rem] w-8 shrink-0 rounded-full bg-muted"
              aria-hidden="true"
            />
          </SettingsSettingRow>
          <SettingsSettingRow
            :title="$t('pages.settingsOverviewPage.logApiRequestsLabel')"
            :description="
              publicApiLocked || !publicApi.publicApiEnabled
                ? $t('pages.settingsOverviewPage.logApiRequestsDisabledHint')
                : $t('pages.settingsOverviewPage.logApiRequestsHint')
            "
          >
            <Switch
              v-if="settingsLoaded"
              :model-value="logging.logApiRequestsEnabled"
              :disabled="apiLoggingDisabled"
              @update:model-value="(v) => (logging.logApiRequestsEnabled = v)"
            />
            <div
              v-else
              class="h-[1.15rem] w-8 shrink-0 rounded-full bg-muted"
              aria-hidden="true"
            />
          </SettingsSettingRow>
          <SettingsSettingRow
            :title="$t('pages.settingsOverviewPage.logAdapterHttpLabel')"
            :description="$t('pages.settingsOverviewPage.logAdapterHttpHint')"
          >
            <Switch
              v-if="settingsLoaded"
              :model-value="logging.logAdapterHttpEnabled"
              :disabled="loggingLoading || loggingSaving"
              @update:model-value="(v) => (logging.logAdapterHttpEnabled = v)"
            />
            <div
              v-else
              class="h-[1.15rem] w-8 shrink-0 rounded-full bg-muted"
              aria-hidden="true"
            />
          </SettingsSettingRow>
        </div>

        <div class="mt-4 flex justify-end">
          <Button
            :disabled="!settingsLoaded || loggingLoading || loggingSaving || !loggingDirty"
            @click="saveLoggingSettings"
          >
            <Spinner v-if="loggingSaving" class="mr-2" />
            <Save v-else class="mr-2 size-4" />
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
            <Spinner v-if="isDumping" class="mr-2" />
            <Download v-else class="mr-2 size-4" />
            {{ $t("pages.settingsOverviewPage.downloadDumpButton") }}
          </Button>
        </SettingsFormField>
      </SettingsFormSection>

      <SettingsFormSection
        :title="$t('nav.usage')"
        :description="$t('pages.usagePage.description')"
      >
        <QuotaUsagePanel />
      </SettingsFormSection>
    </PageSection>
  </div>
</template>
