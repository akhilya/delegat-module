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
import { Loader2, Copy, Trash2, KeyRound, ExternalLink } from "lucide-vue-next";
import { t } from "@/i18n";

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

async function createApiKey() {
  const name = newKeyName.value.trim();
  if (!name) {
    displaySonnerError(t("pages.settingsUserPage.keyNameRequiredError"));
    return;
  }

  isCreatingKey.value = true;
  try {
    const response = await http.post("/api-keys", { name }, { cache: false });
    createdPlaintext.value = response.data?.plaintext || null;
    newKeyName.value = "";
    await fetchApiKeys();
    displaySonnerSuccess(t("pages.settingsUserPage.apiKeyCreated"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isCreatingKey.value = false;
  }
}

async function revokeApiKey(id) {
  if (!confirm(t("pages.settingsUserPage.revokeKeyConfirm"))) return;
  revokingId.value = id;
  try {
    await http.delete(`/api-keys/${id}`, { cache: false });
    if (createdPlaintext.value) createdPlaintext.value = null;
    await fetchApiKeys();
    displaySonnerSuccess(t("pages.settingsUserPage.keyRevoked"));
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
    displaySonnerSuccess(t("pages.settingsUserPage.keyCopied"));
  } catch {
    displaySonnerError(t("common.copyFailed"));
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
      :title="$t('nav.user')"
      :description="$t('pages.settingsUserPage.description')"
    />

    <PageSection :loading="isLoading" class="!min-h-0">
      <FieldGroup class="max-w-xl gap-5">
        <SettingsFormField :label="$t('auth.userName')" label-for="user-name">
          <Input
            id="user-name"
            v-model="userNameDraft"
            :disabled="isSavingProfile"
            :placeholder="$t('auth.userName')"
          />
        </SettingsFormField>
        <Button
          class="w-fit"
          :disabled="isSavingProfile || !userNameDraft.trim() || !hasProfileChanges"
          @click="saveProfile"
        >
          <Loader2 v-if="isSavingProfile" class="mr-2 size-4 animate-spin" />
          {{ $t("pages.settingsUserPage.saveProfileButton") }}
        </Button>
      </FieldGroup>

      <SettingsFormSection :title="$t('auth.password')" class="mt-8">
        <div class="grid gap-5 sm:max-w-2xl sm:grid-cols-2">
          <SettingsFormField :label="$t('pages.settingsUserPage.currentPasswordLabel')" label-for="current-password">
            <PasswordInput
              id="current-password"
              v-model="currentPassword"
              autocomplete="current-password"
              :disabled="isChangingPassword"
            />
          </SettingsFormField>

          <SettingsFormField :label="$t('pages.settingsUserPage.newPasswordLabel')" label-for="new-password">
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
            {{ $t("pages.settingsUserPage.changePasswordButton") }}
          </Button>
        </template>
      </SettingsFormSection>

      <SettingsFormSection title="Public API" class="mt-8">
        <p class="mb-4 max-w-2xl text-sm text-muted-foreground">
          {{ $t("pages.settingsUserPage.apiKeysIntroPrefix") }}
          <code class="text-xs">Authorization: Bearer dk_…</code>.
          {{ $t("pages.settingsUserPage.apiKeysIntroSuffix") }}
        </p>
        <p class="mb-4 max-w-2xl text-sm">
          <a
            href="/swagger"
            target="_blank"
            rel="noopener noreferrer"
            class="inline-flex items-center gap-1 text-primary underline-offset-4 hover:underline"
          >
            {{ $t("pages.settingsUserPage.openSwaggerLink") }}
            <ExternalLink class="size-3.5" />
          </a>
          <span class="text-muted-foreground"> · </span>
          <span class="text-muted-foreground text-xs">/swagger</span>
        </p>

        <div
          v-if="createdPlaintext"
          class="mb-5 flex flex-col gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 sm:max-w-2xl"
        >
          <div class="text-sm font-medium">{{ $t("pages.settingsUserPage.copyKeyWarning") }}</div>
          <div class="flex flex-wrap items-center gap-2">
            <code class="break-all text-xs">{{ createdPlaintext }}</code>
            <Button variant="outline" size="sm" @click="copyPlaintext">
              <Copy class="mr-1 size-3.5" />
              {{ $t("common.copy") }}
            </Button>
          </div>
        </div>

        <div class="mb-5 flex flex-wrap items-end gap-3 sm:max-w-2xl">
          <SettingsFormField :label="$t('pages.settingsUserPage.keyNameLabel')" label-for="api-key-name" class="min-w-[220px] flex-1">
            <Input
              id="api-key-name"
              v-model="newKeyName"
              :placeholder="$t('pages.settingsUserPage.keyNamePlaceholder')"
              :disabled="isCreatingKey"
            />
          </SettingsFormField>
          <Button :disabled="isCreatingKey || !newKeyName.trim()" @click="createApiKey">
            <Loader2 v-if="isCreatingKey" class="mr-2 size-4 animate-spin" />
            <KeyRound v-else class="mr-2 size-4" />
            {{ $t("pages.settingsUserPage.createKeyButton") }}
          </Button>
        </div>

        <div v-if="apiKeysLoading" class="text-sm text-muted-foreground">{{ $t("pages.settingsUserPage.loadingKeys") }}</div>
        <div v-else-if="!apiKeys.length" class="text-sm text-muted-foreground">{{ $t("pages.settingsUserPage.noKeysYet") }}</div>
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
                <span v-if="key.isRevoked"> · {{ $t("pages.settingsUserPage.revokedSuffix") }}</span>
                <span v-else-if="key.lastUsedAt"> · {{ $t("pages.settingsUserPage.usedSuffix", { date: new Date(key.lastUsedAt).toLocaleString() }) }}</span>
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
