<script setup>
import { formatLogDate } from "./logFormat.js";

defineProps({
  value: { type: [String, Date, Number, null], default: null },
  timezone: { type: String, default: "UTC" },
});
</script>

<template>
  <span class="text-sm text-muted-foreground tabular-nums">
    {{ formatLogDate(value, timezone) }}
  </span>
</template>
