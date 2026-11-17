<script setup>
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";
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
const { t, locale } = useI18n();
const busy = ref(false);
const errorKey = ref(null);
const expired = ref(false);
let controller;
let expiryTimer;
const objectUrls = new Map();

const isTemporary = computed(() => props.file?.kind === "temporary");
const expiryMs = computed(() => {
  if (!isTemporary.value || typeof props.file?.expiresAtUtc !== "string")
    return null;
  const ms = Date.parse(props.file.expiresAtUtc);
  return Number.isNaN(ms) ? null : ms;
});
const expiresLabel = computed(() => {
  if (expiryMs.value == null) return "";
  try {
    return new Intl.DateTimeFormat(locale.value, {
      dateStyle: "medium",
      timeStyle: "short",
    }).format(new Date(expiryMs.value));
  } catch {
    return new Date(expiryMs.value).toLocaleString();
  }
});
const expiredNow = () => expiryMs.value != null && Date.now() >= expiryMs.value;

function scheduleExpiry() {
  clearTimeout(expiryTimer);
  expiryTimer = undefined;
  if (expiryMs.value == null) {
    expired.value = false;
    return;
  }
  const delay = expiryMs.value - Date.now();
  if (delay <= 0) {
    expired.value = true;
    return;
  }
  expired.value = false;
  // setTimeout clamps >~24.8 days; re-evaluate on firing instead of polling.
  expiryTimer = setTimeout(scheduleExpiry, Math.min(delay, 2147483647));
}

function reset() {
  clearTimeout(expiryTimer);
  expiryTimer = undefined;
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
    () => props.file.kind,
    () => props.file.expiresAtUtc,
    () => props.contextKey,
    () => store.state.isSwitchingStore,
  ],
  () => {
    reset();
    scheduleExpiry();
  },
  { flush: "sync", immediate: true },
);
onBeforeUnmount(reset);

function failureKey(status) {
  if ([404, 410].includes(status))
    return isTemporary.value
      ? "ai.generatedTemporaryMissing"
      : "ai.generatedFileDownloadMissing";
  if ([400, 401, 403].includes(status))
    return "ai.generatedFileDownloadForbidden";
  return isTemporary.value
    ? "ai.generatedTemporaryFailed"
    : "ai.generatedFileDownloadFailed";
}

async function download(event) {
  if (busy.value || store.state.isSwitchingStore) return;
  // Background tabs may lag timers: re-check the deadline synchronously.
  if (expiredNow()) {
    expired.value = true;
    return;
  }
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
      errorKey.value = failureKey(response.status);
      return;
    }
    const blob = await response.blob();
    if (request.signal.aborted || controller !== request) return;
    if (blob.size !== file.length) {
      errorKey.value = isTemporary.value
        ? "ai.generatedTemporaryFailed"
        : "ai.generatedFileDownloadFailed";
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
      errorKey.value = isTemporary.value
        ? "ai.generatedTemporaryFailed"
        : "ai.generatedFileDownloadFailed";
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
      :disabled="busy || expired || store.state.isSwitchingStore"
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
    <p
      v-if="isTemporary"
      class="text-xs text-muted-foreground"
      data-testid="ai-temporary-expiry"
    >
      {{
        expired
          ? t("ai.generatedTemporaryExpired")
          : t("ai.generatedTemporaryUntil", { time: expiresLabel })
      }}
    </p>
    <p v-if="errorKey" role="alert" class="text-xs text-destructive">
      {{ t(errorKey) }}
    </p>
  </div>
</template>
