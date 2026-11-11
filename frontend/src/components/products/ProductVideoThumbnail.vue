<script setup>
import { onBeforeUnmount, onMounted, ref, watch } from "vue";
import { Play } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import { loadVideoThumbnail } from "@/lib/videoThumbnail";
import { cn } from "@/lib/utils";

const props = defineProps({
  src: { type: String, default: null },
  class: { type: null, default: undefined },
});

const root = ref(null);
const video = ref(null);
const visible = ref(false);
const ready = ref(false);
const failed = ref(false);
let observer;

onMounted(() => {
  if (typeof IntersectionObserver === "undefined") {
    visible.value = true;
    return;
  }
  observer = new IntersectionObserver(
    (entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        visible.value = true;
        observer.disconnect();
      }
    },
    { rootMargin: "128px" },
  );
  observer.observe(root.value);
});

watch(
  [video, () => props.src],
  ([element, src], _, onCleanup) => {
    ready.value = false;
    failed.value = false;
    if (!element || !src) return;
    onCleanup(
      loadVideoThumbnail(element, src, {
        onReady: () => {
          ready.value = true;
        },
        onError: () => {
          failed.value = true;
        },
      }),
    );
  },
  { flush: "post" },
);

onBeforeUnmount(() => observer?.disconnect());
</script>

<template>
  <div
    ref="root"
    :class="cn('relative size-full overflow-hidden bg-muted', props.class)"
    aria-hidden="true"
  >
    <video
      v-if="visible && src"
      ref="video"
      muted
      playsinline
      preload="metadata"
      tabindex="-1"
      class="pointer-events-none absolute inset-0 size-full object-contain"
      :class="ready ? 'opacity-100' : 'opacity-0'"
    />
    <div class="absolute inset-0 flex items-center justify-center">
      <Spinner
        v-if="visible && src && !ready && !failed"
        size="sm"
        class="text-muted-foreground"
      />
      <span
        v-else-if="ready"
        class="flex size-9 items-center justify-center rounded-full bg-background text-foreground shadow-sm"
      >
        <Play class="size-5" />
      </span>
      <Play v-else class="size-8 text-muted-foreground" />
    </div>
  </div>
</template>
