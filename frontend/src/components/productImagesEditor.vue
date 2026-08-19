<script>
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
// ProductImagesEditor - Drag-and-drop editor for reordering product card images and managing unused images.
import draggableComponent from "vuedraggable";
import ImageFile from "./imageFile.vue";
import { Image, GripVertical } from "lucide-vue-next";
import { useStore } from "vuex";
import { useDebounceFn } from "@vueuse/core";
import { displaySonnerError } from "@/store/sonnerHelper";
import { EmptyState } from "@/components/ui/empty-state";
import { InsetGroupLabel } from "@/components/ui/inset-group";

const TILE_CLASS =
  "size-28 shrink-0 !rounded-xl ring-1 ring-inset ring-border/70 sm:size-32";

export default {
  components: {
    Spinner,
    Button,
    draggableComponent,
    ImageFile,
    Image,
    GripVertical,
    EmptyState,
    InsetGroupLabel,
  },
  emits: ["update:images", "preview"],
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
      handler() {
        this.rebuildImageLists();
      },
      immediate: true,
    },
    images: {
      handler() {
        this.rebuildImageLists();
      },
    },
  },
  data() {
    return {
      usedImages: [],
      unusedImages: [],
      isSaving: false,
      TILE_CLASS,
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
      return `/api/files/${file.id}/thumbnail?w=256`;
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
      this.$emit(
        "update:images",
        this.usedImages.map((f, idx) => ({
          id: f.id,
          fileId: f.id,
          index: idx,
        })),
      );
    },
  },
};
</script>

<template>
  <div class="flex flex-col gap-8">
    <section>
      <div class="mb-3 flex items-start justify-between gap-3">
        <div class="min-w-0">
          <InsetGroupLabel class="mb-1">
            {{ $t("components.productImagesEditor.productImagesTitle") }}
          </InsetGroupLabel>
          <p class="px-1 text-xs text-muted-foreground">
            {{ $t("components.productImagesEditor.dragToReorderHint") }}
          </p>
        </div>
        <Spinner
          v-if="isSaving"
          size="sm"
          class="mt-0.5 shrink-0 text-muted-foreground"
        />
      </div>
      <div class="min-h-28 rounded-xl p-2.5 ring-1 ring-dashed ring-border/70">
        <draggableComponent
          :list="usedImages"
          group="images"
          item-key="id"
          handle=".drag-handle"
          :animation="200"
          ghost-class="drag-ghost"
          chosen-class="drag-chosen"
          drag-class="drag-active"
          class="flex flex-wrap gap-2.5"
          @change="onDragChange"
        >
          <template #item="{ element, index }">
            <ImageFile
              :file="element"
              :thumbnail-url="thumbnailUrl(element)"
              :class="TILE_CLASS"
              @click="$emit('preview', element.id)"
            >
              <span
                class="absolute left-1.5 top-1.5 z-10 rounded-md bg-background/90 px-1.5 py-0.5 text-[11px] font-medium tabular-nums text-foreground ring-1 ring-border/70"
              >
                {{
                  index === 0
                    ? $t("products.productGallerySidebar.mainBadge")
                    : index + 1
                }}
              </span>
              <Button
                type="button"
                variant="outline"
                size="icon"
                class="drag-handle absolute right-1.5 top-1.5 z-10 size-7 cursor-grab bg-background/90 active:cursor-grabbing"
                :aria-label="
                  $t('components.productImagesEditor.dragHandleAria')
                "
                @click.stop
              >
                <GripVertical class="size-4" />
              </Button>
            </ImageFile>
          </template>
          <template #footer>
            <div
              v-if="usedImages.length === 0"
              class="flex min-h-24 w-full items-center justify-center"
            >
              <EmptyState
                :framed="false"
                size="sm"
                class="flex-none p-4"
                :title="$t('components.productImagesEditor.usedEmptyTitle')"
                :description="
                  $t('components.productImagesEditor.usedEmptyDescription')
                "
              >
                <template #icon>
                  <Image />
                </template>
              </EmptyState>
            </div>
          </template>
        </draggableComponent>
      </div>
    </section>

    <section>
      <div class="mb-3">
        <InsetGroupLabel class="mb-1">
          {{ $t("components.productImagesEditor.otherImagesTitle") }}
        </InsetGroupLabel>
        <p class="px-1 text-xs text-muted-foreground">
          {{
            unusedImages.length
              ? $t("components.productImagesEditor.otherImagesHint")
              : $t("components.productImagesEditor.otherEmptyDescription")
          }}
        </p>
      </div>
      <div class="min-h-28 rounded-xl p-2.5 ring-1 ring-dashed ring-border/70">
        <draggableComponent
          :list="unusedImages"
          group="images"
          item-key="id"
          handle=".drag-handle"
          :animation="200"
          ghost-class="drag-ghost"
          chosen-class="drag-chosen"
          drag-class="drag-active"
          class="flex min-h-24 flex-wrap gap-2.5"
          @change="onDragChange"
        >
          <template #item="{ element }">
            <ImageFile
              :file="element"
              :thumbnail-url="thumbnailUrl(element)"
              :class="TILE_CLASS"
              @click="$emit('preview', element.id)"
            >
              <Button
                type="button"
                variant="outline"
                size="icon"
                class="drag-handle absolute right-1.5 top-1.5 z-10 size-7 cursor-grab bg-background/90 active:cursor-grabbing"
                :aria-label="
                  $t('components.productImagesEditor.dragHandleAria')
                "
                @click.stop
              >
                <GripVertical class="size-4" />
              </Button>
            </ImageFile>
          </template>
        </draggableComponent>
      </div>
    </section>
  </div>
</template>
