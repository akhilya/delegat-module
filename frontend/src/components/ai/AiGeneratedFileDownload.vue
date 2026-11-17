<script setup>
import { nextTick, onBeforeUnmount, ref, watch } from "vue";
import { useStore } from "vuex";
import { useI18n } from "vue-i18n";
import { Download, Loader2 } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { fetchWithAuth } from "@/utils/fetchWithAuth";
import { triggerBrowserDownload } from "@/lib/artifactDownload";

const props = defineProps({
  file: { type: Object, required: true },
  contextKey: { type: String, required: true },
});
const store = useStore();
const { t } = useI18n();
const busy = ref(false);
const errorKey = ref(null);
let controller;
const objectUrls = new Map();
function reset() {
  controller?.abort();
  controller = null;
  busy.value = false;
  errorKey.value = null;
  for (const [url, timer] of objectUrls) {
    clearTimeout(timer);
    URL.revokeObjectURL(url);
  }
  objectUrls.clear();
}
watch(
  [
    () => props.file.downloadUrl,
    () => props.file.fileName,
    () => props.file.length,
    () => props.contextKey,
    () => store.state.isSwitchingStore,
  ],
  reset,
  { flush: "sync" },
);
onBeforeUnmount(reset);
async function download(event) {
  if (busy.value || store.state.isSwitchingStore) return;
  const button = event?.currentTarget;
  const restoreFocus =
    button instanceof HTMLElement && document.activeElement === button;
  const contextKey = props.contextKey;
  const file = props.file;
  const request = new AbortController();
  controller = request;
  busy.value = true;
  errorKey.value = null;
  try {
    const response = await fetchWithAuth(file.downloadUrl, {
      signal: request.signal,
      redirect: "error",
    });
    if (request.signal.aborted || controller !== request) return;
    if (!response.ok) {
      errorKey.value = [404, 410].includes(response.status)
        ? "ai.generatedFileDownloadMissing"
        : [400, 401, 403].includes(response.status)
          ? "ai.generatedFileDownloadForbidden"
          : "ai.generatedFileDownloadFailed";
      return;
    }
    const blob = await response.blob();
    if (request.signal.aborted || controller !== request) return;
    if (blob.size !== file.length) {
      errorKey.value = "ai.generatedFileDownloadFailed";
      return;
    }
    const url = URL.createObjectURL(blob);
    const timer = setTimeout(() => {
      URL.revokeObjectURL(url);
      objectUrls.delete(url);
    }, 1000);
    objectUrls.set(url, timer);
    triggerBrowserDownload(url, file.fileName);
  } catch {
    if (!request.signal.aborted && controller === request)
      errorKey.value = "ai.generatedFileDownloadFailed";
  } finally {
    if (controller === request) {
      busy.value = false;
      controller = null;
      if (restoreFocus) {
        await nextTick();
        if (
          !request.signal.aborted &&
          props.contextKey === contextKey &&
          props.file.downloadUrl === file.downloadUrl &&
          !store.state.isSwitchingStore &&
          button.isConnected &&
          !button.disabled &&
          document.activeElement === document.body
        )
          button.focus({ preventScroll: true });
      }
    }
  }
}
</script>

<template>
  <div
    class="mt-2 flex min-w-0 flex-col items-start gap-1"
    data-testid="ai-generated-file-download"
  >
    <Button
      type="button"
      variant="outline"
      size="sm"
      :disabled="busy || store.state.isSwitchingStore"
      :aria-busy="busy"
      :aria-label="t('ai.generatedFileDownloadNamed', { name: file.fileName })"
      @click="download"
    >
      <Loader2
        v-if="busy"
        class="size-3.5 motion-safe:animate-spin"
        aria-hidden="true"
      />
      <Download v-else class="size-3.5" aria-hidden="true" />
      {{
        busy ? t("ai.generatedFileDownloading") : t("ai.generatedFileDownload")
      }}
    </Button>
    <p v-if="errorKey" role="alert" class="text-xs text-destructive">
      {{ t(errorKey) }}
    </p>
  </div>
</template>
