<script>
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import AuthLayout from "@/components/auth/AuthLayout.vue";
import { FormField } from "@/components/ui/form";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";

import { toTypedSchema } from "@vee-validate/zod";
import { z } from "zod";
import { useForm } from "vee-validate";
import { useStore } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useRouter, useRoute } from "vue-router";
import { ref } from "vue";

import { PAGE_SIGN_UP } from "@/router";
import { useI18n } from "vue-i18n";
import {
  passwordSchema,
  translateApiFieldErrors,
  usernameSchema,
} from "@/lib/authValidation";

export default {
  components: {
    Spinner,
    AuthLayout,
    FormField,
    FieldGroup,
    SettingsFormField,
    Button,
    Input,
    PasswordInput,
    Checkbox,
  },
  setup() {
    const { t } = useI18n();
    const store = useStore();
    const router = useRouter();
    const route = useRoute();
    const isSubmitting = ref(false);

    const formSchema = toTypedSchema(
      z.object({
        userName: usernameSchema(t),
        password: passwordSchema(t),
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
            form.setErrors(
              translateApiFieldErrors(error.response.data.errors, t),
            );
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
      <FieldGroup class="gap-4">
        <FormField v-slot="{ componentField, errorMessage }" name="userName">
          <SettingsFormField
            :label="t('auth.userName')"
            label-for="sign-in-username"
            :error="errorMessage"
          >
            <Input
              id="sign-in-username"
              type="text"
              :placeholder="t('auth.userName')"
              autocomplete="username"
              v-bind="componentField"
            />
          </SettingsFormField>
        </FormField>

        <FormField v-slot="{ componentField, errorMessage }" name="password">
          <SettingsFormField
            :label="t('auth.password')"
            label-for="sign-in-password"
            :error="errorMessage"
          >
            <PasswordInput
              id="sign-in-password"
              autocomplete="current-password"
              v-bind="componentField"
            />
          </SettingsFormField>
        </FormField>

        <FormField
          v-slot="{ value, handleChange, errorMessage }"
          type="checkbox"
          name="rememberMe"
        >
          <SettingsFormField
            :label="t('auth.rememberMe')"
            label-for="sign-in-remember"
            inline
            :error="errorMessage"
          >
            <Checkbox
              id="sign-in-remember"
              :model-value="value"
              @update:model-value="handleChange"
            />
          </SettingsFormField>
        </FormField>

        <Button type="submit" class="w-full" :disabled="isSubmitting">
          <Spinner v-if="isSubmitting" />
          <span v-else>{{ t("auth.signIn") }}</span>
        </Button>
      </FieldGroup>
    </form>

    <template #footer>
      {{ t("auth.noAccount") }}
      <RouterLink :to="PAGE_SIGN_UP">{{ t("auth.signUp") }}</RouterLink>
    </template>
  </AuthLayout>
</template>
