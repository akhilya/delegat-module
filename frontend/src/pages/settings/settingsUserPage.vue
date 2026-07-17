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
import { Loader2, Copy, Trash2, KeyRound } from "lucide-vue-next";

const me = ref(null);
const isLoading = ref(false);
const isSavingProfile = ref(false);
const isChangingPassword = ref(false);
const userNameDraft = ref("");
const currentPassword = ref("");
const newPassword = ref("");

const apiKeys = ref([]);
const apiKeysLoading = ref(false);
const newKeyName = ref("");
const isCreatingKey = ref(false);
const createdPlaintext = ref(null);
const revokingId = ref(null);

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

async function fetchApiKeys() {
  apiKeysLoading.value = true;
  try {
    const response = await http.get("/api-keys", { cache: false });
    apiKeys.value = response.data?.items || [];
  } catch (error) {
    displaySonnerError(error);
  } finally {
    apiKeysLoading.value = false;
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

async function createApiKey() {
  const name = newKeyName.value.trim();
  if (!name) {
    displaySonnerError("Укажите название ключа");
    return;
  }

  isCreatingKey.value = true;
  try {
    const response = await http.post("/api-keys", { name }, { cache: false });
    createdPlaintext.value = response.data?.plaintext || null;
    newKeyName.value = "";
    await fetchApiKeys();
    displaySonnerSuccess("API-ключ создан — скопируйте его сейчас");
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isCreatingKey.value = false;
  }
}

async function revokeApiKey(id) {
  if (!confirm("Отозвать этот API-ключ? Интеграции перестанут работать.")) return;
  revokingId.value = id;
  try {
    await http.delete(`/api-keys/${id}`, { cache: false });
    if (createdPlaintext.value) createdPlaintext.value = null;
    await fetchApiKeys();
    displaySonnerSuccess("Ключ отозван");
  } catch (error) {
    displaySonnerError(error);
  } finally {
    revokingId.value = null;
  }
}

async function copyPlaintext() {
  if (!createdPlaintext.value) return;
  try {
    await navigator.clipboard.writeText(createdPlaintext.value);
    displaySonnerSuccess("Ключ скопирован");
  } catch {
    displaySonnerError("Не удалось скопировать");
  }
}

onMounted(() => {
  fetchMe();
  fetchApiKeys();
});
</script>

<template>
  <div class="flex flex-col">
    <PageHeader
      title="Пользователь"
      description="Персональные параметры текущей учётной записи"
    />

    <PageSection :loading="isLoading" class="!min-h-0">
      <FieldGroup class="max-w-xl gap-5">
        <SettingsFormField label="Имя пользователя" label-for="user-name">
          <Input
            id="user-name"
            v-model="userNameDraft"
            :disabled="isSavingProfile"
            placeholder="Имя пользователя"
          />
        </SettingsFormField>
        <Button
          class="w-fit"
          :disabled="isSavingProfile || !userNameDraft.trim() || !hasProfileChanges"
          @click="saveProfile"
        >
          <Loader2 v-if="isSavingProfile" class="mr-2 size-4 animate-spin" />
          Сохранить профиль
        </Button>
      </FieldGroup>

      <SettingsFormSection title="Пароль" class="mt-8">
        <div class="grid gap-5 sm:max-w-2xl sm:grid-cols-2">
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

      <SettingsFormSection title="Public API" class="mt-8">
        <p class="mb-4 max-w-2xl text-sm text-muted-foreground">
          Ключи для внешних интеграций (ERP, 1С, n8n). Авторизация:
          <code class="text-xs">Authorization: Bearer dk_…</code>.
          Полный ключ показывается только при создании.
        </p>

        <div
          v-if="createdPlaintext"
          class="mb-5 flex flex-col gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 sm:max-w-2xl"
        >
          <div class="text-sm font-medium">Скопируйте ключ — повторно он не отобразится</div>
          <div class="flex flex-wrap items-center gap-2">
            <code class="break-all text-xs">{{ createdPlaintext }}</code>
            <Button variant="outline" size="sm" @click="copyPlaintext">
              <Copy class="mr-1 size-3.5" />
              Копировать
            </Button>
          </div>
        </div>

        <div class="mb-5 flex flex-wrap items-end gap-3 sm:max-w-2xl">
          <SettingsFormField label="Название ключа" label-for="api-key-name" class="min-w-[220px] flex-1">
            <Input
              id="api-key-name"
              v-model="newKeyName"
              placeholder="Например: 1С"
              :disabled="isCreatingKey"
            />
          </SettingsFormField>
          <Button :disabled="isCreatingKey || !newKeyName.trim()" @click="createApiKey">
            <Loader2 v-if="isCreatingKey" class="mr-2 size-4 animate-spin" />
            <KeyRound v-else class="mr-2 size-4" />
            Создать ключ
          </Button>
        </div>

        <div v-if="apiKeysLoading" class="text-sm text-muted-foreground">Загрузка ключей…</div>
        <div v-else-if="!apiKeys.length" class="text-sm text-muted-foreground">Ключей пока нет</div>
        <ul v-else class="divide-y rounded-md border sm:max-w-2xl">
          <li
            v-for="key in apiKeys"
            :key="key.id"
            class="flex flex-wrap items-center justify-between gap-3 px-3 py-2.5"
          >
            <div class="min-w-0">
              <div class="font-medium">{{ key.name }}</div>
              <div class="text-xs text-muted-foreground">
                {{ key.displayHint }}
                <span v-if="key.isRevoked"> · отозван</span>
                <span v-else-if="key.lastUsedAt"> · использован {{ new Date(key.lastUsedAt).toLocaleString() }}</span>
              </div>
            </div>
            <Button
              v-if="!key.isRevoked"
              variant="ghost"
              size="sm"
              :disabled="revokingId === key.id"
              @click="revokeApiKey(key.id)"
            >
              <Loader2 v-if="revokingId === key.id" class="size-4 animate-spin" />
              <Trash2 v-else class="size-4" />
            </Button>
          </li>
        </ul>
      </SettingsFormSection>
    </PageSection>
  </div>
</template>
