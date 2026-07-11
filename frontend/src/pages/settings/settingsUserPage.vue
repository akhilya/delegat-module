<script setup>
import { computed, onMounted, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsPageHeader from "@/components/settings/SettingsPageHeader.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import InlineEditableValue from "@/components/settings/InlineEditableValue.vue";
import { Button } from "@/components/ui/button";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Loader2 } from "lucide-vue-next";

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
    displaySonnerError("Имя пользователя не может быть пустым");
    return;
  }

  isSavingProfile.value = true;
  try {
    const response = await http.patch("/users/me", { userName }, { cache: false });
    me.value = response.data?.value || response.data;
    userNameDraft.value = me.value?.userName || "";
    displaySonnerSuccess("Профиль обновлён");
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
    displaySonnerSuccess("Пароль обновлён");
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
    <SettingsPageHeader
      title="Пользователь"
      description="Персональные параметры текущей учётной записи"
    />

    <PageSection :loading="isLoading" class="!min-h-0">
      <SettingsFormSection title="Профиль">
        <SettingsFormField
          label="Имя пользователя"
          :changed="hasProfileChanges"
        >
          <InlineEditableValue
            :model-value="userNameDraft"
            :disabled="isSavingProfile"
            placeholder="Имя пользователя"
            empty-label="Имя не задано"
            @update:model-value="userNameDraft = $event"
          />
          <template #description>
            Отображаемое имя в интерфейсе и при входе в систему.
          </template>
        </SettingsFormField>

        <SettingsFormField label="Роль">
          <p class="text-sm font-medium">
            {{ me?.isAdmin ? "Администратор" : "Пользователь" }}
          </p>
          <template #description>
            Глобальная роль учётной записи на платформе.
          </template>
        </SettingsFormField>

        <template #footer>
          <Button
            :disabled="isSavingProfile || !userNameDraft.trim() || !hasProfileChanges"
            @click="saveProfile"
          >
            <Loader2 v-if="isSavingProfile" class="mr-2 size-4 animate-spin" />
            Сохранить профиль
          </Button>
        </template>
      </SettingsFormSection>

      <SettingsFormSection title="Пароль">
        <div class="grid max-w-2xl gap-8 sm:grid-cols-2">
          <SettingsFormField label="Текущий пароль" label-for="current-password">
            <PasswordInput
              id="current-password"
              v-model="currentPassword"
              autocomplete="current-password"
              :disabled="isChangingPassword"
            />
          </SettingsFormField>

          <SettingsFormField label="Новый пароль" label-for="new-password">
            <PasswordInput
              id="new-password"
              v-model="newPassword"
              autocomplete="new-password"
              :disabled="isChangingPassword"
            />
            <template #description>
              Вступит в силу после сохранения.
            </template>
          </SettingsFormField>
        </div>

        <template #footer>
          <Button
            :disabled="isChangingPassword || !currentPassword || !newPassword"
            @click="changePassword"
          >
            <Loader2 v-if="isChangingPassword" class="mr-2 size-4 animate-spin" />
            Сменить пароль
          </Button>
        </template>
      </SettingsFormSection>
    </PageSection>
  </div>
</template>