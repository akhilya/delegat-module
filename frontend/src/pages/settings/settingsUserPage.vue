<script setup>
import { computed, onMounted, ref } from "vue";
import { useStore } from "vuex";
import PageSection from "@/components/pageSection.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { PageHeader } from "@/components/ui/page-header";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import { FieldGroup } from "@/components/ui/field";
import { http, loadMe } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { hasPermission, PERMS } from "@/lib/permissions";
import { Loader2 } from "lucide-vue-next";
import { t } from "@/i18n";

const store = useStore();

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

const canManageOperations = computed(() =>
  hasPermission(me.value, PERMS.OperationsManage),
);

const autoOpenOperationsPanel = computed({
  get: () => store.state.operations.autoOpenPanel !== false,
  set: (value) => store.dispatch("operations/setAutoOpenPanel", value),
});

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
  if (!userName) {
    displaySonnerError(t("pages.settingsUserPage.userNameEmptyError"));
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
      <SettingsFormSection>
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

      <SettingsFormSection
        v-if="canManageOperations"
        :title="$t('pages.settingsUserPage.operationsTitle')"
        :description="$t('pages.settingsUserPage.operationsDescription')"
        class="mt-8"
      >
        <div class="flex items-center justify-between gap-4 rounded-lg border p-3">
          <div class="min-w-0">
            <div class="text-sm font-medium">
              {{ $t("pages.settingsUserPage.autoOpenOperationsLabel") }}
            </div>
            <div class="text-xs text-muted-foreground">
              {{ $t("pages.settingsUserPage.autoOpenOperationsHint") }}
            </div>
          </div>
          <Switch v-model="autoOpenOperationsPanel" />
        </div>
      </SettingsFormSection>

      <SettingsFormSection
        :title="$t('pages.settingsUserPage.passwordTitle')"
        :description="$t('pages.settingsUserPage.passwordDescription')"
        class="mt-8"
      >
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
