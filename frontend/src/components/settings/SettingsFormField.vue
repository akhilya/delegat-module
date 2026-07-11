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
      'border-t border-border/40 py-6 first:border-t-0 first:pt-0',
      changed && 'rounded-md bg-muted/30 px-3 -mx-3',
    )"
  >
    <FieldLabel :for="labelFor">{{ label }}</FieldLabel>
    <slot />
    <FieldDescription v-if="description || $slots.description">
      <slot name="description">{{ description }}</slot>
    </FieldDescription>
  </Field>
</template>