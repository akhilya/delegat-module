<script setup>
import { Spinner } from "@/components/ui/spinner";
import { onMounted, onUnmounted, computed } from "vue";
import { useRoute } from "vue-router";
import { useI18n } from "vue-i18n";

import ServiceLayout from "@/components/auth/ServiceLayout.vue";
import { Button } from "@/components/ui/button";
import {
  checkNow,
  startPolling,
  stopPolling,
  setReturnTo,
  useApiAvailability,
} from "@/composables/useApiAvailability";

const { t } = useI18n();
const route = useRoute();
const { isChecking } = useApiAvailability();

const fromPath = computed(() => {
  const raw = route.query.from;
  return typeof raw === "string" ? raw : Array.isArray(raw) ? raw[0] : null;
});

onMounted(() => {
  if (fromPath.value) setReturnTo(fromPath.value);
  startPolling({ fromQuery: fromPath.value ?? undefined });
});

onUnmounted(() => {
  stopPolling();
});

async function onRetry() {
  await checkNow(fromPath.value ?? undefined);
}
</script>

<template>
  <ServiceLayout
    :title="t('pages.unavailablePage.title')"
    :subtitle="t('pages.unavailablePage.description')"
  >
    <Button
      size="lg"
      :disabled="isChecking"
      @click="onRetry"
    >
      <Spinner v-if="isChecking" size="sm" />
      {{
        isChecking
          ? t("pages.unavailablePage.checking")
          : t("pages.unavailablePage.retry")
      }}
    </Button>
    <p
      class="text-[0.8125rem] leading-snug tracking-[-0.01em] text-muted-foreground"
      aria-live="polite"
    >
      {{
        isChecking
          ? t("pages.unavailablePage.checking")
          : t("pages.unavailablePage.autoHint")
      }}
    </p>
  </ServiceLayout>
</template>
