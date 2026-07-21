<script setup>
import Badge from "@/components/ui/badge/Badge.vue";
import { httpStatusVariant } from "./logFormat.js";

defineProps({
  statusCode: { type: [Number, String, null], default: null },
  empty: { type: String, default: "-" },
});
</script>

<template>
  <Badge :variant="httpStatusVariant(statusCode)">
    {{ statusCode || empty }}
  </Badge>
</template>
