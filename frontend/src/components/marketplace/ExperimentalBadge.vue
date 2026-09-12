<script setup>
import { Wrench } from "lucide-vue-next";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

defineProps({
  class: { type: String, default: "" },
});
</script>

<template>
  <Badge
    variant="muted"
    :class="cn('size-5 p-0', $props.class)"
    :title="$t('common.stageExperimental')"
    :aria-label="$t('common.stageExperimental')"
  >
    <Wrench />
  </Badge>
</template>

