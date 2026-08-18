<script setup>
import LanguageSwitcher from "@/components/LanguageSwitcher.vue";
import Logo from "@/components/logo.vue";
import { brandLogo } from "@/brand";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";

defineProps({
  title: { type: String, required: true },
  subtitle: { type: String, default: "" },
  loading: { type: Boolean, default: false },
  welcome: { type: Boolean, default: false },
  stepIndex: { type: Number, default: 0 },
  stepCount: { type: Number, default: 1 },
  stepsAriaLabel: { type: String, default: "" },
  stepLabels: { type: Array, default: () => [] },
  showBack: { type: Boolean, default: false },
  backLabel: { type: String, default: "" },
  continueLabel: { type: String, default: "" },
  continueDisabled: { type: Boolean, default: false },
  continueLoading: { type: Boolean, default: false },
  skipLabel: { type: String, default: "" },
  skipDisabled: { type: Boolean, default: false },
  stepKey: { type: String, default: "" },
  formId: { type: String, default: "" },
});

const emit = defineEmits(["back", "continue", "skip"]);
</script>

<template>
  <main class="setup-shell">
    <header class="setup-chrome">
      <div class="setup-chrome-spacer" aria-hidden="true" />
      <ol
        class="setup-dots"
        :class="{ 'setup-dots--hidden': welcome }"
        :aria-label="stepsAriaLabel"
      >
        <li v-for="index in stepCount" :key="index">
          <span
            class="setup-dot"
            :class="{
              'setup-dot--current': index - 1 === stepIndex,
              'setup-dot--done': index - 1 < stepIndex,
            }"
            :aria-current="index - 1 === stepIndex ? 'step' : undefined"
          >
            <span class="sr-only">
              {{ stepLabels[index - 1] || index }}
            </span>
          </span>
        </li>
      </ol>
      <div class="setup-lang">
        <LanguageSwitcher variant="corner" />
      </div>
    </header>

    <div class="setup-main">
      <div v-if="loading" class="setup-loading" aria-busy="true">
        <Spinner size="2xl" class="text-muted-foreground" />
      </div>

      <Transition
        v-else
        name="setup-step"
        mode="out-in"
        :duration="{ enter: 180, leave: 0 }"
      >
        <div :key="stepKey || title" class="setup-panel">
          <header
            class="setup-heading"
            :class="{ 'setup-heading--welcome': stepKey === 'welcome' }"
          >
            <Logo
              v-if="stepKey === 'welcome' && brandLogo"
              class="setup-logo"
            />
            <h1 class="setup-title">{{ title }}</h1>
            <p v-if="subtitle" class="setup-subtitle">{{ subtitle }}</p>
          </header>
          <div
            v-if="stepKey !== 'welcome' && stepKey !== 'done'"
            class="setup-body"
          >
            <slot />
          </div>
        </div>
      </Transition>
    </div>

    <footer v-if="!loading" class="setup-footer-wrap">
      <div class="setup-footer">
        <Button
          v-if="showBack"
          type="button"
          variant="ghost"
          class="setup-press"
          :disabled="continueLoading"
          @click="emit('back')"
        >
          {{ backLabel }}
        </Button>
        <span v-else class="setup-footer-spacer" />

        <div class="setup-footer-actions">
          <Button
            v-if="skipLabel"
            type="button"
            variant="ghost"
            class="setup-press"
            :disabled="skipDisabled || continueLoading"
            @click="emit('skip')"
          >
            {{ skipLabel }}
          </Button>
          <Button
            :type="formId ? 'submit' : 'button'"
            :form="formId || undefined"
            size="lg"
            class="setup-continue setup-press"
            :disabled="continueDisabled || continueLoading"
            @click="formId ? undefined : emit('continue')"
          >
            <Spinner v-if="continueLoading" />
            {{ continueLabel }}
          </Button>
        </div>
      </div>
    </footer>
  </main>
</template>

<style scoped>
.setup-shell {
  min-height: 100dvh;
  display: grid;
  grid-template-rows: auto 1fr auto;
  background: var(--sidebar);
  color: var(--foreground);
}

