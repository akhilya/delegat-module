<script setup>
import Logo from "@/components/logo.vue";
import LanguageSwitcher from "@/components/LanguageSwitcher.vue";
import { Card, CardContent } from "@/components/ui/card";
import { BRAND_NAME } from "@/brand";

defineProps({
  /** Page action under the brand (e.g. Sign in / Sign up) */
  title: { type: String, required: true },
});
</script>

<template>
  <main class="auth-shell">
    <div class="auth-lang">
      <LanguageSwitcher variant="corner" />
    </div>

    <div class="auth-center">
      <header class="auth-brand">
        <Logo class="auth-logo" />
        <h1 class="auth-brand-name">{{ BRAND_NAME }}</h1>
        <p class="auth-title">{{ title }}</p>
      </header>

      <Card class="auth-card gap-0 py-6">
        <CardContent class="auth-card-body">
          <slot />
        </CardContent>
      </Card>

      <p v-if="$slots.footer" class="auth-footer">
        <slot name="footer" />
      </p>
    </div>
  </main>
</template>

<style scoped>
.auth-shell {
  position: relative;
  min-height: 100dvh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1.5rem;
  background: var(--sidebar);
  color: var(--foreground);
}

.auth-lang {
  position: absolute;
  top: 1rem;
  right: 1rem;
}

.auth-center {
  width: 100%;
  max-width: 22.5rem;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 1.5rem;
  animation: auth-enter 280ms ease-out both;
}

.auth-brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 0.375rem;
}

.auth-logo {
  width: 2.75rem !important;
  height: auto;
  opacity: 1 !important;
  margin-bottom: 0.375rem;
}

.auth-brand-name {
  font-family: var(--font-display);
  font-size: 1.5rem;
  font-weight: 650;
  letter-spacing: -0.03em;
  line-height: 1.15;
  color: var(--foreground);
}

.auth-title {
  font-size: 0.875rem;
  line-height: 1.35;
  color: var(--muted-foreground);
}

.auth-card-body {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.auth-footer {
  text-align: center;
  font-size: 0.875rem;
  color: var(--muted-foreground);
  line-height: 1.4;
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
    transform: translateY(6px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .auth-center {
    animation: none;
  }
}
</style>
