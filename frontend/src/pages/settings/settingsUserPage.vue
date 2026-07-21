<script setup>
import { computed, onMounted, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { PageHeader } from "@/components/ui/page-header";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import { FieldGroup } from "@/components/ui/field";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Loader2 } from "lucide-vue-next";
import { t } from "@/i18n";

const me = ref(null);
const isLoading = ref(false);
const isSavingProfile = ref(false);
const isChangingPassword = ref(false);
const userNameDraft = ref("");
const currentPassword = ref("");
const newPassword = ref("");

const hasProfileChanges = computed(() =>
  userNameDraft.value.trim() !== (me.value?.userName || ""),
);

async function fetchMe() {
  isLoading.value = true;
  try {
    const response = await http.get("/auth/me", { cache: false });
    me.value = response.data?.value || response.data;
    userNameDraft.value = me.value?.userName || "";
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isLoading.value = false;
  }
}

async function saveProfile() {
  const userName = userNameDraft.value.trim();
  if (!userName) {
    displaySonnerError(t("pages.settingsUserPage.userNameEmptyError"));
    return;
  }

  isSavingProfile.value = true;
  try {
    const response = await http.patch("/users/me", { userName }, { cache: false });
    me.value = response.data?.value || response.data;
    userNameDraft.value = me.value?.userName || "";
    displaySonnerSuccess(t("pages.settingsUserPage.profileUpdated"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isSavingProfile.value = false;
  }
}

async function changePassword() {
  isChangingPassword.value = true;
  try {
    const response = await http.patch("/users/me", {
      currentPassword: currentPassword.value,
      password: newPassword.value,
    }, { cache: false });
    me.value = response.data?.value || response.data;
    currentPassword.value = "";
    newPassword.value = "";
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
  <div>
    <PageHeader
      :title="$t('pages.settingsUserPage.title')"
      :description="$t('pages.settingsUserPage.description')"
    />

    <PageSection :loading="isLoading">
      <SettingsFormSection :title="$t('pages.settingsUserPage.profileTitle')">
        <FieldGroup>
          <SettingsFormField :label="$t('pages.settingsUserPage.userNameLabel')" label-for="user-name">
            <Input id="user-name" v-model="userNameDraft" :disabled="isSavingProfile" />
          </SettingsFormField>
        </FieldGroup>
        <template #footer>
          <Button
            :disabled="isSavingProfile || !hasProfileChanges"
            @click="saveProfile"
          >
            <Loader2 v-if="isSavingProfile" class="mr-2 size-4 animate-spin" />
            {{ $t("pages.settingsUserPage.saveProfileButton") }}
          </Button>
        </template>
      </SettingsFormSection>

      <SettingsFormSection :title="$t('pages.settingsUserPage.passwordTitle')" class="mt-8">
        <FieldGroup>
          <SettingsFormField :label="$t('pages.settingsUserPage.currentPasswordLabel')" label-for="current-password">
            <PasswordInput id="current-password" v-model="currentPassword" :disabled="isChangingPassword" />
          </SettingsFormField>
          <SettingsFormField :label="$t('pages.settingsUserPage.newPasswordLabel')" label-for="new-password">
            <PasswordInput id="new-password" v-model="newPassword" :disabled="isChangingPassword" />
          </SettingsFormField>
        </FieldGroup>
        <template #footer>
          <Button
            :disabled="isChangingPassword || !currentPassword || !newPassword"
            @click="changePassword"
          >
            <Loader2 v-if="isChangingPassword" class="mr-2 size-4 animate-spin" />
            {{ $t("pages.settingsUserPage.changePasswordButton") }}
          </Button>
        </template>
      </SettingsFormSection>
    </PageSection>
  </div>
</template>
