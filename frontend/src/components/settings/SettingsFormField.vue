<script setup>
import { Field, FieldLabel, FieldDescription } from "@/components/ui/field";
import { cn } from "@/lib/utils";

defineProps({
  label: { type: String, required: true },
  labelFor: { type: String, default: undefined },
  description: { type: String, default: "" },
  changed: { type: Boolean, default: false },
});
</script>

<template>
  <Field
    :class="cn(
      'gap-1.5',
      changed && 'rounded-lg bg-muted/40 px-3 py-3 -mx-3',
    )"
  >
    <FieldLabel :for="labelFor" class="text-sm font-medium">{{ label }}</FieldLabel>
    <slot />
    <FieldDescription v-if="description || $slots.description" class="text-xs">
      <slot name="description">{{ description }}</slot>
    </FieldDescription>
  </Field>
</template>