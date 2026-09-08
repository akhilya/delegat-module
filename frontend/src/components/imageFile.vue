<script>
// ImageFile - Displays an image from a file object with loading spinner and a placeholder fallback.
import { Image } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import { imagePaintState } from "@/lib/deferredImage";

export default {
  components: {
    Image,
    Spinner,
  },
  props: {
    file: Object,
    thumbnailUrl: String,
  },
  emits: ["load", "error"],
  computed: {
    resolvedSrc() {
      return this.thumbnailUrl || this.file?.fullPath;
    },
    showPhoto() {
      return Boolean(this.file && this.resolvedSrc && !this.failed);
    },
  },
  data() {
    return {
      isLoading: true,
      failed: false,
    };
  },
  mounted() {
    this.syncFromImg();
  },
  watch: {
    resolvedSrc() {
      this.applyPaintState(imagePaintState(this.resolvedSrc, null));
      this.$nextTick(() => this.syncFromImg());
    },
  },
  methods: {
    applyPaintState(state) {
      this.isLoading = state.isLoading;
      this.failed = state.failed;
    },
    syncFromImg() {
      const src = this.resolvedSrc;
      const next = imagePaintState(src, this.$refs.imgEl);
      this.applyPaintState(next);
      if (src && next.failed) this.$emit("error");
    },
    onImageLoad() {
      this.applyPaintState({ isLoading: false, failed: false });
      this.$emit("load");
    },
    onImageError() {
      this.applyPaintState({ isLoading: false, failed: true });
      this.$emit("error");
    },
  },
};
</script>

<template>
  <div
    v-if="showPhoto"
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
      ref="imgEl"
      loading="lazy"
      decoding="async"
      referrerpolicy="no-referrer"
      @load="onImageLoad"
      @error="onImageError"
      :src="resolvedSrc"
      :alt="
        file?.displayName ||
        file?.fileName ||
        $t('components.imageFile.altFallback')
      "
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
