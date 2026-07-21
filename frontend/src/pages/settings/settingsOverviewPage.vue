<script setup>
import { ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { PageHeader } from "@/components/ui/page-header";
import { Button } from "@/components/ui/button";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Download, Loader2 } from "lucide-vue-next";
import { t } from "@/i18n";

const isDumping = ref(false);

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
</script>

<template>
  <div class="flex flex-col">
    <PageHeader
      :title="$t('nav.overview')"
      :description="$t('pages.settingsOverviewPage.description')"
    />

    <PageSection class="!min-h-0">
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
