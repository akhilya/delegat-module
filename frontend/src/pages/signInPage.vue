<script>
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
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
import { BRAND_NAME } from "@/brand";

export default {
  components: {
    FormControl,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
    Button,
    Input,
    PasswordInput,
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
    };
  },
};
</script>

<template>
  <main class="auth-page">
    <section class="auth-form-wrap">
      <header class="auth-header">
        <Logo class="auth-logo" />
        <div>
          <p class="auth-kicker">{{ BRAND_NAME }}</p>
          <h1 class="auth-title">Вход</h1>
        </div>
      </header>

      <form @submit="onSubmit" class="auth-form">
        <FormField v-slot="{ componentField }" name="userName">
          <FormItem>
            <FormLabel>Имя пользователя</FormLabel>
            <FormControl>
              <Input
                type="text"
                placeholder="Введите имя"
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
              <PasswordInput
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
            <FormLabel class="!mt-0 text-sm font-normal">Запомнить меня</FormLabel>
            <FormMessage />
          </FormItem>
        </FormField>

        <Button type="submit" class="w-full" :disabled="isSubmitting">
          <span v-if="!isSubmitting">Войти</span>
          <Loader2 v-else class="animate-spin" />
        </Button>
      </form>

      <p class="auth-footer">
        Нет аккаунта?
        <RouterLink :to="PAGE_SIGN_UP" class="auth-link">
          Зарегистрироваться
        </RouterLink>
      </p>
    </section>
  </main>
</template>

<style scoped>
.auth-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--background);
  padding: 1.5rem;
}

.auth-form-wrap {
  width: 100%;
  max-width: 360px;
}

.auth-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1.75rem;
}

.auth-logo {
  width: 2.25rem !important;
  opacity: 1 !important;
}

.auth-kicker {
  color: var(--muted-foreground);
  font-size: 0.8125rem;
  line-height: 1;
}

.auth-title {
  font-size: 1.5rem;
  font-weight: 650;
  line-height: 1.2;
}

.auth-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.auth-footer {
  text-align: center;
  font-size: 0.875rem;
  color: var(--muted-foreground);
  margin-top: 1.5rem;
}

.auth-link {
  color: var(--foreground);
  font-weight: 500;
  text-decoration: none;
}
.auth-link:hover {
  text-decoration: underline;
}
</style>
