<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed, onMounted, reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { Image, Upload, X } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import AppImage from "@/components/AppImage.vue";
import { http } from "@/main";
import {
  applyBranding,
  invalidateBrandingCache,
} from "@/brand";
import { storeIconUrl } from "@/lib/storeIcon.js";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";

const props = defineProps({
  /** Load branding when the component mounts (setup wizard). */
  loadOnMount: { type: Boolean, default: true },
  /** Compact spacing for narrow auth layout. */
  compact: { type: Boolean, default: false },
  /** When set, load/save this store’s branding (store settings). Otherwise uses selected store via /settings/branding. */
  storeId: { type: Number, default: null },
  /** Apply colors/logo to the live chrome after save/upload (selected store only). */
  applyLive: { type: Boolean, default: true },
});

const emit = defineEmits(["loaded", "saved", "update:dirty", "update:saving"]);

const { t } = useI18n();

const loading = ref(false);
const saving = ref(false);
const uploadingLogo = ref(false);
const uploadingFavicon = ref(false);

const saved = reactive({
  logoUrl: "",
  faviconUrl: "",
  hasCustomLogo: false,
  hasCustomFavicon: false,
  primaryColor: "",
  accentColor: "",
});

const form = reactive({
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

function brandingPath(suffix = "") {
  if (props.storeId != null) {
    return `/stores/${props.storeId}/branding${suffix}`;
  }
  return `/settings/branding${suffix}`;
}

function afterMutation(data) {
  applyDto(data);
  if (props.applyLive) {
    applyBranding(data);
    invalidateBrandingCache();
  }
  const storeId = data?.storeId ?? props.storeId;
  if (storeId != null) {
    window.dispatchEvent(
      new CustomEvent("store-branding-updated", {
        detail: {
          storeId,
          faviconUrl: storeIconUrl(data.faviconUrl),
          logoUrl: data.logoUrl,
        },
      }),
    );
  }
}

const dirty = computed(() => {
  return (
    (form.primaryColor || "") !== (saved.primaryColor || "") ||
    (form.accentColor || "") !== (saved.accentColor || "") ||
    form.clearLogo ||
    form.clearFavicon ||
    form.hasCustomLogo !== saved.hasCustomLogo ||
    form.hasCustomFavicon !== saved.hasCustomFavicon ||
    form.logoUrl !== saved.logoUrl ||
    form.faviconUrl !== saved.faviconUrl
  );
});

const canSave = computed(() => !loading.value && !saving.value);

watch(dirty, (v) => emit("update:dirty", v), { immediate: true });
watch(saving, (v) => emit("update:saving", v), { immediate: true });

function applyDto(dto) {
  const next = {
    logoUrl: dto.logoUrl || "",
    faviconUrl: storeIconUrl(dto.faviconUrl),
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
    const { data } = await http.get(brandingPath(), { cache: false });
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
      isLogo ? brandingPath("/logo") : brandingPath("/favicon"),
      body,
      { cache: false },
    );
    afterMutation(data);
    displaySonnerSuccess(
      t(
        isLogo
          ? "pages.settingsOverviewPage.branding.logoUploaded"
          : "pages.settingsOverviewPage.branding.faviconUploaded",
      ),
    );
    emit("saved", data);
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
  form.faviconUrl = "";
}

/**
 * Persist colors/clear flags. Returns dto or null on failure.
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
      !form.primaryColor &&
      !form.accentColor &&
      !!(saved.primaryColor || saved.accentColor);

    const payload = {
      primaryColor: form.primaryColor || "",
      accentColor: form.accentColor || "",
      clearColors: colorsCleared || undefined,
      clearLogo: form.clearLogo || undefined,
      clearFavicon: form.clearFavicon || undefined,
    };

    const { data } = await http.put(brandingPath(), payload, { cache: false });
    afterMutation(data);
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

  <div v-else class="flex flex-col gap-6" :class="{ 'gap-8': !compact }">
    <section>
      <InsetGroupLabel>
        {{ t("pages.settingsOverviewPage.branding.assetsTitle") }}
      </InsetGroupLabel>
      <p class="mb-2 px-1 text-xs text-muted-foreground">
        {{ t("pages.settingsOverviewPage.branding.assetsHint") }}
      </p>
      <input
        ref="logoInput"
        type="file"
        accept="image/png,image/jpeg,image/webp,image/svg+xml,image/x-icon,.ico"
        class="sr-only"
        @change="onLogoFileChange"
      />
      <input
        ref="faviconInput"
        type="file"
        accept="image/png,image/jpeg,image/webp,image/svg+xml,image/x-icon,.ico"
        class="sr-only"
        @change="onFaviconFileChange"
      />
      <InsetGroup>
        <InsetGroupItem
          :disabled="uploadingLogo"
          :class="draggingLogo ? 'bg-muted/70' : undefined"
          @click="openLogoPicker"
          @dragover.prevent="draggingLogo = true"
          @dragleave.prevent="draggingLogo = false"
          @drop.prevent="onLogoDrop"
        >
          <template #leading>
            <Spinner v-if="uploadingLogo" size="sm" />
            <AppImage
              v-else-if="form.logoUrl"
              :src="form.logoUrl"
              alt=""
              spinner-size="sm"
              class="flex size-8 items-center justify-center"
              img-class="max-h-6 max-w-6 object-contain"
            />
            <Image v-else class="size-4" />
          </template>
          <p>{{ t("pages.settingsOverviewPage.branding.logoLabel") }}</p>
          <p class="mt-0.5 text-xs font-normal text-muted-foreground">
            {{
              t(
                form.hasCustomLogo && !form.clearLogo
                  ? "pages.settingsOverviewPage.branding.replaceImage"
                  : "pages.settingsOverviewPage.branding.chooseImage",
              )
            }}
          </p>
          <template #trailing>
            <Button
              v-if="form.hasCustomLogo && !form.clearLogo"
              type="button"
              variant="ghost"
              size="icon"
              class="size-8 text-muted-foreground"
              :disabled="uploadingLogo"
              :aria-label="t('pages.settingsOverviewPage.branding.resetAsset')"
              @click.stop="clearLogo"
            >
              <X class="size-4" />
            </Button>
            <Upload v-else class="size-4 opacity-40" />
          </template>
        </InsetGroupItem>
        <InsetGroupItem
          :disabled="uploadingFavicon"
          :class="draggingFavicon ? 'bg-muted/70' : undefined"
          @click="openFaviconPicker"
          @dragover.prevent="draggingFavicon = true"
          @dragleave.prevent="draggingFavicon = false"
          @drop.prevent="onFaviconDrop"
        >
          <template #leading>
            <Spinner v-if="uploadingFavicon" size="sm" />
            <AppImage
              v-else-if="form.faviconUrl"
              :src="form.faviconUrl"
              alt=""
              spinner-size="sm"
              class="flex size-8 items-center justify-center"
              img-class="max-h-6 max-w-6 object-contain"
            />
            <Image v-else class="size-4" />
          </template>
          <p>{{ t("pages.settingsOverviewPage.branding.faviconLabel") }}</p>
          <p class="mt-0.5 text-xs font-normal text-muted-foreground">
            {{
              t(
                form.hasCustomFavicon && !form.clearFavicon
                  ? "pages.settingsOverviewPage.branding.replaceImage"
                  : "pages.settingsOverviewPage.branding.chooseImage",
              )
            }}
          </p>
          <template #trailing>
            <Button
              v-if="form.hasCustomFavicon && !form.clearFavicon"
              type="button"
              variant="ghost"
              size="icon"
              class="size-8 text-muted-foreground"
              :disabled="uploadingFavicon"
              :aria-label="t('pages.settingsOverviewPage.branding.resetAsset')"
              @click.stop="clearFavicon"
            >
              <X class="size-4" />
            </Button>
            <Upload v-else class="size-4 opacity-40" />
          </template>
        </InsetGroupItem>
      </InsetGroup>
    </section>

    <section>
      <div class="mb-2 flex items-start justify-between gap-3 px-1">
        <div>
          <InsetGroupLabel class="mb-1 px-0">
            {{ t("pages.settingsOverviewPage.branding.colorsTitle") }}
          </InsetGroupLabel>
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
              @update:model-value="
                (v) => (form.primaryColor = normalizeHexInput(v))
              "
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
              @update:model-value="
                (v) => (form.accentColor = normalizeHexInput(v))
              "
            />
          </div>
        </SettingsFormField>
      </FieldGroup>
    </section>
  </div>
</template>
