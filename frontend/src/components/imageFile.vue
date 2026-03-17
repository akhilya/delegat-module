<script>
// ImageFile - Displays an image from a file object with loading state and a placeholder fallback.
import { Image } from "lucide-vue-next";

export default {
  components: {
    Image,
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
      // Check if image is already cached
      const img = new window.Image();
      img.src = this.resolvedSrc;
      if (img.complete) {
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
    class="bg-secondary rounded-lg overflow-hidden relative"
    :class="{
      ' animate-pulse': isLoading,
    }"
  >
    <!--
    <img
      :src="resolvedSrc"
      class="absolute top-0 left-0 w-full h-full blur-3xl"
      v-show="!isLoading"
    />
    -->
    <img
      @load="onImageLoad"
      @error="onImageError"
      :src="resolvedSrc"
      :alt="file?.displayName || file?.fileName || 'Изображение товара'"
      class="absolute w-full h-full object-contain"
      v-show="!isLoading"
    />
    <slot />
  </div>
  <div v-else class="bg-secondary rounded-lg flex items-center justify-center">
    <Image class="text-secondary-foreground size-[25%]" aria-hidden="true" />
    <slot />
  </div>
</template>
