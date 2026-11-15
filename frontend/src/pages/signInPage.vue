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

import { toTypedSchema } from "../lib/zodSchema";
import { z } from "zod";
import { useForm } from "vee-validate";
import { useStore } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useRouter, useRoute } from "vue-router";
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from "vue";

import { PAGE_SIGN_UP } from "@/router";
import { useI18n } from "vue-i18n";
import {
  passwordSchema,
  translateApiFieldErrors,
  usernameSchema,
} from "@/lib/authValidation";
import { FIELD_LIMITS } from "@/lib/fieldLimits";
import {
  readSignInQuery,
  stripSignInSecretsFromQuery,
} from "@/lib/signInQuery";
import { http } from "@/main";
import { assertPasskey, mapWebAuthnError } from "@/lib/webauthn";
import { Fingerprint } from "lucide-vue-next";

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
    Fingerprint,
  },
  setup() {
    const { t } = useI18n();
    const store = useStore();
    const router = useRouter();
    const route = useRoute();
    const isSubmitting = ref(false);
    const formEl = ref(null);
    const queryAutoSubmitStarted = ref(false);
    const step = ref("password");
    const loginTicket = ref("");
    const totpCode = ref("");
    const recoveryCode = ref("");
    const recoveryMode = ref(false);
    const passkeyBusy = ref(false);
    let conditionalStopped = false;

    const authTitle = computed(() => {
      if (step.value === "password") return t("auth.signIn");
      return recoveryMode.value ? t("auth.recoveryTitle") : t("auth.totpTitle");
    });
    const authSubtitle = computed(() => {
      if (step.value === "password") return t("auth.signInBody");
      return recoveryMode.value ? t("auth.recoveryBody") : t("auth.totpBody");
    });

    const signedIn = () => {
      displaySonnerSuccess(t("auth.signInSuccess"));
      router.push(route.query.redirect || "/");
    };

    const formSchema = toTypedSchema(
      z.object({
        userName: usernameSchema(t),
        password: passwordSchema(t, { minLength: 1 }),
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
        onSuccess: signedIn,
        onTwoFactor: (body) => {
          conditionalStopped = true;
          loginTicket.value = body.loginTicket || "";
          totpCode.value = "";
          recoveryCode.value = "";
          recoveryMode.value = false;
          step.value = "totp";
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

    const finishSecondFactor = (action, input) =>
      store.dispatch(action, {
        input,
        onSuccess: signedIn,
        onError: (error) => displaySonnerError(error),
      });

    const onTotpSubmit = async () => {
      isSubmitting.value = true;
      await finishSecondFactor("postSignInTotp", {
        loginTicket: loginTicket.value,
        code: totpCode.value,
        rememberMe: form.values.rememberMe,
      });
      isSubmitting.value = false;
    };

    const onRecoverySubmit = async () => {
      isSubmitting.value = true;
      await finishSecondFactor("postSignInRecovery", {
        loginTicket: loginTicket.value,
        code: recoveryCode.value,
        rememberMe: form.values.rememberMe,
      });
      isSubmitting.value = false;
    };

    const onPasskeySignIn = async () => {
      passkeyBusy.value = true;
      try {
        const optionsRes = await http.post(
          "/auth/signIn/passkey/options",
          { userName: form.values.userName || "" },
          { cache: false },
        );
        const options = optionsRes.data?.value ?? optionsRes.data;
        const assertion = await assertPasskey(options);
        await store.dispatch("postSignInPasskey", {
          assertion,
          rememberMe: form.values.rememberMe,
          onSuccess: signedIn,
          onError: (error) => displaySonnerError(error),
        });
      } catch (error) {
        const mapped = mapWebAuthnError(error);
        if (mapped === "canceled") {
          displaySonnerError(t("auth.passkeyCanceled"));
        } else {
          displaySonnerError(error?.response ? error : t("auth.passkeyFailed"));
        }
      } finally {
        passkeyBusy.value = false;
      }
    };

    async function startConditionalPasskey() {
      try {
        const optionsRes = await http.post(
          "/auth/signIn/passkey/options",
          { userName: "" },
          { cache: false },
        );
        const options = optionsRes.data?.value ?? optionsRes.data;
        const assertion = await assertPasskey(options, { useBrowserAutofill: true });
        if (conditionalStopped || step.value !== "password") return;
        await store.dispatch("postSignInPasskey", {
          assertion,
          rememberMe: form.values.rememberMe,
          onSuccess: signedIn,
          onError: (error) => displaySonnerError(error),
        });
      } catch {
        // Conditional UI is optional.
      }
    }

    const backToPassword = () => {
      step.value = "password";
      loginTicket.value = "";
      totpCode.value = "";
      recoveryCode.value = "";
      recoveryMode.value = false;
      conditionalStopped = false;
      startConditionalPasskey();
    };

    onMounted(async () => {
      startConditionalPasskey();
      if (route.query.ssoError) {
        displaySonnerError(t("auth.ssoFailed"));
        await router.replace({ query: {} });
        return;
      }
      const creds = readSignInQuery(route.query);
      if (!creds || queryAutoSubmitStarted.value) return;
      queryAutoSubmitStarted.value = true;

      form.setFieldValue("userName", creds.username, false);
      form.setFieldValue("password", creds.password, false);

      await router.replace({
        query: stripSignInSecretsFromQuery(route.query),
      });

      await nextTick();
      await nextTick();
      formEl.value?.querySelector('button[type="submit"]')?.click();
    });

    onBeforeUnmount(() => {
      conditionalStopped = true;
    });

    return {
      form,
      formEl,
      onSubmit,
      onTotpSubmit,
      onRecoverySubmit,
      onPasskeySignIn,
      backToPassword,
      authTitle,
      authSubtitle,
      isSubmitting,
      passkeyBusy,
      step,
      totpCode,
      recoveryCode,
      recoveryMode,
      PAGE_SIGN_UP,
      t,
      FIELD_LIMITS,
    };
  },
};
</script>

<template>
  <AuthLayout :title="authTitle" :subtitle="authSubtitle">
    <form v-if="step === 'password'" ref="formEl" @submit="onSubmit">
      <FieldGroup class="gap-5">
        <FormField v-slot="{ componentField, errorMessage }" name="userName">
          <SettingsFormField
            :label="t('auth.userName')"
            label-for="sign-in-username"
            :error="errorMessage"
          >
            <Input
              id="sign-in-username"
              type="text"
              autocomplete="username webauthn"
              :maxlength="FIELD_LIMITS.userName"
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
              :maxlength="FIELD_LIMITS.password"
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

        <Button
          type="submit"
          size="lg"
          class="auth-submit w-full"
          :disabled="isSubmitting || passkeyBusy"
        >
          <Spinner v-if="isSubmitting" />
          <span v-else>{{ t("auth.signIn") }}</span>
        </Button>
        <Button
          type="button"
          variant="outline"
          size="lg"
          class="w-full"
          :disabled="isSubmitting || passkeyBusy"
          @click="onPasskeySignIn"
        >
          <Spinner v-if="passkeyBusy" />
          <template v-else>
            <Fingerprint class="mr-2 size-4" />
            {{ t("auth.passkeySignIn") }}
          </template>
        </Button>
      </FieldGroup>
    </form>

    <form v-else-if="!recoveryMode" @submit.prevent="onTotpSubmit">
      <FieldGroup class="gap-5">
        <SettingsFormField :label="t('auth.totpCode')" label-for="sign-in-totp">
          <Input
            id="sign-in-totp"
            v-model="totpCode"
            inputmode="numeric"
            autocomplete="one-time-code"
            maxlength="6"
            pattern="[0-9]*"
          />
        </SettingsFormField>
        <Button
          type="submit"
          size="lg"
          class="auth-submit w-full"
          :disabled="isSubmitting || totpCode.length !== 6"
        >
          <Spinner v-if="isSubmitting" />
          <span v-else>{{ t("auth.totpSubmit") }}</span>
        </Button>
        <Button type="button" variant="ghost" class="w-full" @click="recoveryMode = true">
          {{ t("auth.recoveryToggle") }}
        </Button>
        <Button type="button" variant="ghost" class="w-full" @click="backToPassword">
          {{ t("auth.totpBack") }}
        </Button>
      </FieldGroup>
    </form>

    <form v-else @submit.prevent="onRecoverySubmit">
      <FieldGroup class="gap-5">
        <SettingsFormField :label="t('auth.recoveryCode')" label-for="sign-in-recovery">
          <Input
            id="sign-in-recovery"
            v-model="recoveryCode"
            autocomplete="off"
            maxlength="16"
          />
        </SettingsFormField>
        <Button
          type="submit"
          size="lg"
          class="auth-submit w-full"
          :disabled="isSubmitting || !recoveryCode"
        >
          <Spinner v-if="isSubmitting" />
          <span v-else>{{ t("auth.totpSubmit") }}</span>
        </Button>
        <Button type="button" variant="ghost" class="w-full" @click="recoveryMode = false">
          {{ t("auth.recoveryBack") }}
        </Button>
        <Button type="button" variant="ghost" class="w-full" @click="backToPassword">
          {{ t("auth.totpBack") }}
        </Button>
      </FieldGroup>
    </form>

    <template #footer>
      {{ t("auth.noAccount") }}
      <RouterLink :to="PAGE_SIGN_UP">{{ t("auth.signUp") }}</RouterLink>
    </template>
  </AuthLayout>
</template>
