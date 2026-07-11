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
  <FieldSet class="m-0 min-w-0 max-w-2xl border-0 p-0 [&+&]:mt-14">
    <FieldLegend variant="legend" class="mb-0.5 w-full text-base font-semibold">
      {{ title }}
    </FieldLegend>
    <FieldDescription v-if="description" class="mb-6 max-w-lg">
      {{ description }}
    </FieldDescription>
    <FieldGroup class="gap-8">
      <slot />
    </FieldGroup>
    <div v-if="$slots.footer" class="mt-6 flex flex-wrap items-center gap-3">
      <slot name="footer" />
    </div>
  </FieldSet>
</template>