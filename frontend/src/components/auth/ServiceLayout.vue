<script setup>
import LanguageSwitcher from "@/components/LanguageSwitcher.vue";

defineProps({
  title: { type: String, required: true },
  subtitle: { type: String, default: "" },
});
</script>

<template>
  <main class="service-shell">
    <header class="service-chrome">
      <div class="service-chrome-spacer" aria-hidden="true" />
      <div class="service-lang">
        <LanguageSwitcher variant="corner" />
      </div>
    </header>

    <div class="service-main">
      <div class="service-panel">
        <h1 class="service-title">{{ title }}</h1>
        <p v-if="subtitle" class="service-subtitle">{{ subtitle }}</p>
        <div class="service-actions">
          <slot />
        </div>
      </div>
    </div>
  </main>
</template>

<style scoped>
.service-shell {
  min-height: 100dvh;
  display: grid;
  grid-template-rows: auto 1fr;
  background: var(--sidebar);
  color: var(--foreground);
}

.service-chrome {
  display: grid;
  grid-template-columns: 1fr auto;
  align-items: center;
  padding: 1.25rem 1.5rem 0.5rem;
}

.service-chrome-spacer {
  min-height: 2.25rem;
}

.service-lang {
  justify-self: end;
}

.service-main {
  display: flex;
  justify-content: center;
  align-items: center;
  overflow: auto;
  padding: 8vh 1.5rem 12vh;
}

.service-panel {
  width: 100%;
  max-width: 22.5rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  animation: service-enter var(--duration-overlay) var(--ease-out-quart) both;
}

.service-title {
  font-family: var(--font-display);
  font-size: 2rem;
  font-weight: 650;
  letter-spacing: -0.04em;
  line-height: 1.08;
  color: var(--foreground);
}

.service-subtitle {
  margin-top: 0.75rem;
  max-width: 32ch;
  font-size: 1.0625rem;
  line-height: 1.45;
  letter-spacing: -0.011em;
  color: var(--muted-foreground);
}

.service-actions {
  margin-top: 1.75rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.875rem;
  width: 100%;
}

.service-actions :deep([data-slot="button"]) {
  min-width: 10.5rem;
}

@keyframes service-enter {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .service-panel {
    animation: none;
  }
}

@media (min-width: 640px) {
  .service-title {
    font-size: 2.25rem;
  }
}
</style>
