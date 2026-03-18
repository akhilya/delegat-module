<script>
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import Logo from "@/components/logo.vue";
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";

import { toTypedSchema } from "@vee-validate/zod";
import { z } from "zod";
import { useForm } from "vee-validate";
import { useStore } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useRouter, useRoute } from "vue-router";
import { ref } from "vue";
import { Loader2 } from "lucide-vue-next";
import { PAGE_SIGN_UP } from "@/router";
import { BRAND_NAME, BRAND_DESCRIPTION } from "@/brand";

export default {
  components: {
    FormControl,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
    Button,
    Input,
    Checkbox,
    Logo,
    Loader2,
  },
  setup() {
    const store = useStore();
    const router = useRouter();
    const route = useRoute();
    const isSubmitting = ref(false);

    const formSchema = toTypedSchema(
      z.object({
        userName: z.string().min(4).max(45),
        password: z.string().min(8).max(128),
        rememberMe: z.boolean().default(true).optional(),
      }),
    );

    const form = useForm({
      validationSchema: formSchema,
      initialValues: {
        rememberMe: true,
      },
    });

    const onSubmit = form.handleSubmit(async (values) => {
      isSubmitting.value = true;
      await store.dispatch("postSignInUser", {
        input: values,
        onSuccess: (response) => {
          displaySonnerSuccess("Успешный вход.");
          router.push(route.query.redirect || "/");
        },
        onError: (error) => {
          if (error?.response?.data?.errors)
            form.setErrors(error.response.data.errors);
          displaySonnerError(error);
        },
      });
      isSubmitting.value = false;
    });

    return {
      form,
      onSubmit,
      isSubmitting,
      PAGE_SIGN_UP,
      BRAND_NAME,
      BRAND_DESCRIPTION,
    };
  },
};
</script>

<template>
  <div class="auth-layout">
    <!-- Brand panel — desktop only -->
    <aside class="auth-brand">
      <div class="auth-brand-inner">
        <Logo class="auth-brand-logo" />
        <h2 class="auth-brand-title">{{ BRAND_NAME }}</h2>
        <p class="auth-brand-desc">{{ BRAND_DESCRIPTION }}</p>
      </div>
      <div class="auth-dot-pattern" aria-hidden="true"></div>
    </aside>

    <!-- Form side -->
    <main class="auth-main">
      <div class="auth-form-wrap">
        <!-- Mobile logo -->
        <div class="auth-mobile-logo">
          <Logo class="!w-14 !opacity-100" />
        </div>

        <header class="auth-header">
          <h1 class="auth-title">Вход</h1>
          <p class="auth-subtitle">Войдите в панель управления</p>
        </header>

        <form @submit="onSubmit" class="auth-form">
          <FormField v-slot="{ componentField }" name="userName">
            <FormItem>
              <FormLabel>Имя пользователя</FormLabel>
              <FormControl>
                <Input
                  type="text"
                  placeholder="Имя пользователя"
                  autocomplete="username"
                  v-bind="componentField"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

          <FormField v-slot="{ componentField }" name="password">
            <FormItem>
              <FormLabel>Пароль</FormLabel>
              <FormControl>
                <Input
                  type="password"
                  autocomplete="current-password"
                  v-bind="componentField"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

          <FormField
            v-slot="{ value, handleChange }"
            type="checkbox"
            name="rememberMe"
          >
            <FormItem class="flex flex-row items-center gap-2">
              <FormControl>
                <Checkbox
                  :model-value="value"
                  @update:model-value="handleChange"
                />
              </FormControl>
              <FormLabel class="!mt-0">Запомнить меня</FormLabel>
              <FormMessage />
            </FormItem>
          </FormField>

          <Button type="submit" size="lg" class="w-full mt-2" :disabled="isSubmitting">
            <span v-if="!isSubmitting">Войти</span>
            <Loader2 v-else class="animate-spin" />
          </Button>
        </form>

        <p class="auth-footer">
          Нет аккаунта?
          <RouterLink :to="PAGE_SIGN_UP" class="auth-link">
            Создать
          </RouterLink>
        </p>
      </div>
    </main>
  </div>
</template>

<style scoped>
/* ── Layout ── */
.auth-layout {
  display: flex;
  min-height: 100vh;
  background: var(--background);
}

.auth-brand {
  display: none;
  position: relative;
  width: 42%;
  min-width: 340px;
  background: oklch(0.16 0.01 250);
  overflow: hidden;
}
@media (min-width: 1024px) {
  .auth-brand {
    display: flex;
    align-items: center;
    justify-content: center;
  }
}

.auth-brand-inner {
  position: relative;
  z-index: 1;
  padding: 3rem;
  text-align: center;
}

.auth-brand-logo {
  width: 5rem !important;
  opacity: 1 !important;
  filter: brightness(0) invert(1);
  margin: 0 auto 1.5rem;
}

.auth-brand-title {
  font-family: var(--font-display);
  font-size: 2rem;
  font-weight: 700;
  color: oklch(0.96 0 0);
  letter-spacing: -0.025em;
  margin-bottom: 0.75rem;
}

.auth-brand-desc {
  font-size: 0.9375rem;
  line-height: 1.6;
  color: oklch(0.55 0 0);
  max-width: 260px;
  margin: 0 auto;
}

/* Halftone dot grid — print aesthetic */
.auth-dot-pattern {
  position: absolute;
  inset: 0;
  opacity: 0.05;
  background-image: radial-gradient(circle, oklch(0.9 0 0) 1px, transparent 1px);
  background-size: 20px 20px;
}

/* ── Form side ── */
.auth-main {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2rem 1.5rem;
}

.auth-form-wrap {
  width: 100%;
  max-width: 380px;
  animation: auth-enter 0.5s cubic-bezier(0.16, 1, 0.3, 1) both;
}

@keyframes auth-enter {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.auth-mobile-logo {
  display: flex;
  justify-content: center;
  margin-bottom: 2rem;
}
@media (min-width: 1024px) {
  .auth-mobile-logo {
    display: none;
  }
}

/* ── Typography ── */
.auth-header {
  margin-bottom: 2rem;
}

.auth-title {
  font-family: var(--font-display);
  font-size: 1.875rem;
  font-weight: 700;
  color: var(--foreground);
  letter-spacing: -0.025em;
  line-height: 1.15;
}

.auth-subtitle {
  font-size: 0.9375rem;
  color: var(--muted-foreground);
  margin-top: 0.5rem;
  line-height: 1.5;
}

/* ── Form ── */
.auth-form {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

/* ── Footer ── */
.auth-footer {
  text-align: center;
  font-size: 0.875rem;
  color: var(--muted-foreground);
  margin-top: 2rem;
}

.auth-link {
  color: var(--foreground);
  font-weight: 500;
  text-decoration: none;
}
.auth-link:hover {
  text-decoration: underline;
}

/* Reduced motion */
@media (prefers-reduced-motion: reduce) {
  .auth-form-wrap {
    animation: none;
  }
}
</style>
