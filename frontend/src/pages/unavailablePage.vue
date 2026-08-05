<script setup>
import { onMounted, onUnmounted, computed } from "vue";
import { useRoute } from "vue-router";
import { useI18n } from "vue-i18n";
import { Loader2 } from "lucide-vue-next";
import Logo from "@/components/logo.vue";
import LanguageSwitcher from "@/components/LanguageSwitcher.vue";
import { Button } from "@/components/ui/button";
import { brandName } from "@/brand";
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
  <main class="unavailable-shell">
    <div class="unavailable-lang">
      <LanguageSwitcher variant="corner" />
    </div>

    <div class="unavailable-center">
      <header class="unavailable-brand">
        <Logo class="unavailable-logo" />
        <p class="unavailable-brand-name">{{ brandName }}</p>
      </header>

      <div class="unavailable-body">
        <p class="unavailable-eyebrow">
          {{ t("pages.unavailablePage.eyebrow") }}
        </p>
        <h1 class="unavailable-title">
          {{ t("pages.unavailablePage.title") }}
        </h1>
        <p class="unavailable-description">
          {{ t("pages.unavailablePage.description") }}
        </p>

        <div class="unavailable-actions">
          <Button
            class="unavailable-cta"
            :disabled="isChecking"
            @click="onRetry"
          >
            <Loader2 v-if="isChecking" class="size-4 animate-spin" />
            {{
              isChecking
                ? t("pages.unavailablePage.checking")
                : t("pages.unavailablePage.retry")
            }}
          </Button>
          <p v-if="!isChecking" class="unavailable-hint">
            {{ t("pages.unavailablePage.autoHint") }}
          </p>
        </div>
      </div>
    </div>
  </main>
</template>

<style scoped>
.unavailable-shell {
  position: relative;
  min-height: 100dvh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1.5rem;
  background: var(--sidebar);
  color: var(--foreground);
}

.unavailable-lang {
  position: absolute;
  top: 1rem;
  right: 1rem;
}

.unavailable-center {
  width: 100%;
  max-width: 24rem;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 2rem;
  animation: unavailable-enter 280ms ease-out both;
}

.unavailable-brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 0.5rem;
}

.unavailable-logo {
  width: 2.75rem !important;
  height: auto;
  opacity: 1 !important;
}

.unavailable-brand-name {
  font-family: var(--font-display);
  font-size: 1.5rem;
  font-weight: 650;
  letter-spacing: -0.03em;
  line-height: 1.15;
  color: var(--foreground);
}

.unavailable-body {
  text-align: center;
}

.unavailable-eyebrow {
  font-size: 0.75rem;
  font-weight: 500;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--muted-foreground);
}

.unavailable-title {
  margin-top: 0.5rem;
  font-family: var(--font-display);
  font-size: 1.375rem;
  font-weight: 600;
  letter-spacing: -0.025em;
  line-height: 1.25;
  color: var(--foreground);
}

.unavailable-description {
  margin-top: 0.625rem;
  font-size: 0.875rem;
  line-height: 1.55;
  color: var(--muted-foreground);
}

.unavailable-actions {
  margin-top: 1.75rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
}

.unavailable-cta {
  min-width: 11rem;
}

.unavailable-hint {
  font-size: 0.75rem;
  line-height: 1.4;
  color: var(--muted-foreground);
}

@keyframes unavailable-enter {
  from {
    opacity: 0;
    transform: translateY(6px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .unavailable-center {
    animation: none;
  }
}
</style>
