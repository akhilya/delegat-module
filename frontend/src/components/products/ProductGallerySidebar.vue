<script setup>
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Plus, X } from "lucide-vue-next";
import { computed } from "vue";
import { Spinner } from "@/components/ui/spinner";
import AppImage from "@/components/AppImage.vue";
import ProductVideoThumbnail from "@/components/products/ProductVideoThumbnail.vue";
import draggable from "vuedraggable";
import { t } from "@/i18n";
import { getMediaKind } from "@/lib/mediaKind";

const images = defineModel("images", { type: Array, required: true });
const mainImageFileId = computed(
  () =>
    images.value.find((item) => getMediaKind(item.contentType) === "image")
      ?.fileId,
);

const props = defineProps({
  mode: {
    type: String,
    default: "gallery",
    validator: (v) => ["gallery", "library"].includes(v),
  },
  isUploading: { type: Boolean, default: false },
  dragGroup: { type: String, default: null },
  disabled: { type: Boolean, default: false },
  layout: {
    type: String,
    default: "sidebar",
    validator: (v) => ["sidebar", "inline", "grid"].includes(v),
  },
});

const emit = defineEmits([
  "drag-change",
  "drag-end",
  "remove",
  "add",
  "upload",
  "preview",
]);
const MEDIA_GRID_CLASS =
  "grid grid-cols-[repeat(auto-fill,minmax(min(100%,12rem),1fr))] gap-3";

function thumbSrc(element) {
  return (
    element.previewUrl ||
    (isVideo(element)
      ? `/api/files/${element.fileId}/download`
      : `/api/files/${element.fileId}/thumbnail?w=${props.layout === "grid" ? 512 : 256}`)
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
          : layout === 'grid' && !images.length && !dragGroup
            ? MEDIA_GRID_CLASS
            : '',
        disabled ? 'pointer-events-none opacity-50' : '',
      ]"
    >
      <draggable
        v-if="images?.length || dragGroup"
        :list="images"
        :group="dragGroup || undefined"
        :sort="mode === 'gallery'"
        :disabled="disabled || (mode === 'library' && !dragGroup)"
        :data-gallery-mode="mode"
        item-key="fileId"
        :animation="200"
        ghost-class="drag-ghost"
        chosen-class="drag-chosen"
        drag-class="drag-active"
        :class="[
          layout === 'sidebar'
            ? 'flex flex-col gap-1.5'
            : layout === 'grid'
              ? MEDIA_GRID_CLASS
              : 'flex flex-wrap gap-2',
        ]"
        @change="emit('drag-change', $event)"
        @end="emit('drag-end')"
      >
        <template #item="{ element, index }">
          <div
            :data-file-id="element.fileId"
            :class="[
              'gallery-item group relative overflow-hidden rounded-lg bg-muted hover:ring-2 ring-primary',
              mode === 'gallery' || dragGroup
                ? 'cursor-grab active:cursor-grabbing'
                : '',
              layout !== 'inline'
                ? 'aspect-square w-full min-w-0'
                : 'size-28 sm:size-32',
              mode === 'gallery' && isMainImage(element)
                ? 'ring-2 ring-primary'
                : '',
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
              <ProductVideoThumbnail
                v-if="isVideo(element)"
                :src="thumbSrc(element)"
                class="pointer-events-none absolute inset-0"
              />
              <AppImage
                v-else
                :src="thumbSrc(element)"
                :alt="
                  element.displayName ||
                  t('products.productGallerySidebar.productPhotoAlt')
                "
                spinner-size="sm"
                class="pointer-events-none absolute inset-0 size-full"
                img-class="pointer-events-none relative z-10 size-full object-cover"
              />
              <Badge
                v-if="isVideo(element)"
                variant="secondary"
                class="pointer-events-none absolute bottom-1.5 left-1.5 z-20"
                aria-hidden="true"
              >
                {{ t("products.productGallerySidebar.videoBadge") }}
              </Badge>
              <Badge
                v-if="mode === 'gallery' && isMainImage(element)"
                class="pointer-events-none absolute top-1.5 left-1.5 z-20"
                aria-hidden="true"
              >
                {{ t("products.productGallerySidebar.mainBadge") }}
              </Badge>
            </Button>
            <Button
              type="button"
              variant="outline"
              size="icon"
              :disabled="disabled"
              class="gallery-remove absolute top-1.5 right-1.5 z-20 size-6 rounded-md bg-background text-foreground"
              :class="
                mode === 'library'
                  ? ''
                  : 'hover:bg-destructive hover:text-destructive-foreground'
              "
              :aria-label="
                t(
                  mode === 'library'
                    ? 'products.productGallerySidebar.addToGallery'
                    : 'products.productGallerySidebar.removePhoto',
                )
              "
              @click.stop="emit(mode === 'library' ? 'add' : 'remove', index)"
            >
              <Plus v-if="mode === 'library'" class="size-3" />
              <X v-else class="size-3" />
            </Button>
          </div>
        </template>
        <template #footer>
          <div
            v-if="mode === 'library' && !images.length"
            class="col-span-full flex min-h-48 items-center justify-center rounded-lg border border-dashed border-border px-4 text-center text-sm text-muted-foreground"
          >
            {{ t("products.productGallerySidebar.dropToRemove") }}
          </div>
          <Button
            v-if="mode === 'gallery'"
            type="button"
            variant="ghost"
            :disabled="disabled || isUploading"
            :aria-label="t('products.productGallerySidebar.uploadPhoto')"
            :class="[
              'flex h-auto min-h-0 cursor-pointer items-center justify-center rounded-lg border border-dashed border-muted-foreground/30 bg-transparent p-0 transition-colors hover:border-primary/50 hover:bg-transparent active:scale-[0.99]',
              layout !== 'inline'
                ? 'aspect-square w-full min-w-0'
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
        v-else-if="mode === 'gallery'"
        type="button"
        variant="ghost"
        :disabled="disabled || isUploading"
        :aria-label="t('products.productGallerySidebar.uploadPhoto')"
        :class="[
          'flex h-auto min-h-0 cursor-pointer items-center justify-center rounded-lg border border-dashed border-muted-foreground/30 bg-transparent p-0 transition-colors hover:border-primary/50 hover:bg-transparent active:scale-[0.99]',
          layout !== 'inline'
            ? 'aspect-square w-full min-w-0'
            : 'size-28 sm:size-32',
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
