<script setup>
import { computed, onMounted, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsPageHeader from "@/components/settings/SettingsPageHeader.vue";
import SettingsSection from "@/components/settings/SettingsSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
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
      <SettingsSection
        title="Профиль"
        description="Имя отображается в интерфейсе и используется при входе."
      >
        <div class="grid max-w-xl gap-4">
          <div class="grid gap-2">
            <label class="text-sm font-medium" for="user-name">Имя пользователя</label>
            <Input
              id="user-name"
              v-model="userNameDraft"
              :disabled="isSavingProfile"
              placeholder="Имя пользователя"
            />
          </div>
          <div class="grid gap-1 text-sm">
            <span class="text-muted-foreground">Роль</span>
            <span class="font-medium">{{ me?.isAdmin ? "Администратор" : "Пользователь" }}</span>
          </div>
        </div>
        <template #footer>
          <Button
            :disabled="isSavingProfile || !userNameDraft.trim() || !hasProfileChanges"
            @click="saveProfile"
          >
            <Loader2 v-if="isSavingProfile" class="mr-2 size-4 animate-spin" />
            Сохранить профиль
          </Button>
        </template>
      </SettingsSection>

      <SettingsSection
        title="Пароль"
        description="Для смены пароля нужно указать текущий пароль."
      >
        <div class="grid max-w-xl gap-4">
          <div class="grid gap-2 sm:grid-cols-2">
            <div class="grid gap-2">
              <label class="text-sm font-medium" for="current-password">Текущий пароль</label>
              <PasswordInput
                id="current-password"
                v-model="currentPassword"
                autocomplete="current-password"
                :disabled="isChangingPassword"
              />
            </div>
            <div class="grid gap-2">
              <label class="text-sm font-medium" for="new-password">Новый пароль</label>
              <PasswordInput
                id="new-password"
                v-model="newPassword"
                autocomplete="new-password"
                :disabled="isChangingPassword"
              />
            </div>
          </div>
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
      </SettingsSection>
    </PageSection>
  </div>
</template>