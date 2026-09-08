<script setup>
/**
 * Content image with centered Spinner while the network/decode finishes.
 * Parent should give the wrapper a size (e.g. size-full inside a sized box).
 */
import { ref, watch } from "vue";
import { Image } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import { cn } from "@/lib/utils";

defineOptions({ inheritAttrs: false });

const props = defineProps({
  src: { type: String, default: null },
  alt: { type: String, default: "" },
  spinnerSize: {
    type: String,
    default: "sm",
    validator: (v) => ["xs", "sm", "default", "lg", "xl", "2xl"].includes(v),
  },
  /** Classes on the primary <img> */
  imgClass: { type: String, default: "relative z-10 size-full object-contain" },
  /** Soft blurred fill behind contain (letterbox) */
  blurBackdrop: { type: Boolean, default: false },
  /** Extra classes on the wrapper (merged with relative overflow-hidden) */
  class: { type: null, default: undefined },
  /** Extra classes on the Spinner (e.g. contrast on a dim overlay) */
  spinnerClass: { type: null, default: undefined },
});

const emit = defineEmits(["load", "error"]);

const isLoading = ref(false);
const failed = ref(false);

watch(
  () => props.src,
  (src) => {
    failed.value = false;
    if (!src) {
      isLoading.value = false;
      return;
    }
    isLoading.value = true;
    const probe = new window.Image();
    probe.src = src;
    if (probe.complete && probe.naturalWidth > 0) {
      isLoading.value = false;
    }
  },
  { immediate: true },
);

function onLoad(e) {
  isLoading.value = false;
  failed.value = false;
  emit("load", e);
}

function onError(e) {
  isLoading.value = false;
  failed.value = true;
  emit("error", e);
}
</script>

<template>
  <div
    :class="
      cn(
        'relative overflow-hidden',
        (!src || failed) && 'flex items-center justify-center',
        props.class,
      )
    "
  >
    <div
      v-if="src && isLoading"
      class="absolute inset-0 z-20 flex items-center justify-center"
      aria-hidden="true"
    >
      <Spinner
        :size="spinnerSize"
        :class="cn('text-muted-foreground', spinnerClass)"
      />
    </div>
    <template v-if="src && !failed">
      <img
        v-if="blurBackdrop"
        :src="src"
        alt=""
        aria-hidden="true"
        class="pointer-events-none absolute inset-0 size-full scale-110 object-cover blur-md"
      />
      <img
        :src="src"
        :alt="alt"
        :class="cn(imgClass, isLoading ? 'opacity-0' : 'opacity-100')"
        v-bind="$attrs"
        @load="onLoad"
        @error="onError"
      />
    </template>
    <slot v-else>
      <div
        class="bg-secondary flex size-full items-center justify-center"
        aria-hidden="true"
      >
        <Image class="text-secondary-foreground size-[25%]" />
      </div>
    </slot>
  </div>
</template>
