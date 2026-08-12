<script>
// ImageFile - Displays an image from a file object with loading spinner and a placeholder fallback.
import { Image } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";

export default {
  components: {
    Image,
    Spinner,
  },
  props: {
    file: Object,
    thumbnailUrl: String,
  },
  computed: {
    resolvedSrc() {
      return this.thumbnailUrl || this.file?.fullPath;
    },
  },
  data() {
    return {
      isLoading: true,
    };
  },
  mounted() {
    this.checkImageCache();
  },
  watch: {
    resolvedSrc: {
      handler() {
        this.checkImageCache();
      },
    },
  },
  methods: {
    checkImageCache() {
      if (!this.resolvedSrc) {
        this.isLoading = false;
        return;
      }
      const img = new window.Image();
      img.src = this.resolvedSrc;
      if (img.complete && img.naturalWidth > 0) {
        this.isLoading = false;
      } else {
        this.isLoading = true;
      }
    },
    onImageLoad() {
      this.isLoading = false;
    },
    onImageError() {
      this.isLoading = false;
    },
  },
};
</script>

<template>
  <div
    v-if="file"
    class="bg-secondary relative overflow-hidden rounded-lg"
  >
    <div
      v-if="isLoading"
      class="absolute inset-0 z-10 flex items-center justify-center"
      aria-hidden="true"
    >
      <Spinner size="sm" class="text-muted-foreground" />
    </div>
    <img
      @load="onImageLoad"
      @error="onImageError"
      :src="resolvedSrc"
      :alt="file?.displayName || file?.fileName || $t('components.imageFile.altFallback')"
      class="absolute h-full w-full object-contain"
      :class="isLoading ? 'opacity-0' : 'opacity-100'"
    />
    <slot />
  </div>
  <div v-else class="bg-secondary flex items-center justify-center rounded-lg">
    <Image class="text-secondary-foreground size-[25%]" aria-hidden="true" />
    <slot />
  </div>
</template>
