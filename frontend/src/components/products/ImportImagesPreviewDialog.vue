<script setup>
/**
 * Preview marketplace photos and pick which to download (append-only).
 */
import { computed, ref, watch } from "vue";
import { Download, ImageOff } from "lucide-vue-next";
import { t } from "@/i18n";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Checkbox } from "@/components/ui/checkbox";
import AppImage from "@/components/AppImage.vue";
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field";

const props = defineProps({
  open: { type: Boolean, default: false },
  images: { type: Array, default: () => [] },
  localGalleryCount: { type: Number, default: 0 },
  confirmLoading: { type: Boolean, default: false },
});

const emit = defineEmits(["update:open", "confirm", "cancel"]);

const selectedUrls = ref(new Set());
const brokenUrls = ref(new Set());

const newImages = computed(() =>
  props.images.filter((img) => img.status === "new"),
);

const selectableImages = computed(() =>
  props.images.filter((img) => img.status !== "queued"),
);

const allSelectableSelected = computed(() => {
  if (selectableImages.value.length === 0) return false;
  return selectableImages.value.every((img) => selectedUrls.value.has(img.url));
});

const confirmDisabled = computed(
  () => props.confirmLoading || selectedUrls.value.size === 0,
);

watch(
  () => props.images,
  () => {
    selectedUrls.value = new Set(newImages.value.map((img) => img.url));
    brokenUrls.value = new Set();
  },
  { immediate: true },
);

function isSelected(url) {
  return selectedUrls.value.has(url);
}

function toggleUrl(url, checked) {
  const next = new Set(selectedUrls.value);
  if (checked) next.add(url);
  else next.delete(url);
  selectedUrls.value = next;
}

function isSelectable(status) {
  return status !== "queued";
}

function toggleSelectAll(checked) {
  if (checked) {
    selectedUrls.value = new Set(selectableImages.value.map((img) => img.url));
  } else {
    selectedUrls.value = new Set();
  }
}

function onImgError(url) {
  const next = new Set(brokenUrls.value);
  next.add(url);
  brokenUrls.value = next;
}

function statusLabel(status) {
  if (status === "alreadyImported") {
    return t("productMarketplaceIntegrations.importImagesAlreadyImported");
  }
  if (status === "queued") {
    return t("productMarketplaceIntegrations.importImagesQueued");
  }
  return t("productMarketplaceIntegrations.importImagesNew");
}

function onOpenChange(value) {
  emit("update:open", value);
  if (!value) emit("cancel");
}

function onConfirm() {
  if (confirmDisabled.value) return;
  emit("confirm", [...selectedUrls.value]);
}
</script>

<template>
  <Dialog :open="open" @update:open="onOpenChange">
    <DialogContent
      v-if="open"
      size="4xl"
      class="flex max-h-[90vh] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader variant="bar">
        <DialogTitle>
          {{ t("productMarketplaceIntegrations.importImagesTitle") }}
        </DialogTitle>
        <DialogDescription>
          {{ t("productMarketplaceIntegrations.importImagesDescription") }}
        </DialogDescription>
      </DialogHeader>

      <div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <FieldGroup>
          <FieldDescription>
            {{
              t("productMarketplaceIntegrations.importImagesAppendHint", {
                count: localGalleryCount,
              })
            }}
          </FieldDescription>

          <Field
            v-if="selectableImages.length > 0"
            orientation="horizontal"
            class="items-center"
          >
            <Checkbox
              id="import-images-select-all"
              :model-value="allSelectableSelected"
              :disabled="confirmLoading"
              @update:model-value="toggleSelectAll"
            />
            <FieldLabel for="import-images-select-all" class="cursor-pointer">
              {{ t("productMarketplaceIntegrations.importImagesSelectAll") }}
            </FieldLabel>
          </Field>

          <div
            class="grid grid-cols-2 gap-3 sm:grid-cols-3"
            data-slot="checkbox-group"
          >
            <Field v-for="(img, idx) in images" :key="img.url" class="min-w-0">
              <FieldLabel
                class="w-full cursor-pointer items-stretch rounded-lg border p-3 has-data-[state=checked]:border-primary"
                :class="
                  isSelectable(img.status) ? '' : 'cursor-default opacity-70'
                "
              >
                <div class="flex w-full flex-col gap-2">
                  <div class="flex items-start gap-2">
                    <Checkbox
                      :id="`import-image-${idx}`"
                      :model-value="isSelected(img.url)"
                      :disabled="!isSelectable(img.status) || confirmLoading"
                      @update:model-value="(v) => toggleUrl(img.url, v)"
                    />
                    <span class="text-sm font-medium leading-none">
                      {{
                        t("productMarketplaceIntegrations.importImagesPhotoN", {
                          n: idx + 1,
                        })
                      }}
                    </span>
                  </div>
                  <div
                    class="bg-muted relative aspect-square w-full overflow-hidden rounded-md"
                  >
                    <AppImage
                      v-if="!brokenUrls.has(img.url)"
                      :src="img.url"
                      :alt="
                        t('productMarketplaceIntegrations.importImagesPhotoN', {
                          n: idx + 1,
                        })
                      "
                      class="size-full"
                      img-class="size-full object-contain object-center"
                      @error="onImgError(img.url)"
                    />
                    <div
                      v-else
                      class="text-muted-foreground flex size-full items-center justify-center"
                    >
                      <ImageOff class="size-8" />
                    </div>
                  </div>
                  <span class="text-muted-foreground text-xs">
                    {{ statusLabel(img.status) }}
                  </span>
                </div>
              </FieldLabel>
            </Field>
          </div>
        </FieldGroup>
      </div>

      <DialogFooter variant="bar" class="sm:justify-between">
        <DialogClose as-child>
          <Button variant="outline" :disabled="confirmLoading">
            {{ t("common.cancel") }}
          </Button>
        </DialogClose>
        <Button :disabled="confirmDisabled" @click="onConfirm">
          <Spinner v-if="confirmLoading" class="mr-2" />
          <Download v-else class="mr-2 size-4" />
          {{
            t("productMarketplaceIntegrations.importImagesConfirm", {
              count: selectedUrls.size,
            })
          }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
