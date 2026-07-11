<script setup>
import {
  FieldSet,
  FieldLegend,
  FieldDescription,
  FieldGroup,
} from "@/components/ui/field";

defineProps({
  title: { type: String, required: true },
  description: { type: String, default: "" },
});
</script>

<template>
  <FieldSet class="max-w-2xl border-t border-border/40 pt-8 first:border-t-0 first:pt-0">
    <FieldLegend variant="legend">{{ title }}</FieldLegend>
    <FieldDescription v-if="description">{{ description }}</FieldDescription>
    <FieldGroup class="gap-0">
      <slot />
    </FieldGroup>
    <div v-if="$slots.footer" class="mt-6 flex flex-wrap items-center gap-3 border-t border-border/40 pt-4">
      <slot name="footer" />
    </div>
  </FieldSet>
</template>