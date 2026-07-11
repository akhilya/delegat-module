<script setup>
import {
  FieldSet,
  FieldLegend,
  FieldDescription,
  FieldGroup,
} from "@/components/ui/field";
import { cn } from "@/lib/utils";

const props = defineProps({
  title: { type: String, default: "" },
  description: { type: String, default: "" },
  class: { type: null, default: undefined },
});
</script>

<template>
  <FieldSet :class="cn('m-0 min-w-0 max-w-2xl border-0 p-0 [&+&]:mt-8', props.class)">
    <FieldLegend v-if="title" variant="label" class="mb-1 w-full">
      {{ title }}
    </FieldLegend>
    <FieldDescription v-if="description" class="mb-4">
      {{ description }}
    </FieldDescription>
    <FieldGroup class="gap-5">
      <slot />
    </FieldGroup>
    <div v-if="$slots.footer" class="mt-4 flex flex-wrap items-center gap-3">
      <slot name="footer" />
    </div>
  </FieldSet>
</template>