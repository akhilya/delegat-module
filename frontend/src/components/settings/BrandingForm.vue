<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed, onMounted, reactive, ref } from "vue";
import { useI18n } from "vue-i18n";
import {
  Upload,
  X,
} from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { http } from "@/main";
import {
  applyBranding,
  DEFAULT_BRAND_FAVICON,
  invalidateBrandingCache,
} from "@/brand";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";

const props = defineProps({
  /** Load branding when the component mounts (setup wizard). */
  loadOnMount: { type: Boolean, default: true },
  /** Compact spacing for narrow auth layout. */
  compact: { type: Boolean, default: false },
});

const emit = defineEmits(["loaded", "saved"]);

const { t } = useI18n();

const loading = ref(false);
const saving = ref(false);
const uploadingLogo = ref(false);
const uploadingFavicon = ref(false);

const saved = reactive({
  name: "",
  description: "",
  logoUrl: "",
  faviconUrl: "",
  hasCustomLogo: false,
  hasCustomFavicon: false,
  primaryColor: "",
  accentColor: "",
});

const form = reactive({
  name: "",
  description: "",
  logoUrl: "",
  faviconUrl: "",
  hasCustomLogo: false,
  hasCustomFavicon: false,
  primaryColor: "",
  accentColor: "",
  clearLogo: false,
  clearFavicon: false,
});

const logoInput = ref(null);
const faviconInput = ref(null);
const draggingLogo = ref(false);
const draggingFavicon = ref(false);

const dirty = computed(() => {
  return (
    form.name !== saved.name
    || (form.description || "") !== (saved.description || "")
    || (form.primaryColor || "") !== (saved.primaryColor || "")
    || (form.accentColor || "") !== (saved.accentColor || "")
    || form.clearLogo
    || form.clearFavicon
    || form.hasCustomLogo !== saved.hasCustomLogo
    || form.hasCustomFavicon !== saved.hasCustomFavicon
    || form.logoUrl !== saved.logoUrl
    || form.faviconUrl !== saved.faviconUrl
  );
});

const canSave = computed(() => Boolean(form.name.trim()) && !loading.value && !saving.value);

const previewPrimary = computed(() => form.primaryColor || "#343434");
const previewAccent = computed(() => form.accentColor || "#F7F7F7");

function applyDto(dto) {
  const next = {
    name: dto.name || "",
    description: dto.description || "",
    logoUrl: dto.logoUrl || "",
    faviconUrl: dto.faviconUrl || DEFAULT_BRAND_FAVICON,
    hasCustomLogo: dto.hasCustomLogo === true,
    hasCustomFavicon: dto.hasCustomFavicon === true,
    primaryColor: dto.primaryColor || "",
    accentColor: dto.accentColor || "",
  };
  Object.assign(saved, next);
  Object.assign(form, { ...next, clearLogo: false, clearFavicon: false });
}

async function load() {
  loading.value = true;
  try {
    const { data } = await http.get("/settings/branding", { cache: false });
    applyDto(data);
    emit("loaded", data);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    loading.value = false;
  }
}

