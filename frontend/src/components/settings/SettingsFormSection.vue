<script setup>
import {
  FieldSet,
  FieldGroup,
  FieldLegend,
  FieldDescription,
} from "@/components/ui/field";
import { cn } from "@/lib/utils";

const props = defineProps({
  title: { type: String, default: "" },
  description: { type: String, default: "" },
  class: { type: null, default: undefined },
});
</script>

<template>
  <FieldSet :class="cn('m-0 min-w-0 max-w-2xl border-0 p-0 gap-4 [&+&]:mt-8', props.class)">
    <div v-if="title || description" class="space-y-1">
      <FieldLegend v-if="title" variant="label" class="mb-0">{{ title }}</FieldLegend>
      <FieldDescription v-if="description">{{ description }}</FieldDescription>
    </div>
    <FieldGroup class="gap-5">
      <slot />
    </FieldGroup>
    <div v-if="$slots.footer" class="mt-1 flex flex-wrap items-center gap-3">
      <slot name="footer" />
    </div>
  </FieldSet>
</template>
