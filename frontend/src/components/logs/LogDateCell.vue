<script setup>
import { computed } from "vue";
import { useSharedNow } from "@/composables/useSharedNow";
import { formatLogDate, formatLogRelative } from "./logFormat.js";

const props = defineProps({
  value: { type: [String, Date, Number, null], default: null },
  timezone: { type: String, default: "UTC" },
});

const now = useSharedNow();

const absolute = computed(() => formatLogDate(props.value, props.timezone));
const relative = computed(() =>
  formatLogRelative(props.value, now.value, props.timezone),
);
</script>

<template>
  <span
    class="inline-flex min-w-0 max-w-full flex-col gap-0.5 overflow-hidden text-sm text-muted-foreground tabular-nums leading-tight"
    :title="absolute"
  >
    <span class="truncate">{{ relative }}</span>
    <span class="truncate text-[11px] opacity-70">{{ absolute }}</span>
  </span>
</template>
