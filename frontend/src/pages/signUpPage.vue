<script>
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import AuthLayout from "@/components/auth/AuthLayout.vue";
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

import { useI18n } from "vue-i18n";

export default {
  components: {
    Spinner,
    AuthLayout,
    FormControl,
    FormDescription,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
    Button,
    Input,
    PasswordInput
  },
  setup() {
    const { t } = useI18n();
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
          message: t("auth.passwordsMismatch"),
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
          displaySonnerSuccess(t("auth.signUpSuccess"));
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
      t,
    };
  },
};
</script>

<template>
  <AuthLayout :title="t('auth.signUp')">
    <form class="auth-form" @submit="onSubmit">
      <FormField v-slot="{ componentField }" name="userName">
        <FormItem>
          <FormLabel>{{ t("auth.userName") }}</FormLabel>
          <FormControl>
            <Input
              type="text"
              :placeholder="t('auth.userNamePlaceholder')"
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
              autocomplete="new-password"
              v-bind="componentField"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="confirmPassword">
        <FormItem>
          <FormLabel>{{ t("auth.confirmPassword") }}</FormLabel>
          <FormControl>
            <PasswordInput
              autocomplete="new-password"
              v-bind="componentField"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="invitationToken">
        <FormItem>
          <FormLabel>{{ t("auth.invitationToken") }}</FormLabel>
          <FormControl>
            <Input
              type="text"
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              autocomplete="off"
              v-bind="componentField"
            />
          </FormControl>
          <FormDescription>
            {{ t("auth.invitationTokenHint") }}
          </FormDescription>
          <FormMessage />
        </FormItem>
      </FormField>

      <Button type="submit" class="w-full" :disabled="isSubmitting">
        <Spinner v-if="isSubmitting" />
        <span v-else>{{ t("auth.createAccount") }}</span>
      </Button>
    </form>

    <template #footer>
      {{ t("auth.haveAccount") }}
      <RouterLink :to="PAGE_SIGN_IN">{{ t("auth.signIn") }}</RouterLink>
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
