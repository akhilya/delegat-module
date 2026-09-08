<script setup>
import LanguageSwitcher from "@/components/LanguageSwitcher.vue";

defineProps({
  /** Large page title (Sign in / Create your account). */
  title: { type: String, required: true },
  subtitle: { type: String, default: "" },
  /** center: short screens (sign-in). start: taller forms (sign-up). */
  align: {
    type: String,
    default: "center",
    validator: (v) => ["center", "start"].includes(v),
  },
});
</script>

<template>
  <main class="auth-shell">
    <header class="auth-chrome">
      <div class="auth-chrome-spacer" aria-hidden="true" />
      <div class="auth-lang">
        <LanguageSwitcher variant="corner" />
      </div>
    </header>

    <div class="auth-main" :class="{ 'auth-main--start': align === 'start' }">
      <div class="auth-panel">
        <header class="auth-heading">
          <h1 class="auth-title">{{ title }}</h1>
          <p v-if="subtitle" class="auth-subtitle">{{ subtitle }}</p>
        </header>
        <div class="auth-body">
          <slot />
        </div>
      </div>
    </div>

    <footer v-if="$slots.footer" class="auth-footer-wrap">
      <p class="auth-footer">
        <slot name="footer" />
      </p>
    </footer>
  </main>
</template>

<style scoped>
.auth-shell {
  min-height: 100dvh;
  display: grid;
  grid-template-rows: auto 1fr auto;
  background: var(--sidebar);
  color: var(--foreground);
}

.auth-chrome {
  display: grid;
  grid-template-columns: 1fr auto;
  align-items: center;
  padding: 1.25rem 1.5rem 0.5rem;
}

.auth-chrome-spacer {
  min-height: 2.25rem;
}

.auth-lang {
  justify-self: end;
}

.auth-main {
  display: flex;
  justify-content: center;
  align-items: center;
  overflow: auto;
  padding: 2vh 1.5rem 1.5rem;
}

.auth-main--start {
  align-items: flex-start;
  padding-top: 6vh;
}

.auth-panel {
  width: 100%;
  max-width: 26rem;
  display: flex;
  flex-direction: column;
  gap: 1.75rem;
  animation: auth-enter var(--duration-drawer) var(--ease-out-quart) both;
}

.auth-heading {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 0.75rem;
}

.auth-title {
  font-family: var(--font-display);
  font-size: 2.375rem;
  font-weight: 650;
  letter-spacing: -0.038em;
  line-height: 1.05;
  color: var(--foreground);
}

.auth-subtitle {
  max-width: 40ch;
  font-size: 1.0625rem;
  line-height: 1.45;
  letter-spacing: -0.011em;
  color: var(--muted-foreground);
}

.auth-body {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.auth-body :deep(.auth-submit) {
  min-width: 10rem;
  transition: transform 100ms cubic-bezier(0.16, 1, 0.3, 1);
}

.auth-body :deep(.auth-submit:active:not(:disabled)) {
  transform: scale(0.97);
}

.auth-footer-wrap {
  padding: 0.75rem 1.5rem 1.75rem;
}

.auth-footer {
  width: 100%;
  max-width: 26rem;
  margin: 0 auto;
  text-align: center;
  font-size: 0.9375rem;
  line-height: 1.45;
  color: var(--muted-foreground);
}

.auth-footer :deep(a) {
  color: var(--foreground);
  font-weight: 500;
  text-decoration: none;
}

.auth-footer :deep(a:hover) {
  text-decoration: underline;
  text-underline-offset: 0.15em;
}

@keyframes auth-enter {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .auth-panel {
    animation: none;
  }

  .auth-body :deep(.auth-submit:active:not(:disabled)) {
    transform: none;
  }
}

@media (max-width: 639px) {
  .auth-body :deep(.auth-submit) {
    width: 100%;
  }
}

@media (min-width: 640px) {
  .auth-title {
    font-size: 2.75rem;
  }
}
</style>