function normalizeHexInput(value) {
  if (!value) return "";
  let v = String(value).trim();
  if (/^[0-9A-Fa-f]{6}$/.test(v)) v = `#${v}`;
  if (/^#[0-9A-Fa-f]{6}$/.test(v)) return v.toUpperCase();
  return v;
}

function resetColors() {
  form.primaryColor = "";
  form.accentColor = "";
}

function openLogoPicker() {
  logoInput.value?.click();
}

function openFaviconPicker() {
  faviconInput.value?.click();
}

async function uploadAsset(file, kind) {
  if (!file) return;
  const isLogo = kind === "logo";
  if (isLogo) uploadingLogo.value = true;
  else uploadingFavicon.value = true;

  try {
    const body = new FormData();
    body.append("file", file);
    const { data } = await http.post(
      isLogo ? "/settings/branding/logo" : "/settings/branding/favicon",
      body,
      { cache: false },
    );
    applyDto(data);
    applyBranding(data);
    invalidateBrandingCache();
    displaySonnerSuccess(
      t(
        isLogo
          ? "pages.settingsOverviewPage.branding.logoUploaded"
          : "pages.settingsOverviewPage.branding.faviconUploaded",
      ),
    );
  } catch (error) {
    displaySonnerError(error);
  } finally {
    if (isLogo) uploadingLogo.value = false;
    else uploadingFavicon.value = false;
  }
}

function onLogoFileChange(event) {
  const file = event.target.files?.[0];
  event.target.value = "";
  uploadAsset(file, "logo");
}

function onFaviconFileChange(event) {
  const file = event.target.files?.[0];
  event.target.value = "";
  uploadAsset(file, "favicon");
}

function onLogoDrop(event) {
  draggingLogo.value = false;
  const file = event.dataTransfer?.files?.[0];
  uploadAsset(file, "logo");
}

function onFaviconDrop(event) {
  draggingFavicon.value = false;
  const file = event.dataTransfer?.files?.[0];
  uploadAsset(file, "favicon");
}

function clearLogo() {
  form.clearLogo = true;
  form.hasCustomLogo = false;
  form.logoUrl = "";
}

function clearFavicon() {
  form.clearFavicon = true;
  form.hasCustomFavicon = false;
  form.faviconUrl = DEFAULT_BRAND_FAVICON;
}

/**
 * Persist identity/colors/clear flags. Returns dto or null on failure.
 * @param {{ quiet?: boolean }} [opts]
 */
async function save(opts = {}) {
  if (!canSave.value) return null;
  if (!dirty.value) {
    emit("saved", { ...saved });
    return { ...saved };
  }

  saving.value = true;
  try {
    const colorsCleared =
      !form.primaryColor
      && !form.accentColor
      && !!(saved.primaryColor || saved.accentColor);

    const payload = {
      name: form.name.trim(),
      description: form.description.trim(),
      primaryColor: form.primaryColor || "",
      accentColor: form.accentColor || "",
      clearColors: colorsCleared || undefined,
      clearLogo: form.clearLogo || undefined,
      clearFavicon: form.clearFavicon || undefined,
    };

    const { data } = await http.put("/settings/branding", payload, { cache: false });
    applyDto(data);
    applyBranding(data);
    invalidateBrandingCache();
    if (!opts.quiet) {
      displaySonnerSuccess(t("pages.settingsOverviewPage.branding.saved"));
    }
    emit("saved", data);
    return data;
  } catch (error) {
    displaySonnerError(error);
    return null;
  } finally {
    saving.value = false;
  }
}

onMounted(() => {
  if (props.loadOnMount) load();
});

defineExpose({
  load,
  save,
  dirty,
  loading,
  saving,
  canSave,
  form,
});
</script>

<template>
  <div v-if="loading" class="flex h-40 items-center justify-center">
    <Spinner size="xl" class="text-muted-foreground" />
  </div>

  <div v-else class="space-y-6" :class="{ 'space-y-8': !compact }">
    <!-- Live preview -->
    <div class="overflow-hidden rounded-lg border">
      <div class="border-b bg-muted/20 px-4 py-2 text-xs font-medium text-muted-foreground">
        {{ t("pages.settingsOverviewPage.branding.previewLabel") }}
      </div>
      <div class="flex flex-wrap items-center gap-4 px-4 py-4">
        <img
          v-if="form.logoUrl"
          :src="form.logoUrl"
          :alt="form.name"
          class="h-10 w-auto max-w-[8rem] object-contain"
        />
        <div class="min-w-0 flex-1">
          <div class="truncate text-base font-semibold tracking-tight">
            {{ form.name || "—" }}
          </div>
          <div class="truncate text-xs text-muted-foreground">
            {{ form.description || t("pages.settingsOverviewPage.branding.descriptionPlaceholder") }}
          </div>
        </div>
        <div class="flex items-center gap-2">
          <span
            class="inline-flex h-8 items-center rounded-md px-3 text-xs font-medium text-white"
            :style="{ backgroundColor: previewPrimary }"
          >
            {{ t("pages.settingsOverviewPage.branding.primarySwatch") }}
          </span>
          <span
            class="inline-flex size-8 items-center justify-center rounded-md border text-[10px] font-medium"
            :style="{ backgroundColor: previewAccent }"
          >
            A
          </span>
        </div>
      </div>
    </div>

    <!-- Identity -->
    <section class="space-y-4">
      <div>
        <h3 class="text-sm font-semibold">
          {{ t("pages.settingsOverviewPage.branding.identityTitle") }}
        </h3>
        <p class="text-xs text-muted-foreground">
          {{ t("pages.settingsOverviewPage.branding.identityHint") }}
        </p>
      </div>
      <FieldGroup class="gap-4">
        <SettingsFormField
          :label="t('pages.settingsOverviewPage.branding.nameLabel')"
          label-for="brand-name"
        >
          <Input
            id="brand-name"
            v-model="form.name"
            maxlength="120"
            :placeholder="t('pages.settingsOverviewPage.branding.namePlaceholder')"
          />
        </SettingsFormField>
        <SettingsFormField
          :label="t('pages.settingsOverviewPage.branding.descriptionLabel')"
          label-for="brand-description"
        >
          <Input
            id="brand-description"
            v-model="form.description"
            maxlength="500"
            :placeholder="t('pages.settingsOverviewPage.branding.descriptionPlaceholder')"
          />
        </SettingsFormField>
      </FieldGroup>
    </section>

    <!-- Assets -->
    <section class="space-y-4">
      <div>
        <h3 class="text-sm font-semibold">
          {{ t("pages.settingsOverviewPage.branding.assetsTitle") }}
        </h3>
        <p class="text-xs text-muted-foreground">
          {{ t("pages.settingsOverviewPage.branding.assetsHint") }}
        </p>
      </div>

      <div class="grid gap-4 sm:grid-cols-2">
        <SettingsFormField :label="t('pages.settingsOverviewPage.branding.logoLabel')">
          <input
            ref="logoInput"
            type="file"
            accept="image/png,image/jpeg,image/webp,image/svg+xml,image/x-icon,.ico"
            class="sr-only"
            @change="onLogoFileChange"
          />
          <Button
            type="button"
            variant="outline"
            class="group flex h-auto w-full flex-col items-center justify-center gap-0 border-dashed px-4 py-6 text-center font-normal shadow-none"
            :class="
              draggingLogo
                ? 'border-foreground/40 bg-muted/40'
                : 'border-muted-foreground/30 hover:border-foreground/35 hover:bg-muted/25'
            "
            :disabled="uploadingLogo"
            @click="openLogoPicker"
            @dragover.prevent="draggingLogo = true"
            @dragleave.prevent="draggingLogo = false"
            @drop.prevent="onLogoDrop"
          >
            <div
              v-if="form.logoUrl"
              class="mb-3 flex size-16 items-center justify-center overflow-hidden rounded-md border bg-background"
            >
              <img :src="form.logoUrl" alt="" class="max-h-14 max-w-14 object-contain" />
            </div>
            <div
              v-else
              class="mb-3 flex size-10 items-center justify-center rounded-md border bg-background text-muted-foreground"
            >
              <Upload class="size-4" />
            </div>
            <p class="text-sm font-medium">
              <Spinner v-if="uploadingLogo" size="sm" class="mr-1 inline" />
              {{ t("pages.settingsOverviewPage.branding.dropzoneTitle") }}
            </p>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ t("pages.settingsOverviewPage.branding.dropzoneHint") }}
            </p>
          </Button>
          <Button
            v-if="form.hasCustomLogo && !form.clearLogo"
            type="button"
            variant="ghost"
            size="sm"
            class="h-8 px-2 text-muted-foreground"
            :disabled="uploadingLogo"
            @click="clearLogo"
          >
            <X class="mr-1 size-3.5" />
            {{ t("pages.settingsOverviewPage.branding.resetAsset") }}
          </Button>
        </SettingsFormField>

        <SettingsFormField :label="t('pages.settingsOverviewPage.branding.faviconLabel')">
          <input
            ref="faviconInput"
            type="file"
            accept="image/png,image/jpeg,image/webp,image/svg+xml,image/x-icon,.ico"
            class="sr-only"
            @change="onFaviconFileChange"
          />
          <Button
            type="button"
            variant="outline"
            class="group flex h-auto w-full flex-col items-center justify-center gap-0 border-dashed px-4 py-6 text-center font-normal shadow-none"
            :class="
              draggingFavicon
                ? 'border-foreground/40 bg-muted/40'
                : 'border-muted-foreground/30 hover:border-foreground/35 hover:bg-muted/25'
            "
            :disabled="uploadingFavicon"
            @click="openFaviconPicker"
            @dragover.prevent="draggingFavicon = true"
            @dragleave.prevent="draggingFavicon = false"
            @drop.prevent="onFaviconDrop"
          >
            <div
              v-if="form.faviconUrl"
              class="mb-3 flex size-16 items-center justify-center overflow-hidden rounded-md border bg-background"
            >
              <img :src="form.faviconUrl" alt="" class="max-h-10 max-w-10 object-contain" />
            </div>
            <div
              v-else
              class="mb-3 flex size-10 items-center justify-center rounded-md border bg-background text-muted-foreground"
            >
              <Upload class="size-4" />
            </div>
            <p class="text-sm font-medium">
              <Spinner v-if="uploadingFavicon" size="sm" class="mr-1 inline" />
              {{ t("pages.settingsOverviewPage.branding.dropzoneTitle") }}
            </p>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ t("pages.settingsOverviewPage.branding.dropzoneHint") }}
            </p>
          </Button>
          <Button
            v-if="form.hasCustomFavicon && !form.clearFavicon"
            type="button"
            variant="ghost"
            size="sm"
            class="h-8 px-2 text-muted-foreground"
            :disabled="uploadingFavicon"
            @click="clearFavicon"
          >
            <X class="mr-1 size-3.5" />
            {{ t("pages.settingsOverviewPage.branding.resetAsset") }}
          </Button>
        </SettingsFormField>
      </div>
    </section>

    <!-- Colors -->
    <section class="space-y-4">
      <div class="flex items-start justify-between gap-3">
        <div>
          <h3 class="text-sm font-semibold">
            {{ t("pages.settingsOverviewPage.branding.colorsTitle") }}
          </h3>
          <p class="text-xs text-muted-foreground">
            {{ t("pages.settingsOverviewPage.branding.colorsHint") }}
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          class="shrink-0"
          @click="resetColors"
        >
          {{ t("pages.settingsOverviewPage.branding.resetColors") }}
        </Button>
      </div>

      <FieldGroup class="gap-4 sm:grid sm:grid-cols-2">
        <SettingsFormField
          :label="t('pages.settingsOverviewPage.branding.primaryLabel')"
          label-for="brand-primary"
        >
          <div class="flex items-center gap-2">
            <input
              type="color"
              class="size-9 shrink-0 cursor-pointer rounded-md border bg-background p-1"
              :value="form.primaryColor || '#343434'"
              @input="(e) => (form.primaryColor = e.target.value.toUpperCase())"
            />
            <Input
              id="brand-primary"
              :model-value="form.primaryColor"
              placeholder="#343434"
              maxlength="7"
              class="font-mono uppercase"
              @update:model-value="(v) => (form.primaryColor = normalizeHexInput(v))"
            />
          </div>
        </SettingsFormField>
        <SettingsFormField
          :label="t('pages.settingsOverviewPage.branding.accentLabel')"
          label-for="brand-accent"
        >
          <div class="flex items-center gap-2">
            <input
              type="color"
              class="size-9 shrink-0 cursor-pointer rounded-md border bg-background p-1"
              :value="form.accentColor || '#F7F7F7'"
              @input="(e) => (form.accentColor = e.target.value.toUpperCase())"
            />
            <Input
              id="brand-accent"
              :model-value="form.accentColor"
              placeholder="#F7F7F7"
              maxlength="7"
              class="font-mono uppercase"
              @update:model-value="(v) => (form.accentColor = normalizeHexInput(v))"
            />
          </div>
        </SettingsFormField>
      </FieldGroup>
    </section>
  </div>
</template>
