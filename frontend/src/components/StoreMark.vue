<script setup>
import { computed } from "vue";
import { storeIconInitial, storeIconUrl } from "@/lib/storeIcon.js";

const props = defineProps({
  src: { type: String, default: "" },
  name: { type: String, default: "" },
  class: { type: null, required: false },
});

const iconSrc = computed(() => storeIconUrl(props.src));
const initial = computed(() => storeIconInitial(props.name));
</script>

<template>
  <img
    v-if="iconSrc"
    :src="iconSrc"
    alt=""
    :class="['size-4 shrink-0 rounded-sm object-cover', props.class]"
  />
  <span
    v-else-if="initial"
    aria-hidden="true"
    :class="[
      'flex size-4 shrink-0 items-center justify-center rounded-sm bg-muted text-[9px] leading-none font-semibold text-foreground',
      props.class,
    ]"
  >
    {{ initial }}
  </span>
  <span
    v-else
    aria-hidden="true"
    :class="['size-4 shrink-0 rounded-sm bg-muted', props.class]"
  />
</template>
