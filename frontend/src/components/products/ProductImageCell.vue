<script>
/**
 * Product table image thumb with delayed hover preview of the full image.
 */
import ImageFile from "@/components/imageFile.vue";
import AppImage from "@/components/AppImage.vue";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { productImageSizeId } from "@/composables/useProductImageSize";
import { productImageSizeSpec } from "@/lib/productImageSize";

export default {
  name: "ProductImageCell",
  components: {
    ImageFile,
    AppImage,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
  },
  props: {
    thumbnailUrl: {
      type: String,
      default: null,
    },
    imageUrl: {
      type: String,
      default: null,
    },
    imageClass: {
      type: String,
      default: "size-32",
    },
    /** Use the shared catalog / orders / scrape photo size instead of imageClass. */
    followTableSize: {
      type: Boolean,
      default: false,
    },
    /** Stop pointer/click so table row / menu handlers do not fire (disable inside labels). */
    stopEvents: {
      type: Boolean,
      default: true,
    },
  },
  computed: {
    resolvedImageClass() {
      if (this.followTableSize) {
        return productImageSizeSpec(productImageSizeId.value).imageOnlyClass;
      }
      return this.imageClass;
    },
    thumbSrc() {
      return this.thumbnailUrl || this.imageUrl || null;
    },
    previewSrc() {
      return this.imageUrl || this.thumbnailUrl || null;
    },
    file() {
      return this.thumbSrc ? { fullPath: this.thumbSrc } : null;
    },
  },
  data() {
    return {
      thumbFailed: false,
    };
  },
  watch: {
    thumbSrc() {
      this.thumbFailed = false;
    },
  },
};
</script>

<template>
  <TooltipProvider v-if="previewSrc && !thumbFailed" :delay-duration="300">
    <Tooltip>
      <TooltipTrigger as-child>
        <div
          class="inline-flex"
          @pointerdown="stopEvents && $event.stopPropagation()"
          @click="stopEvents && $event.stopPropagation()"
        >
          <slot>
            <ImageFile
              :class="resolvedImageClass"
              :file="file"
              @error="thumbFailed = true"
            />
          </slot>
        </div>
      </TooltipTrigger>
      <TooltipContent
        side="right"
        :side-offset="8"
        class="max-w-none overflow-hidden p-1.5"
      >
        <AppImage
          :src="previewSrc"
          alt=""
          spinner-size="default"
          class="flex size-64 items-center justify-center"
          img-class="max-h-64 max-w-64 rounded-sm object-contain"
        />
      </TooltipContent>
    </Tooltip>
  </TooltipProvider>
  <template v-else>
    <slot>
      <ImageFile
        :class="resolvedImageClass"
        :file="thumbFailed ? null : file"
        @error="thumbFailed = true"
      />
    </slot>
  </template>
</template>
