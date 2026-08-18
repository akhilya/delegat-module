<script setup>
import {
  FieldSet,
  FieldGroup,
} from "@/components/ui/field";
import { InsetGroupLabel } from "@/components/ui/inset-group";
import { cn } from "@/lib/utils";

const props = defineProps({
  title: { type: String, default: "" },
  description: { type: String, default: "" },
  class: { type: null, default: undefined },
});
</script>

<template>
  <FieldSet :class="cn('m-0 min-w-0 max-w-2xl border-0 p-0 gap-4 [&+&]:mt-8', props.class)">
    <div v-if="title || description">
      <InsetGroupLabel v-if="title" :class="description ? 'mb-1' : 'mb-2'">
        {{ title }}
      </InsetGroupLabel>
      <p
        v-if="description"
        class="mb-2 px-1 text-xs text-muted-foreground"
      >
        {{ description }}
      </p>
    </div>
    <FieldGroup class="gap-5">
      <slot />
    </FieldGroup>
    <div v-if="$slots.footer" class="mt-1 flex flex-wrap items-center justify-end gap-3">
      <slot name="footer" />
    </div>
  </FieldSet>
</template>
