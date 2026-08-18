<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed, onMounted, reactive, ref, watch } from "vue";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import QuotaUsagePanel from "@/components/settings/QuotaUsagePanel.vue";
import { http, invalidateMeCache, loadMe } from "@/main";
import { notifyMeUpdated } from "@/lib/meCache";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  AlertTriangle,
  Cable,
  Download,
  KeyRound,
  Save,
  ScrollText,
} from "lucide-vue-next";
import { t } from "@/i18n";

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

function togglePublicApi() {
  if (publicApiLocked.value || publicApiLoading.value || publicApiSaving.value) return;
  publicApi.publicApiEnabled = !publicApi.publicApiEnabled;
}

function toggleLogging(key) {
  if (loggingLoading.value || loggingSaving.value) return;
  if (key === "logApiRequestsEnabled" && apiLoggingDisabled.value) return;
  logging[key] = !logging[key];
}

onMounted(async () => {
  await Promise.all([loadPlanFlags(), loadPublicApiSettings(), loadLoggingSettings()]);
  settingsLoaded.value = true;
});
</script>

<template>
  <div class="flex w-full max-w-xl flex-col gap-8 pb-10">
    <QuotaUsagePanel />

    <section>
      <InsetGroupLabel>{{ $t("pages.settingsOverviewPage.publicApiTitle") }}</InsetGroupLabel>
      <InsetGroup>
        <InsetGroupItem
          :as="publicApiLocked ? 'div' : 'button'"
          :disabled="!publicApiLocked && (!settingsLoaded || publicApiLoading || publicApiSaving)"
          :aria-pressed="publicApiLocked ? false : publicApi.publicApiEnabled"
          @click="togglePublicApi"
        >
          <template #leading>
            <KeyRound class="size-4" />
          </template>
          <p>{{ $t("pages.settingsOverviewPage.publicApiEnabledLabel") }}</p>
          <p class="text-muted-foreground mt-0.5 text-xs font-normal">
            {{
              publicApiLocked
                ? $t("pages.settingsOverviewPage.publicApiPlanLockedHint")
                : $t("pages.settingsOverviewPage.publicApiEnabledHint")
            }}
          </p>
          <template #trailing>
            <Switch
              v-if="settingsLoaded"
              :model-value="publicApiLocked ? false : publicApi.publicApiEnabled"
              :disabled="publicApiLoading || publicApiSaving || publicApiLocked"
              tabindex="-1"
              class="pointer-events-none"
            />
            <div
              v-else
              class="h-[1.15rem] w-8 shrink-0 rounded-full bg-muted"
              aria-hidden="true"
            />
          </template>
        </InsetGroupItem>
      </InsetGroup>
      <div v-if="!publicApiLocked" class="mt-2 flex justify-end">
        <Button
          size="sm"
          :disabled="!settingsLoaded || publicApiLoading || publicApiSaving || !publicApiDirty"
          @click="savePublicApiSettings"
        >
          <Spinner v-if="publicApiSaving" class="mr-2" />
          <Save v-else class="mr-2 size-4" />
          {{ $t("pages.settingsOverviewPage.savePublicApiButton") }}
        </Button>
      </div>
    </section>

    <section>
      <InsetGroupLabel>{{ $t("pages.settingsOverviewPage.loggingTitle") }}</InsetGroupLabel>
      <InsetGroup>
        <InsetGroupItem
          :disabled="!settingsLoaded || loggingLoading || loggingSaving"
          :aria-pressed="logging.logErrorsEnabled"
          @click="toggleLogging('logErrorsEnabled')"
        >
          <template #leading>
            <AlertTriangle class="size-4" />
          </template>
          <p>{{ $t("pages.settingsOverviewPage.logErrorsLabel") }}</p>
          <p class="text-muted-foreground mt-0.5 text-xs font-normal">
            {{ $t("pages.settingsOverviewPage.logErrorsHint") }}
          </p>
          <template #trailing>
            <Switch
              v-if="settingsLoaded"
              :model-value="logging.logErrorsEnabled"
              :disabled="loggingLoading || loggingSaving"
              tabindex="-1"
              class="pointer-events-none"
            />
            <div
              v-else
              class="h-[1.15rem] w-8 shrink-0 rounded-full bg-muted"
              aria-hidden="true"
            />
          </template>
        </InsetGroupItem>
        <InsetGroupItem
          :as="apiLoggingDisabled ? 'div' : 'button'"
          :disabled="!apiLoggingDisabled && (!settingsLoaded || loggingLoading || loggingSaving)"
          :aria-pressed="logging.logApiRequestsEnabled"
          @click="toggleLogging('logApiRequestsEnabled')"
        >
          <template #leading>
            <ScrollText class="size-4" />
          </template>
          <p>{{ $t("pages.settingsOverviewPage.logApiRequestsLabel") }}</p>
          <p class="text-muted-foreground mt-0.5 text-xs font-normal">
            {{
              publicApiLocked || !publicApi.publicApiEnabled
                ? $t("pages.settingsOverviewPage.logApiRequestsDisabledHint")
                : $t("pages.settingsOverviewPage.logApiRequestsHint")
            }}
          </p>
          <template #trailing>
            <Switch
              v-if="settingsLoaded"
              :model-value="logging.logApiRequestsEnabled"
              :disabled="apiLoggingDisabled"
              tabindex="-1"
              class="pointer-events-none"
            />
            <div
              v-else
              class="h-[1.15rem] w-8 shrink-0 rounded-full bg-muted"
              aria-hidden="true"
            />
          </template>
        </InsetGroupItem>
        <InsetGroupItem
          :disabled="!settingsLoaded || loggingLoading || loggingSaving"
          :aria-pressed="logging.logAdapterHttpEnabled"
          @click="toggleLogging('logAdapterHttpEnabled')"
        >
          <template #leading>
            <Cable class="size-4" />
          </template>
          <p>{{ $t("pages.settingsOverviewPage.logAdapterHttpLabel") }}</p>
          <p class="text-muted-foreground mt-0.5 text-xs font-normal">
            {{ $t("pages.settingsOverviewPage.logAdapterHttpHint") }}
          </p>
          <template #trailing>
            <Switch
              v-if="settingsLoaded"
              :model-value="logging.logAdapterHttpEnabled"
              :disabled="loggingLoading || loggingSaving"
              tabindex="-1"
              class="pointer-events-none"
            />
            <div
              v-else
              class="h-[1.15rem] w-8 shrink-0 rounded-full bg-muted"
              aria-hidden="true"
            />
          </template>
        </InsetGroupItem>
      </InsetGroup>
      <div class="mt-2 flex justify-end">
        <Button
          size="sm"
          :disabled="!settingsLoaded || loggingLoading || loggingSaving || !loggingDirty"
          @click="saveLoggingSettings"
        >
          <Spinner v-if="loggingSaving" class="mr-2" />
          <Save v-else class="mr-2 size-4" />
          {{ $t("pages.settingsOverviewPage.saveLoggingButton") }}
        </Button>
      </div>
    </section>

    <section>
      <InsetGroupLabel>{{ $t("pages.settingsOverviewPage.databaseBackupTitle") }}</InsetGroupLabel>
      <InsetGroup>
        <InsetGroupItem
          :disabled="isDumping"
          show-chevron
          @click="downloadDatabaseDump"
        >
          <template #leading>
            <Spinner v-if="isDumping" size="sm" />
            <Download v-else class="size-4" />
          </template>
          <p>{{ $t("pages.settingsOverviewPage.downloadDumpButton") }}</p>
          <p class="text-muted-foreground mt-0.5 text-xs font-normal">
            {{ $t("pages.settingsOverviewPage.databaseBackupDescription") }}
          </p>
        </InsetGroupItem>
      </InsetGroup>
    </section>
  </div>
</template>
