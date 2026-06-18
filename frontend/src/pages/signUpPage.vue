<script>
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Logo from "@/components/logo.vue";
import {
  FormControl,
  FormDescription,
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
import { PAGE_SIGN_IN } from "@/router";
import { ref } from "vue";
import { Loader2 } from "lucide-vue-next";
import { BRAND_NAME } from "@/brand";

export default {
  components: {
    FormControl,
    FormDescription,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
    Button,
    Input,
    Logo,
    Loader2,
  },
  setup() {
    const store = useStore();
    const router = useRouter();
    const route = useRoute();
    const isSubmitting = ref(false);

    const formSchema = toTypedSchema(
      z
        .object({
          userName: z.string().min(4).max(45),
          password: z.string().min(8).max(128),
          confirmPassword: z.string().min(8).max(128),
          invitationToken: z.string(),
        })
        .refine((data) => data.password === data.confirmPassword, {
          message: "Пароли не совпадают",
          path: ["confirmPassword"],
        }),
    );

    const form = useForm({
      validationSchema: formSchema,
      initialValues: {
        invitationToken: route.query.token || "",
      },
    });

    const onSubmit = form.handleSubmit(async (values) => {
      isSubmitting.value = true;
      await store.dispatch("postSignUpUser", {
        input: {
          userName: values.userName,
          password: values.password,
          confirmPassword: values.confirmPassword,
        },
        token: values.invitationToken,
        onSuccess: (response) => {
          displaySonnerSuccess("Успешная регистрация.");
          router.push(response.data.value.next);
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
      PAGE_SIGN_IN,
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
          <h1 class="auth-title">Регистрация</h1>
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
              <Input
                type="password"
                autocomplete="new-password"
                v-bind="componentField"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="confirmPassword">
          <FormItem>
            <FormLabel>Подтвердите пароль</FormLabel>
            <FormControl>
              <Input
                type="password"
                autocomplete="new-password"
                v-bind="componentField"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="invitationToken">
          <FormItem>
            <FormLabel>Токен приглашения</FormLabel>
            <FormControl>
              <Input
                type="text"
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                autocomplete="off"
                v-bind="componentField"
              />
            </FormControl>
            <FormDescription>
              Вставьте токен, полученный от администратора
            </FormDescription>
            <FormMessage />
          </FormItem>
        </FormField>

        <Button type="submit" class="w-full" :disabled="isSubmitting">
          <span v-if="!isSubmitting">Создать аккаунт</span>
          <Loader2 v-else class="animate-spin" />
        </Button>
      </form>

      <p class="auth-footer">
        Есть аккаунт?
        <RouterLink :to="PAGE_SIGN_IN" class="auth-link">
          Войти
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
