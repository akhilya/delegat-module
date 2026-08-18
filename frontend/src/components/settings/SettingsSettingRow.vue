<script setup>
import { InsetGroupItem } from "@/components/ui/inset-group";
import { cn } from "@/lib/utils";

const props = defineProps({
  title: { type: String, required: true },
  description: { type: String, default: "" },
  as: { type: null, default: "div" },
  disabled: { type: Boolean, default: false },
  class: { type: null, default: undefined },
});
</script>

<template>
  <InsetGroupItem
    :as="as"
    :disabled="disabled"
    :class="cn(props.class)"
  >
    <p>{{ title }}</p>
    <p
      v-if="description"
      class="mt-0.5 text-xs font-normal text-muted-foreground"
    >
      {{ description }}
    </p>
    <template #trailing>
      <slot />
    </template>
  </InsetGroupItem>
</template>
