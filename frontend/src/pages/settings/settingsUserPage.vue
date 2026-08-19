<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed, onMounted, ref } from "vue";
import { useStore } from "vuex";
import PageSection from "@/components/pageSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import { FieldGroup } from "@/components/ui/field";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import { http, loadMe } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { hasPermission, PERMS } from "@/lib/permissions";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import { newPasswordFieldsIssue } from "@/lib/authValidation";
import { Activity, Save } from "lucide-vue-next";

import { t } from "@/i18n";

const store = useStore();

const me = ref(null);
const isLoading = ref(false);
const isSavingProfile = ref(false);
const isChangingPassword = ref(false);
const userNameDraft = ref("");
const currentPassword = ref("");
const newPassword = ref("");
const confirmPassword = ref("");

const passwordFieldIssue = computed(() => {
  if (!newPassword.value && !confirmPassword.value) return null;
  return newPasswordFieldsIssue(newPassword.value, confirmPassword.value, t);
});

const canSubmitPassword = computed(
  () =>
    Boolean(
      currentPassword.value &&
        newPassword.value &&
        confirmPassword.value &&
        !passwordFieldIssue.value,
    ),
);

const hasProfileChanges = computed(() =>
  userNameDraft.value.trim() !== (me.value?.userName || ""),
);

const canManageOperations = computed(() =>
  hasPermission(me.value, PERMS.OperationsManage),
);

const autoOpenOperationsPanel = computed({
  get: () => store.state.operations.autoOpenPanel !== false,
  set: (value) => store.dispatch("operations/setAutoOpenPanel", value),
});

function toggleAutoOpenOperations() {
  autoOpenOperationsPanel.value = !autoOpenOperationsPanel.value;
}

async function fetchMe() {
  isLoading.value = true;
  try {
    const profile = await loadMe();
    me.value = profile;
    userNameDraft.value = profile?.userName || "";
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isLoading.value = false;
  }
}

