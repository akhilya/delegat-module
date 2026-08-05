<script setup>
import { computed, useId } from "vue";
import { Plug } from "lucide-vue-next";
import { getAdapterIconUrl } from "@/lib/adapterIconAssets";
import { cn } from "@/lib/utils";

const props = defineProps({
  adapterKey: { type: String, default: null },
  /** Optional override when URL is already known */
  src: { type: String, default: null },
  alt: { type: String, default: "" },
  class: { type: String, default: "" },
  fallback: { type: Boolean, default: true },
});

const uid = useId();
const filterId = computed(
  () => `adapter-icon-tint-${String(uid).replace(/:/g, "")}`,
);

const resolvedSrc = computed(
  () => props.src || getAdapterIconUrl(props.adapterKey),
);

const boxClass = computed(() =>
  cn("inline-block size-4 shrink-0 overflow-hidden", props.class),
);
</script>

<template>
  <!--
    Icons are white (or any RGB) on transparent. Tint = currentColor RGB,
    keep the file's alpha channel (feComposite "in").
  -->
  <svg
    v-if="resolvedSrc"
    xmlns="http://www.w3.org/2000/svg"
    viewBox="0 0 24 24"
    role="img"
    :aria-label="alt || undefined"
    :aria-hidden="alt ? undefined : true"
    :class="boxClass"
  >
    <defs>
      <filter
        :id="filterId"
        color-interpolation-filters="sRGB"
        x="0"
        y="0"
        width="100%"
        height="100%"
      >
        <feFlood flood-color="currentColor" result="tint" />
        <feComposite in="tint" in2="SourceGraphic" operator="in" />
      </filter>
    </defs>
    <image
      :href="resolvedSrc"
      width="24"
      height="24"
      preserveAspectRatio="xMidYMid meet"
      :filter="`url(#${filterId})`"
    />
  </svg>
  <Plug v-else-if="fallback" :class="cn('size-4 shrink-0', props.class)" />
</template>
