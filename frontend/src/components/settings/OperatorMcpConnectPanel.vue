<script setup>
import { computed, onMounted, onUnmounted, ref } from "vue";
import { Cable } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { LogCodeBlock } from "@/components/logs";
import { http } from "@/main";
import { getCachedMe, ME_UPDATED_EVENT } from "@/lib/meCache";
import { resolveApiBaseUrl } from "@/utils/apiBaseUrl";
import { displaySonnerError } from "@/store/sonnerHelper";
import {
  issueOperatorMcpKey,
  operatorMcpJson,
  operatorMcpPublicOrigin,
} from "@/lib/operatorMcpClient";

const me = ref(getCachedMe() ?? null);
const isConnecting = ref(false);
const isOpen = ref(false);
const issuedKey = ref("");

function refreshMe() {
  me.value = getCachedMe() ?? null;
}

function onMeUpdated(event) {
  if (event?.detail && typeof event.detail === "object") {
    me.value = event.detail;
  } else {
    refreshMe();
  }
}

onMounted(() => {
  refreshMe();
  window.addEventListener(ME_UPDATED_EVENT, onMeUpdated);
});

onUnmounted(() => {
  window.removeEventListener(ME_UPDATED_EVENT, onMeUpdated);
});

const mcpJson = computed(() => {
  const key = issuedKey.value;
  if (!key) return null;
  return operatorMcpJson({
    origin: operatorMcpPublicOrigin({
      pageOrigin:
        typeof window !== "undefined" ? window.location.origin : "",
      apiBase: resolveApiBaseUrl(import.meta.env.VITE_API_SERVER),
    }),
    apiKey: key,
    storeId: me.value?.selectedStoreId ?? "",
  });
});

async function connect() {
  if (isConnecting.value) return;
  if (issuedKey.value) {
    isOpen.value = true;
    return;
  }
  isConnecting.value = true;
  try {
    issuedKey.value = await issueOperatorMcpKey({
      post: (path, body) => http.post(path, body, { cache: false }),
    });
    isOpen.value = true;
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isConnecting.value = false;
  }
}
</script>

<template>
  <div class="flex min-h-0 flex-col">
    <div class="mb-3 flex flex-wrap items-end justify-between gap-3">
      <div class="min-w-0">
        <h2 class="text-sm font-medium">
          {{ $t("pages.aiModelsPage.operatorMcpSectionTitle") }}
        </h2>
        <p class="mt-0.5 text-xs text-muted-foreground">
          {{ $t("pages.aiModelsPage.operatorMcpSectionDescription") }}
        </p>
      </div>
      <Button size="sm" :disabled="isConnecting" @click="connect">
        <Spinner v-if="isConnecting" size="xs" class="mr-2" />
        <Cable v-else class="mr-2 size-4" />
        {{ $t("pages.aiModelsPage.operatorMcpConnectButton") }}
      </Button>
    </div>

    <Dialog v-model:open="isOpen">
      <DialogContent
        size="lg"
        class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
      >
        <DialogHeader variant="bar">
          <DialogTitle>{{
            $t("pages.aiModelsPage.operatorMcpDialogTitle")
          }}</DialogTitle>
          <DialogDescription>
            {{ $t("pages.aiModelsPage.operatorMcpDialogDescription") }}
          </DialogDescription>
        </DialogHeader>
        <div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
          <LogCodeBlock
            v-if="mcpJson"
            :label="$t('pages.aiModelsPage.operatorMcpJsonLabel')"
            json
            :text="mcpJson"
            max-height-class="max-h-72"
          />
        </div>
        <DialogFooter variant="bar">
          <Button variant="outline" @click="isOpen = false">
            {{ $t("common.close") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
