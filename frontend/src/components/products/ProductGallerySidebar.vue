<script setup>
import { Button } from "@/components/ui/button";
import { Play, Plus, X } from "lucide-vue-next";
import { computed } from "vue";
import { Spinner } from "@/components/ui/spinner";
import AppImage from "@/components/AppImage.vue";
import draggable from "vuedraggable";
import { t } from "@/i18n";
import { getMediaKind } from "@/lib/mediaKind";

const images = defineModel("images", { type: Array, required: true });
const mainImageFileId = computed(
  () =>
    images.value.find((item) => getMediaKind(item.contentType) === "image")
      ?.fileId,
);

defineProps({
  isUploading: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
  layout: {
    type: String,
    default: "sidebar",
    validator: (v) => ["sidebar", "inline"].includes(v),
  },
});

const emit = defineEmits(["drag-change", "remove", "upload", "preview"]);

function thumbSrc(element) {
  return (
    element.previewUrl ||
    (isVideo(element)
      ? `/api/files/${element.fileId}/download`
      : `/api/files/${element.fileId}/thumbnail?w=256`)
  );
}

function isVideo(element) {
  return getMediaKind(element?.contentType) === "video";
}

function isMainImage(element) {
  return (
    mainImageFileId.value != null &&
    Number(mainImageFileId.value) === Number(element?.fileId)
  );
}
</script>

<template>
  <div
    :class="[
      layout === 'sidebar'
        ? 'product-gallery-sidebar hidden lg:flex shrink-0 flex-col self-start overflow-hidden rounded-xl border bg-card text-card-foreground sticky top-0 h-[var(--app-fill-pane-height)]'
        : 'flex w-full flex-col gap-3',
    ]"
  >
    <div
      :class="[
        layout === 'sidebar'
          ? 'thin-scrollbar min-h-0 flex-1 overflow-y-auto p-2'
          : '',
        disabled ? 'pointer-events-none opacity-50' : '',
      ]"
    >
      <draggable
        v-if="images?.length"
        :list="images"
        item-key="fileId"
        :animation="200"
        ghost-class="drag-ghost"
        chosen-class="drag-chosen"
        drag-class="drag-active"
        :class="[
          layout === 'sidebar'
            ? 'flex flex-col gap-1.5'
            : 'flex flex-wrap gap-2',
        ]"
        @change="emit('drag-change')"
      >
        <template #item="{ element, index }">
          <div
            :class="[
              'gallery-item group relative overflow-hidden rounded-lg bg-muted cursor-grab active:cursor-grabbing hover:ring-2 ring-primary',
              layout === 'sidebar'
                ? 'aspect-square w-full'
                : 'size-28 sm:size-32',
              isMainImage(element) ? 'ring-2 ring-primary' : '',
            ]"
          >
            <Button
              type="button"
              variant="ghost"
              size="icon"
              class="media-preview-button absolute inset-0 z-10 flex size-full items-center justify-center overflow-hidden rounded-lg border-0 bg-transparent p-0 text-inherit"
              :aria-label="
                t(
                  isVideo(element)
                    ? 'products.productGallerySidebar.openVideo'
                    : 'products.productGallerySidebar.openPhoto',
                )
              "
              @click="emit('preview', index)"
            >
              <div
                v-if="isVideo(element)"
                class="pointer-events-none absolute inset-0 flex size-full items-center justify-center bg-muted"
                aria-hidden="true"
              >
                <Play class="size-8 fill-current text-muted-foreground" />
              </div>
              <AppImage
                v-else
                :src="thumbSrc(element)"
                :alt="
                  element.displayName ||
                  t('products.productGallerySidebar.productPhotoAlt')
                "
                blur-backdrop
                spinner-size="sm"
                class="pointer-events-none absolute inset-0 size-full"
                img-class="pointer-events-none relative z-10 size-full object-contain"
              />
              <div
                v-if="isVideo(element)"
                class="pointer-events-none absolute bottom-1.5 left-1.5 z-20 rounded bg-black/70 px-1.5 py-0.5 text-[10px] font-medium text-white"
                aria-hidden="true"
              >
                {{ t("products.productGallerySidebar.videoBadge") }}
              </div>
              <div
                v-if="isMainImage(element)"
                class="pointer-events-none absolute top-1.5 left-1.5 z-20 rounded bg-primary px-1.5 py-0.5 text-[10px] font-medium text-primary-foreground"
                aria-hidden="true"
              >
                {{ t("products.productGallerySidebar.mainBadge") }}
              </div>
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              :disabled="disabled"
              class="gallery-remove absolute top-1.5 right-1.5 z-20 size-6 rounded-md bg-black/60 text-white hover:bg-destructive"
              :aria-label="t('products.productGallerySidebar.removePhoto')"
              @click.stop="emit('remove', index)"
            >
              <X class="size-3" />
            </Button>
          </div>
        </template>
        <template #footer>
          <Button
            type="button"
            variant="ghost"
            :disabled="disabled || isUploading"
            :aria-label="t('products.productGallerySidebar.uploadPhoto')"
            :class="[
              'flex h-auto min-h-0 cursor-pointer items-center justify-center rounded-lg border border-dashed border-muted-foreground/30 bg-transparent p-0 transition-colors hover:border-primary/50 hover:bg-transparent active:scale-[0.99]',
              layout === 'sidebar'
                ? 'aspect-square w-full'
                : 'size-28 sm:size-32',
            ]"
            @click="emit('upload')"
          >
            <Spinner
              v-if="isUploading"
              size="lg"
              class="text-muted-foreground"
            />
            <Plus v-else class="size-5 text-muted-foreground" />
          </Button>
        </template>
      </draggable>

      <Button
        v-else
        type="button"
        variant="ghost"
        :disabled="disabled || isUploading"
        :aria-label="t('products.productGallerySidebar.uploadPhoto')"
        :class="[
          'flex h-auto min-h-0 cursor-pointer items-center justify-center rounded-lg border border-dashed border-muted-foreground/30 bg-transparent p-0 transition-colors hover:border-primary/50 hover:bg-transparent active:scale-[0.99]',
          layout === 'sidebar' ? 'aspect-square w-full' : 'size-28 sm:size-32',
        ]"
        @click="emit('upload')"
      >
        <Spinner v-if="isUploading" size="lg" class="text-muted-foreground" />
        <Plus v-else class="size-5 text-muted-foreground" />
      </Button>
    </div>
  </div>
</template>

<style scoped>
.product-gallery-sidebar {
  width: clamp(10rem, 14vw, 14rem);
}

@media (hover: hover) and (pointer: fine) {
  .gallery-remove {
    opacity: 0;
    transition: opacity 150ms cubic-bezier(0.25, 1, 0.5, 1);
  }

  .gallery-item:hover .gallery-remove,
  .gallery-remove:focus-visible {
    opacity: 1;
  }
}
</style>

<style>
.drag-ghost {
  opacity: 0.3;
  border: 2px dashed var(--primary);
  border-radius: var(--radius-md);
}
.drag-chosen {
  box-shadow: none;
  border-radius: var(--radius-md);
}
.drag-active {
  opacity: 1 !important;
  transform: scale(1.03);
  box-shadow: none;
  border-radius: var(--radius-md);
  cursor: grabbing !important;
  z-index: 50;
}
</style>
