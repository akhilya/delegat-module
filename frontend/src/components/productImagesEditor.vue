<script>
// ProductImagesEditor - Drag-and-drop editor for reordering product card images and managing unused images.
import draggableComponent from "vuedraggable";
import ImageFile from "./imageFile.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { Image, ImageOff, Loader2 } from "lucide-vue-next";
import { useStore } from "vuex";
import { useDebounceFn } from "@vueuse/core";
import { displaySonnerError } from "@/store/sonnerHelper";

export default {
  components: {
    draggableComponent,
    ImageFile,
    Badge,
    Image,
    ImageOff,
    Loader2,
  },
  emits: ['update:images', 'preview'],
  props: {
    files: {
      type: Array,
      required: true,
    },
    images: {
      type: Array,
      required: true,
    },
    productId: {
      type: Number,
      required: true,
    },
  },
  watch: {
    files: {
      handler() { this.rebuildImageLists(); },
      immediate: true,
    },
    images: {
      handler() { this.rebuildImageLists(); },
    },
  },
  data() {
    return {
      usedImages: [],
      unusedImages: [],
      isSaving: false,
    };
  },
  setup() {
    const store = useStore();

    const debouncedSave = useDebounceFn(async function (vm) {
      vm.isSaving = true;
      await store.dispatch("postProductImages", {
        productId: vm.productId,
        files: vm.usedImages.map((f) => f.id),
        onSuccess: () => {},
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      vm.isSaving = false;
    }, 400);

    return { debouncedSave };
  },
  methods: {
    thumbnailUrl(file) {
      return `/api/files/${file.id}/thumbnail?w=512`;
    },
    rebuildImageLists() {
      const imageFiles = [];
      this.files.forEach((file) => {
        if (file.contentType.includes("image")) {
          imageFiles.push(file);
        }
      });
      this.usedImages = [];
      this.unusedImages = [];
      for (let imageKey in imageFiles) {
        let imageFile = imageFiles[imageKey];
        let image = this.images.find((i) => i.fileId == imageFile.id);
        if (image != undefined) {
          this.usedImages.push({ ...imageFile, index: image.index });
        } else {
          this.unusedImages.push(imageFile);
        }
      }
      this.usedImages = this.usedImages.sort((a, b) => a.index - b.index);
    },
    onDragChange() {
      this.debouncedSave(this);
      this.$emit('update:images', this.usedImages.map((f, idx) => ({
        id: f.id,
        fileId: f.id,
        index: idx,
      })));
    },
  },
};
</script>

<template>
  <div class="grow flex flex-col min-h-0">
    <!-- Attached images section -->
    <div class="flex-1 min-h-0 flex flex-col">
      <div class="flex items-center gap-2 px-4 h-11 border-b shrink-0">
        <p class="text-sm font-medium flex items-center gap-2">
          <Image class="size-4" />
          {{ $t('components.productImagesEditor.productImagesTitle') }}
        </p>
        <Loader2 v-if="isSaving" class="size-4 animate-spin text-muted-foreground" />
        <span v-if="!isSaving" class="text-xs text-muted-foreground">- {{ $t('components.productImagesEditor.dragToReorderHint') }}</span>
      </div>
      <draggableComponent
        :list="usedImages"
        group="images"
        @change="onDragChange"
        itemKey="id"
        :animation="200"
        ghostClass="drag-ghost"
        chosenClass="drag-chosen"
        dragClass="drag-active"
        class="flex flex-wrap flex-1 min-h-0 overflow-y-auto gap-4 p-4"
      >
        <template #item="{ element, index }">
          <ImageFile :file="element" :thumbnailUrl="thumbnailUrl(element)" class="size-48 cursor-grab active:cursor-grabbing" @click="$emit('preview', element.id)">
            <Badge class="text-sm absolute left-2 top-2">
              {{ index + 1 }}
            </Badge>
          </ImageFile>
        </template>
      </draggableComponent>
    </div>

    <!-- Other images section -->
    <div class="flex-1 min-h-0 flex flex-col">
      <div class="flex items-center px-4 h-11 border-t border-b shrink-0">
        <p class="text-sm font-medium flex items-center gap-2">
          <ImageOff class="size-4" />
          {{ $t('components.productImagesEditor.otherImagesTitle') }}
        </p>
      </div>
      <draggableComponent
        :list="unusedImages"
        group="images"
        @change="onDragChange"
        itemKey="id"
        :animation="200"
        ghostClass="drag-ghost"
        chosenClass="drag-chosen"
        dragClass="drag-active"
        class="flex flex-wrap flex-1 min-h-0 overflow-y-auto gap-4 p-4"
      >
        <template #item="{ element, index }">
          <ImageFile :file="element" :thumbnailUrl="thumbnailUrl(element)" class="size-48 cursor-grab active:cursor-grabbing" @click="$emit('preview', element.id)" />
        </template>
      </draggableComponent>
    </div>
  </div>
</template>

<style>
/* Ghost: the placeholder left behind in the list */
.drag-ghost {
  opacity: 0.3;
  border: 2px dashed hsl(var(--primary));
  border-radius: 0.5rem;
}

/* Chosen: the element being picked up (before moving) */
.drag-chosen {
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  border-radius: 0.5rem;
}

/* Active: the clone that follows the cursor */
.drag-active {
  opacity: 1 !important;
  transform: scale(1.05) rotate(2deg);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.2);
  border-radius: 0.5rem;
  cursor: grabbing !important;
  z-index: var(--z-overlay, 50);
}
</style>
