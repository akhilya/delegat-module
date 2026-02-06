<script>
// ImageFile - Displays an image from a file object with loading state and a placeholder fallback.
import { Image } from "lucide-vue-next";

export default {
  components: {
    Image,
  },
  props: {
    file: Object,
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
    'file.fullPath': {
      handler() {
        this.checkImageCache();
      },
    },
  },
  methods: {
    checkImageCache() {
      if (!this.file?.fullPath) {
        this.isLoading = false;
        return;
      }
      // Check if image is already cached
      const img = new window.Image();
      img.src = this.file.fullPath;
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
      :src="file.fullPath"
      class="absolute top-0 left-0 w-full h-full blur-3xl"
      v-show="!isLoading"
    />
    -->
    <img
      @load="onImageLoad"
      @error="onImageError"
      :src="file.fullPath"
      class="absolute w-full h-full object-contain"
      v-show="!isLoading"
    />
    <slot />
  </div>
  <div v-else class="bg-secondary rounded-lg flex items-center justify-center">
    <Image class="text-secondary-foreground size-[25%]" />
    <slot />
  </div>
</template>
