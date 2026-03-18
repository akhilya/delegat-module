<script>
import Page from "@/components/page.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
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
import { ref } from "vue";
import { Loader2 } from "lucide-vue-next";
import { PAGE_SIGN_UP } from "@/router";

export default {
  components: {
    Page,
    FormControl,
    FormDescription,
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
    };
  },
};
</script>

<template>
  <Page class="min-h-screen flex items-center justify-center bg-background">
    <div class="w-full max-w-md bg-card rounded-xl shadow-lg border p-8 sm:p-10">
      <div class="flex flex-col items-center mb-6">
        <Logo class="mb-4" />
        <h1 class="text-2xl font-semibold text-center">Вход</h1>
        <p class="text-sm text-muted-foreground text-center mt-1">
          Войдите в панель управления
        </p>
      </div>

      <form @submit="onSubmit" class="space-y-4">
        <FormField v-slot="{ componentField }" name="userName">
          <FormItem>
            <FormLabel>Имя пользователя</FormLabel>
            <FormControl>
              <Input type="text" placeholder="Олег" v-bind="componentField" />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="password">
          <FormItem>
            <FormLabel>Пароль</FormLabel>
            <FormControl>
              <Input type="password" v-bind="componentField" />
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

        <Button type="submit" size="lg" class="w-full mt-6" :disabled="isSubmitting">
          <p v-if="!isSubmitting">Войти</p>
          <Loader2 v-else class="animate-spin" />
        </Button>
      </form>

      <p class="text-center text-sm text-muted-foreground mt-4">
        Нету аккаунта?
        <RouterLink :to="PAGE_SIGN_UP" class="text-foreground hover:underline">
          Создать.
        </RouterLink>
      </p>
    </div>
  </Page>
</template>
