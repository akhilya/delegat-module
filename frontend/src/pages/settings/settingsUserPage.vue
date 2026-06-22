<script setup>
import { computed, onMounted, ref } from "vue";
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import { PageHeader } from "@/components/ui/page-header";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Loader2 } from "lucide-vue-next";

const breadcrumbs = [
  { label: "Настройки", to: "/settings" },
  { label: "Пользователь" },
];

const me = ref(null);
const isLoading = ref(false);
const isSavingProfile = ref(false);
const isChangingPassword = ref(false);
const userNameDraft = ref("");
const currentPassword = ref("");
const newPassword = ref("");

const hasProfileChanges = computed(() =>
  userNameDraft.value.trim() !== (me.value?.userName || "")
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
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      title="Настройки пользователя"
      description="Персональные параметры текущей учётной записи"
    />

    <PageSection :loading="isLoading" class="space-y-8">
      <section class="grid gap-4 lg:grid-cols-[260px_minmax(0,1fr)]">
        <div>
          <h2 class="text-sm font-semibold">Профиль</h2>
          <p class="mt-1 text-sm text-muted-foreground">
            Имя отображается в интерфейсе и используется при входе.
          </p>
        </div>

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

          <div>
            <Button
              :disabled="isSavingProfile || !userNameDraft.trim() || !hasProfileChanges"
              @click="saveProfile"
            >
              <Loader2 v-if="isSavingProfile" class="mr-2 size-4 animate-spin" />
              Сохранить профиль
            </Button>
          </div>
        </div>
      </section>

      <section class="grid gap-4 border-t pt-8 lg:grid-cols-[260px_minmax(0,1fr)]">
        <div>
          <h2 class="text-sm font-semibold">Пароль</h2>
          <p class="mt-1 text-sm text-muted-foreground">
            Для смены пароля нужно указать текущий пароль.
          </p>
        </div>

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

          <div>
            <Button
              :disabled="isChangingPassword || !currentPassword || !newPassword"
              @click="changePassword"
            >
              <Loader2 v-if="isChangingPassword" class="mr-2 size-4 animate-spin" />
              Сменить пароль
            </Button>
          </div>
        </div>
      </section>
    </PageSection>
  </Page>
</template>
