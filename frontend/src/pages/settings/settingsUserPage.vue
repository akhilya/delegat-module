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
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { http, loadMe } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { hasPermission, PERMS } from "@/lib/permissions";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import { newPasswordFieldsIssue } from "@/lib/authValidation";
import { otpauthQrSvg } from "@/lib/totpQr";
import { createPasskey, mapWebAuthnError } from "@/lib/webauthn";
import { formatDateTimeCombined } from "@/utils/dateTime";
import { Activity, Copy, Fingerprint, Save } from "lucide-vue-next";

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

const totpEnabled = ref(false);
const passkeys = ref([]);
const totpPassword = ref("");
const totpCode = ref("");
const totpSecret = ref("");
const totpQrSvg = ref("");
const recoveryCodes = ref([]);
const totpBusy = ref(false);
const passkeyBusy = ref(false);
const passkeyNickname = ref("");
const disableOpen = ref(false);
const deleteOpen = ref(false);
const disablePassword = ref("");
const deletePasskeyId = ref(null);
const passkeyPassword = ref("");
const deletePassword = ref("");

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

const hasProfileChanges = computed(
  () => userNameDraft.value.trim() !== (me.value?.userName || ""),
);

const canManageOperations = computed(() =>
  hasPermission(me.value, PERMS.OperationsManage),
);

const autoOpenOperationsPanel = computed({
  get: () => store.state.operations.autoOpenPanel !== false,
  set: (value) => store.dispatch("operations/setAutoOpenPanel", value),
});

function unwrap(response) {
  return response.data?.value ?? response.data;
}

function toggleAutoOpenOperations() {
  autoOpenOperationsPanel.value = !autoOpenOperationsPanel.value;
}