.setup-chrome {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  column-gap: 0.75rem;
  padding: 1.25rem 1.5rem 0.5rem;
}

.setup-chrome-spacer {
  min-height: 2.25rem;
}

.setup-lang {
  justify-self: end;
}

.setup-dots {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.setup-dot {
  display: block;
  width: 0.4375rem;
  height: 0.4375rem;
  border-radius: 999px;
  background: color-mix(in oklch, var(--foreground) 16%, transparent);
  transition:
    background-color 200ms cubic-bezier(0.16, 1, 0.3, 1),
    transform 200ms cubic-bezier(0.16, 1, 0.3, 1);
}

.setup-dot--done {
  background: color-mix(in oklch, var(--foreground) 38%, transparent);
}

.setup-dot--current {
  background: var(--foreground);
  transform: scale(1.15);
}

.setup-dots--hidden {
  visibility: hidden;
}

.setup-main {
  display: flex;
  justify-content: center;
  align-items: flex-start;
  overflow: auto;
  padding: 6vh 1.5rem 1.5rem;
}

.setup-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 16rem;
}

.setup-panel {
  width: 100%;
  max-width: 26rem;
  display: flex;
  flex-direction: column;
  gap: 1.75rem;
}

.setup-heading {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  text-align: left;
}

.setup-heading--welcome {
  align-items: center;
  text-align: center;
  gap: 0.75rem;
}

.setup-logo {
  width: 3.25rem !important;
  height: auto;
  margin-bottom: 0.25rem;
  opacity: 1 !important;
}

.setup-title {
  font-family: var(--font-display);
  font-size: 1.75rem;
  font-weight: 650;
  letter-spacing: -0.032em;
  line-height: 1.12;
  color: var(--foreground);
}

.setup-heading--welcome .setup-title {
  font-size: 2.375rem;
  letter-spacing: -0.038em;
  line-height: 1.05;
}

.setup-subtitle {
  max-width: 42ch;
  font-size: 1.0625rem;
  line-height: 1.45;
  letter-spacing: -0.011em;
  color: var(--muted-foreground);
}

.setup-heading--welcome .setup-subtitle {
  max-width: 40ch;
}

.setup-body {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.setup-footer-wrap {
  padding: 0.75rem 1.5rem 1.75rem;
}

.setup-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  width: 100%;
  max-width: 26rem;
  margin: 0 auto;
}

.setup-footer-spacer {
  min-width: 4.5rem;
}

.setup-footer-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.5rem;
  margin-left: auto;
}

.setup-continue {
  min-width: 10rem;
}

.setup-press {
  transition: transform 100ms cubic-bezier(0.16, 1, 0.3, 1);
}

.setup-press:active:not(:disabled) {
  transform: scale(0.97);
}

.setup-step-leave-active {
  transition: none;
  opacity: 0;
}

.setup-step-enter-active {
  transition: opacity 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.setup-step-enter-from,
.setup-step-leave-from,
.setup-step-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: reduce) {
  .setup-dot,
  .setup-press,
  .setup-step-enter-active,
  .setup-step-leave-active {
    transition: none;
  }

  .setup-step-enter-from,
  .setup-step-leave-to {
    opacity: 0;
  }

  .setup-press:active:not(:disabled) {
    transform: none;
  }
}

@media (max-width: 639px) {
  .setup-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .setup-footer-actions {
    order: -1;
    width: 100%;
    margin-left: 0;
    flex-direction: column;
  }

  .setup-continue,
  .setup-footer-actions :deep([data-slot="button"]) {
    width: 100%;
  }

  .setup-footer-spacer {
    display: none;
  }

  .setup-title {
    font-size: 1.875rem;
  }

  .setup-heading--welcome .setup-title {
    font-size: 2.375rem;
  }
}

@media (min-width: 640px) {
  .setup-title {
    font-size: 1.875rem;
  }

  .setup-heading--welcome .setup-title {
    font-size: 2.75rem;
  }
}
</style>
