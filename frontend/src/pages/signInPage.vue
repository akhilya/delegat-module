<script>
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import AuthLayout from "@/components/auth/AuthLayout.vue";
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
import { useI18n } from "vue-i18n";

export default {
  components: {
    AuthLayout,
    FormControl,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
    Button,
    Input,
    PasswordInput,
    Checkbox,
    Loader2,
  },
  setup() {
    const { t } = useI18n();
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
        onSuccess: () => {
          displaySonnerSuccess(t("auth.signInSuccess"));
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
      t,
    };
  },
};
</script>

<template>
  <AuthLayout :title="t('auth.signIn')">
    <form class="auth-form" @submit="onSubmit">
      <FormField v-slot="{ componentField }" name="userName">
        <FormItem>
          <FormLabel>{{ t("auth.userName") }}</FormLabel>
          <FormControl>
            <Input
              type="text"
              :placeholder="t('auth.userName')"
              autocomplete="username"
              v-bind="componentField"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="password">
        <FormItem>
          <FormLabel>{{ t("auth.password") }}</FormLabel>
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
        <FormItem class="flex flex-row items-center gap-2 space-y-0">
          <FormControl>
            <Checkbox
              :model-value="value"
              @update:model-value="handleChange"
            />
          </FormControl>
          <FormLabel class="!mt-0 text-sm font-normal">
            {{ t("auth.rememberMe") }}
          </FormLabel>
          <FormMessage />
        </FormItem>
      </FormField>

      <Button type="submit" class="w-full" :disabled="isSubmitting">
        <Loader2 v-if="isSubmitting" class="animate-spin" />
        <span v-else>{{ t("auth.signIn") }}</span>
      </Button>
    </form>

    <template #footer>
      {{ t("auth.noAccount") }}
      <RouterLink :to="PAGE_SIGN_UP">{{ t("auth.signUp") }}</RouterLink>
    </template>
  </AuthLayout>
</template>

<style scoped>
.auth-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
</style>
