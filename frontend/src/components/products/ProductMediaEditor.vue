<script setup>
import { ref, useId, watch } from "vue";
import { InsetGroupLabel } from "@/components/ui/inset-group";
import ProductGallerySidebar from "@/components/products/ProductGallerySidebar.vue";
import { availableGalleryMedia } from "@/lib/productGalleryMedia";
import { t } from "@/i18n";

const images = defineModel("images", { type: Array, required: true });
const props = defineProps({
  files: { type: Array, default: () => [] },
  isUploading: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
});
const emit = defineEmits(["drag-change", "remove", "add", "upload", "preview"]);
const dragGroup = `product-media-${useId()}`;
const available = ref([]);
function syncAvailable() {
  available.value = availableGalleryMedia(props.files, images.value);
}
// Keep a mutable drag list, then reconcile it from the selected gallery after each change.
watch([() => props.files, images], syncAvailable, {
  immediate: true,
  deep: true,
});
</script>

<template>
  <div class="flex flex-col gap-8">
    <section class="min-w-0">
      <InsetGroupLabel class="mb-3">
        {{ t("components.productMediaEditor.galleryTitle") }}
      </InsetGroupLabel>
      <ProductGallerySidebar
        v-model:images="images"
        layout="grid"
        :drag-group="dragGroup"
        :is-uploading="isUploading"
        :disabled="disabled"
        @drag-change="emit('drag-change')"
        @drag-end="syncAvailable"
        @remove="emit('remove', $event)"
        @upload="emit('upload')"
        @preview="emit('preview', images[$event].fileId)"
      />
    </section>
    <section class="min-w-0">
      <InsetGroupLabel class="mb-1">
        {{ t("components.productMediaEditor.availableTitle") }}
      </InsetGroupLabel>
      <p class="mb-3 px-1 text-xs text-muted-foreground">
        {{ t("components.productMediaEditor.availableHint") }}
      </p>
      <ProductGallerySidebar
        :images="available"
        layout="grid"
        mode="library"
        :drag-group="dragGroup"
        :disabled="disabled"
        @add="emit('add', available[$event])"
        @drag-end="syncAvailable"
        @preview="emit('preview', available[$event].fileId)"
      />
    </section>
  </div>
</template>