function passkeyDate(iso) {
  return formatDateTimeCombined(iso);
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

async function loadSecurity() {
  const response = await http.get("/auth/account/security", { cache: false });
  const data = unwrap(response);
  totpEnabled.value = !!data?.totpEnabled;
  passkeys.value = Array.isArray(data?.passkeys) ? data.passkeys : [];
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
    const updated = unwrap(response);
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
    const response = await http.patch(
      "/users/me",
      {
        currentPassword: currentPassword.value,
        password: newPassword.value,
      },
      { cache: false },
    );
    const updated = unwrap(response);
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

async function startTotp() {
  totpBusy.value = true;
  try {
    const data = unwrap(
      await http.post(
        "/auth/account/totp/start",
        { password: totpPassword.value },
        { cache: false },
      ),
    );
    totpSecret.value = data?.secret || "";
    totpQrSvg.value = data?.otpauthUri ? await otpauthQrSvg(data.otpauthUri) : "";
    totpPassword.value = "";
    totpCode.value = "";
    recoveryCodes.value = [];
  } catch (error) {
    displaySonnerError(error);
  } finally {
    totpBusy.value = false;
  }
}

async function confirmTotp() {
  totpBusy.value = true;
  try {
    const data = unwrap(
      await http.post(
        "/auth/account/totp/confirm",
        { code: totpCode.value },
        { cache: false },
      ),
    );
    recoveryCodes.value = Array.isArray(data?.recoveryCodes) ? data.recoveryCodes : [];
    totpSecret.value = "";
    totpQrSvg.value = "";
    totpCode.value = "";
    totpEnabled.value = true;
    await loadSecurity();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    totpBusy.value = false;
  }
}

async function disableTotp() {
  totpBusy.value = true;
  try {
    await http.post(
      "/auth/account/totp/disable",
      { password: disablePassword.value },
      { cache: false },
    );
    disableOpen.value = false;
    disablePassword.value = "";
    totpEnabled.value = false;
    recoveryCodes.value = [];
    totpSecret.value = "";
    totpQrSvg.value = "";
    await loadSecurity();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    totpBusy.value = false;
  }
}

async function copyRecovery() {
  try {
    await navigator.clipboard.writeText(recoveryCodes.value.join("\n"));
    displaySonnerSuccess(t("pages.settingsUserPage.totpRecoveryCopied"));
  } catch (error) {
    displaySonnerError(error);
  }
}

async function addPasskey() {
  passkeyBusy.value = true;
  try {
    const password = passkeyPassword.value;
    const options = unwrap(
      await http.post("/auth/account/passkeys/options", { password }, { cache: false }),
    );
    const attestation = await createPasskey(options);
    const nickname = passkeyNickname.value.trim();
    await http.post(
      "/auth/account/passkeys",
      { ...attestation, nickname: nickname || undefined, password },
      { cache: false },
    );
    passkeyNickname.value = "";
    passkeyPassword.value = "";
    displaySonnerSuccess(t("pages.settingsUserPage.passkeyAdded"));
    await loadSecurity();
  } catch (error) {
    const mapped = mapWebAuthnError(error);
    if (mapped === "canceled") {
      displaySonnerError(t("pages.settingsUserPage.passkeyCanceled"));
    } else if (mapped === "alreadyRegistered") {
      displaySonnerError(t("pages.settingsUserPage.passkeyAlreadyRegistered"));
    } else {
      displaySonnerError(error?.response ? error : t("pages.settingsUserPage.passkeyFailed"));
    }
  } finally {
    passkeyBusy.value = false;
  }
}

async function removePasskey() {
  if (!deletePasskeyId.value) return;
  passkeyBusy.value = true;
  try {
    await http.delete(`/auth/account/passkeys/${deletePasskeyId.value}`, {
      data: { password: deletePassword.value },
      cache: false,
    });
    deleteOpen.value = false;
    deletePasskeyId.value = null;
    deletePassword.value = "";
    displaySonnerSuccess(t("pages.settingsUserPage.passkeyRemoved"));
    await loadSecurity();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    passkeyBusy.value = false;
  }
}

onMounted(() => {
  fetchMe();
  loadSecurity().catch((error) => displaySonnerError(error));
});
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

      <section>
        <InsetGroupLabel>{{ $t("pages.settingsUserPage.totpTitle") }}</InsetGroupLabel>
        <p class="mb-2 px-1 text-xs text-muted-foreground">
          {{ $t("pages.settingsUserPage.totpDescription") }}
        </p>
        <p class="mb-3 px-1 text-sm">
          {{
            totpEnabled
              ? $t("pages.settingsUserPage.totpEnabled")
              : $t("pages.settingsUserPage.totpDisabled")
          }}
        </p>
        <form
          v-if="!totpEnabled && !totpSecret"
          class="space-y-3"
          @submit.prevent="startTotp"
        >
          <FieldGroup class="gap-5">
            <SettingsFormField
              :label="$t('pages.settingsUserPage.totpPasswordLabel')"
              label-for="totp-start-password"
            >
              <PasswordInput
                id="totp-start-password"
                v-model="totpPassword"
                autocomplete="current-password"
                :disabled="totpBusy"
                :maxlength="FIELD_LIMITS.password"
              />
            </SettingsFormField>
          </FieldGroup>
          <div class="flex justify-end">
            <Button type="submit" size="sm" :disabled="totpBusy || !totpPassword">
              <Spinner v-if="totpBusy" class="mr-2" />
              {{ $t("pages.settingsUserPage.totpStart") }}
            </Button>
          </div>
        </form>
        <div v-else-if="totpSecret" class="space-y-3">
          <div
            v-if="totpQrSvg"
            class="size-52 overflow-hidden rounded-md border bg-white p-2"
            role="img"
            :aria-label="$t('pages.settingsUserPage.totpQrLabel')"
            v-html="totpQrSvg"
          />
          <p class="px-1 text-sm">
            <span class="text-muted-foreground">{{ $t("pages.settingsUserPage.totpSecretLabel") }}:</span>
            <code class="ml-2 font-mono tracking-wider">{{ totpSecret }}</code>
          </p>
          <form @submit.prevent="confirmTotp">
            <FieldGroup class="gap-5">
              <SettingsFormField
                :label="$t('pages.settingsUserPage.totpConfirmLabel')"
                label-for="totp-confirm-code"
              >
                <Input
                  id="totp-confirm-code"
                  v-model="totpCode"
                  inputmode="numeric"
                  autocomplete="one-time-code"
                  maxlength="6"
                  :disabled="totpBusy"
                />
              </SettingsFormField>
            </FieldGroup>
            <div class="mt-2 flex justify-end">
              <Button type="submit" size="sm" :disabled="totpBusy || totpCode.length !== 6">
                <Spinner v-if="totpBusy" class="mr-2" />
                {{ $t("pages.settingsUserPage.totpConfirm") }}
              </Button>
            </div>
          </form>
        </div>
        <div v-if="recoveryCodes.length" class="mt-4 space-y-2">
          <p class="px-1 text-xs text-muted-foreground">
            {{ $t("pages.settingsUserPage.totpRecoveryHint") }}
          </p>
          <ul class="px-1 font-mono text-sm tracking-wide">
            <li v-for="code in recoveryCodes" :key="code">{{ code }}</li>
          </ul>
          <Button type="button" variant="outline" size="sm" @click="copyRecovery">
            <Copy class="mr-2 size-4" />
            {{ $t("pages.settingsUserPage.totpRecoveryCopy") }}
          </Button>
        </div>
        <div v-if="totpEnabled" class="mt-3">
          <Button type="button" variant="destructive" size="sm" @click="disableOpen = true">
            {{ $t("pages.settingsUserPage.totpDisable") }}
          </Button>
        </div>
      </section>

      <section>
        <InsetGroupLabel>{{ $t("pages.settingsUserPage.passkeysTitle") }}</InsetGroupLabel>
        <p class="mb-2 px-1 text-xs text-muted-foreground">
          {{ $t("pages.settingsUserPage.passkeysDescription") }}
        </p>
        <InsetGroup v-if="passkeys.length" class="mb-4">
          <InsetGroupItem
            v-for="item in passkeys"
            :key="item.id"
            as="div"
          >
            <template #leading>
              <Fingerprint class="size-4" />
            </template>
            <p class="truncate">
              {{ item.nickname || $t("pages.settingsUserPage.passkeysTitle") }}
            </p>
            <p class="mt-0.5 text-xs font-normal text-muted-foreground">
              {{ $t("pages.settingsUserPage.passkeyCreated", { date: passkeyDate(item.createdAt) }) }}
            </p>
            <p class="text-xs font-normal text-muted-foreground">
              {{
                item.lastUsedAt
                  ? $t("pages.settingsUserPage.passkeyLastUsed", { date: passkeyDate(item.lastUsedAt) })
                  : $t("pages.settingsUserPage.passkeyNeverUsed")
              }}
            </p>
            <template #trailing>
              <Button
                type="button"
                variant="outline"
                size="sm"
                :disabled="passkeyBusy"
                @click="deletePasskeyId = item.id; deleteOpen = true"
              >
                {{ $t("pages.settingsUserPage.passkeyDelete") }}
              </Button>
            </template>
          </InsetGroupItem>
        </InsetGroup>
        <p v-else class="mb-3 px-1 text-sm text-muted-foreground">
          {{ $t("pages.settingsUserPage.passkeyEmpty") }}
        </p>
        <form class="space-y-3" @submit.prevent="addPasskey">
          <FieldGroup class="gap-5">
            <SettingsFormField
              :label="$t('pages.settingsUserPage.passkeyNicknameLabel')"
              label-for="passkey-nickname"
            >
              <Input
                id="passkey-nickname"
                v-model="passkeyNickname"
                :placeholder="$t('pages.settingsUserPage.passkeyNicknamePlaceholder')"
                autocomplete="off"
                maxlength="128"
                :disabled="passkeyBusy"
              />
            </SettingsFormField>
            <SettingsFormField
              :label="$t('pages.settingsUserPage.totpPasswordLabel')"
              label-for="passkey-password"
            >
              <PasswordInput
                id="passkey-password"
                v-model="passkeyPassword"
                autocomplete="current-password"
                :disabled="passkeyBusy"
                :maxlength="FIELD_LIMITS.password"
              />
            </SettingsFormField>
          </FieldGroup>
          <div class="flex justify-end">
            <Button type="submit" size="sm" :disabled="passkeyBusy || !passkeyPassword">
              <Spinner v-if="passkeyBusy" class="mr-2" />
              <Fingerprint v-else class="mr-2 size-4" />
              {{ $t("pages.settingsUserPage.passkeyAdd") }}
            </Button>
          </div>
        </form>
      </section>
      </div>
    </PageSection>

    <AlertDialog v-model:open="disableOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t("pages.settingsUserPage.totpDisableConfirmTitle") }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t("pages.settingsUserPage.totpDisableConfirmBody") }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <form class="space-y-4" @submit.prevent="disableTotp">
          <SettingsFormField
            :label="$t('pages.settingsUserPage.totpPasswordLabel')"
            label-for="totp-disable-password"
          >
            <PasswordInput
              id="totp-disable-password"
              v-model="disablePassword"
              autocomplete="current-password"
              :disabled="totpBusy"
              :maxlength="FIELD_LIMITS.password"
            />
          </SettingsFormField>
          <AlertDialogFooter>
            <AlertDialogCancel type="button">{{ $t("common.cancel") }}</AlertDialogCancel>
            <Button type="submit" variant="destructive" :disabled="totpBusy || !disablePassword">
              <Spinner v-if="totpBusy" class="mr-2" />
              {{ $t("pages.settingsUserPage.totpDisable") }}
            </Button>
          </AlertDialogFooter>
        </form>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog v-model:open="deleteOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t("pages.settingsUserPage.passkeyDeleteTitle") }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t("pages.settingsUserPage.passkeyDeleteBody") }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <form class="space-y-4" @submit.prevent="removePasskey">
          <SettingsFormField
            :label="$t('pages.settingsUserPage.totpPasswordLabel')"
            label-for="passkey-delete-password"
          >
            <PasswordInput
              id="passkey-delete-password"
              v-model="deletePassword"
              autocomplete="current-password"
              :disabled="passkeyBusy"
              :maxlength="FIELD_LIMITS.password"
            />
          </SettingsFormField>
          <AlertDialogFooter>
            <AlertDialogCancel type="button">{{ $t("common.cancel") }}</AlertDialogCancel>
            <Button type="submit" variant="destructive" :disabled="passkeyBusy || !deletePassword">
              <Spinner v-if="passkeyBusy" class="mr-2" />
              {{ $t("pages.settingsUserPage.passkeyDelete") }}
            </Button>
          </AlertDialogFooter>
        </form>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
