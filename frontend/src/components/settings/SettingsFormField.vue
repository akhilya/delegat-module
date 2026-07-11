<script setup>
import { Field, FieldLabel, FieldDescription } from "@/components/ui/field";

defineProps({
  label: { type: String, required: true },
  labelFor: { type: String, default: undefined },
  description: { type: String, default: "" },
});
</script>

<template>
  <Field class="gap-1.5">
    <FieldLabel :for="labelFor" class="text-sm font-medium">{{ label }}</FieldLabel>
    <slot />
    <FieldDescription v-if="description || $slots.description" class="text-xs">
      <slot name="description">{{ description }}</slot>
    </FieldDescription>
  </Field>
</template>