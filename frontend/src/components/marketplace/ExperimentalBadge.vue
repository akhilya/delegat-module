<script setup>
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

defineProps({
  class: { type: String, default: "" },
});
</script>

<template>
  <Badge variant="muted" :class="cn('font-normal', $props.class)">
    {{ $t("common.stageExperimental") }}
  </Badge>
</template>

