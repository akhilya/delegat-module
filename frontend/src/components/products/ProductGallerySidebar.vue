<script setup>
import { Button } from "@/components/ui/button";
import { Plus, X } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import AppImage from "@/components/AppImage.vue";
import draggable from "vuedraggable";
import { t } from "@/i18n";

const images = defineModel("images", { type: Array, required: true });

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
  return element.previewUrl || `/api/files/${element.fileId}/thumbnail?w=256`;
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
              index === 0 ? 'ring-2 ring-primary' : '',
            ]"
            @click="emit('preview', index)"
          >
            <!-- Blurred fill so letterboxed contain never shows empty muted bands -->
            <AppImage
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
              v-if="index === 0"
              class="absolute top-1.5 left-1.5 z-20 rounded bg-primary px-1.5 py-0.5 text-[10px] font-medium text-primary-foreground"
            >
              {{ t("products.productGallerySidebar.mainBadge") }}
            </div>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              :disabled="disabled"
              class="absolute top-1.5 right-1.5 z-20 size-6 rounded-md bg-black/60 text-white opacity-0 transition-opacity hover:bg-destructive group-hover:opacity-100"
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