async function saveProfile() {
  const userName = userNameDraft.value.trim();
  const nameIssue = fieldLimitIssue(userName, FIELD_LIMITS.userNameStored);
  if (nameIssue === "empty") {
    displaySonnerError(t("pages.settingsUserPage.userNameEmptyError"));
    return;
  }
  if (nameIssue === "tooLong") {
    displaySonnerError(t("common.tooLong", { max: FIELD_LIMITS.userNameStored }));
    return;
  }

  isSavingProfile.value = true;
  try {
    const response = await http.patch("/users/me", { userName }, { cache: false });
    const updated = response.data?.value || response.data;
    me.value = { ...me.value, ...updated };
    userNameDraft.value = me.value?.userName || "";
    displaySonnerSuccess(t("pages.settingsUserPage.profileUpdated"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isSavingProfile.value = false;
  }
}

async function changePassword() {
  const issue = newPasswordFieldsIssue(
    newPassword.value,
    confirmPassword.value,
    t,
  );
  if (issue) {
    if (issue.password) displaySonnerError(issue.password);
    else if (issue.confirm) displaySonnerError(issue.confirm);
    return;
  }

  isChangingPassword.value = true;
  try {
    const response = await http.patch("/users/me", {
      currentPassword: currentPassword.value,
      password: newPassword.value,
    }, { cache: false });
    const updated = response.data?.value || response.data;
    me.value = { ...me.value, ...updated };
    currentPassword.value = "";
    newPassword.value = "";
    confirmPassword.value = "";
    displaySonnerSuccess(t("pages.settingsUserPage.passwordUpdated"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isChangingPassword.value = false;
  }
}

onMounted(fetchMe);
</script>

<template>
  <div class="flex w-full max-w-xl flex-col gap-8 pb-10">
    <PageSection :loading="isLoading">
      <div class="flex flex-col gap-8">
      <section>
        <InsetGroupLabel>{{ $t("pages.settingsUserPage.profileTitle") }}</InsetGroupLabel>
        <p class="mb-2 px-1 text-xs text-muted-foreground">
          {{ $t("pages.settingsUserPage.profileDescription") }}
        </p>
        <form id="profile-form" @submit.prevent="saveProfile">
          <FieldGroup class="gap-5">
            <SettingsFormField :label="$t('pages.settingsUserPage.userNameLabel')" label-for="user-name">
              <Input
                id="user-name"
                v-model="userNameDraft"
                :disabled="isSavingProfile"
                :maxlength="FIELD_LIMITS.userNameStored"
              />
            </SettingsFormField>
          </FieldGroup>
          <div class="mt-2 flex justify-end">
            <Button
              type="submit"
              size="sm"
              :disabled="isSavingProfile || !hasProfileChanges"
            >
              <Spinner v-if="isSavingProfile" class="mr-2" />
              <Save v-else class="mr-2 size-4" />
              {{ $t("pages.settingsUserPage.saveProfileButton") }}
            </Button>
          </div>
        </form>
      </section>

      <section v-if="canManageOperations">
        <InsetGroupLabel>{{ $t("pages.settingsUserPage.operationsTitle") }}</InsetGroupLabel>
        <InsetGroup>
          <InsetGroupItem
            :aria-pressed="autoOpenOperationsPanel"
            @click="toggleAutoOpenOperations"
          >
            <template #leading>
              <Activity class="size-4" />
            </template>
            <p>{{ $t("pages.settingsUserPage.autoOpenOperationsLabel") }}</p>
            <p class="mt-0.5 text-xs font-normal text-muted-foreground">
              {{ $t("pages.settingsUserPage.autoOpenOperationsHint") }}
            </p>
            <template #trailing>
              <Switch
                :model-value="autoOpenOperationsPanel"
                tabindex="-1"
                class="pointer-events-none"
              />
            </template>
          </InsetGroupItem>
        </InsetGroup>
      </section>

      <section>
        <InsetGroupLabel>{{ $t("pages.settingsUserPage.passwordTitle") }}</InsetGroupLabel>
        <p class="mb-2 px-1 text-xs text-muted-foreground">
          {{ $t("pages.settingsUserPage.passwordDescription") }}
        </p>
        <form id="password-form" @submit.prevent="changePassword">
          <FieldGroup class="gap-5">
            <SettingsFormField :label="$t('pages.settingsUserPage.currentPasswordLabel')" label-for="current-password">
              <PasswordInput
                id="current-password"
                v-model="currentPassword"
                autocomplete="current-password"
                :disabled="isChangingPassword"
                :maxlength="FIELD_LIMITS.password"
              />
            </SettingsFormField>
            <SettingsFormField
              :label="$t('pages.settingsUserPage.newPasswordLabel')"
              label-for="new-password"
              :error="passwordFieldIssue?.password"
            >
              <PasswordInput
                id="new-password"
                v-model="newPassword"
                autocomplete="new-password"
                :disabled="isChangingPassword"
                :maxlength="FIELD_LIMITS.password"
              />
            </SettingsFormField>
            <SettingsFormField
              :label="$t('auth.confirmPassword')"
              label-for="confirm-password"
              :error="passwordFieldIssue?.confirm"
            >
              <PasswordInput
                id="confirm-password"
                v-model="confirmPassword"
                autocomplete="new-password"
                :disabled="isChangingPassword"
                :maxlength="FIELD_LIMITS.password"
              />
            </SettingsFormField>
          </FieldGroup>
          <div class="mt-2 flex justify-end">
            <Button
              type="submit"
              size="sm"
              :disabled="isChangingPassword || !canSubmitPassword"
            >
              <Spinner v-if="isChangingPassword" class="mr-2" />
              {{ $t("pages.settingsUserPage.changePasswordButton") }}
            </Button>
          </div>
        </form>
      </section>
      </div>
    </PageSection>
  </div>
</template>
