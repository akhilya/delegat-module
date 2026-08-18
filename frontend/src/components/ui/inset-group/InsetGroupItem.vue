<script setup>
import { computed } from "vue";
import { reactiveOmit } from "@vueuse/core";
import { Primitive } from "reka-ui";
import { ChevronRight } from "lucide-vue-next";
import { cn } from "@/lib/utils";

defineOptions({ inheritAttrs: false });

const props = defineProps({
  as: { type: null, default: "button" },
  asChild: { type: Boolean, required: false },
  selected: { type: Boolean, default: false },
  showChevron: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
  class: { type: null, required: false },
});

const delegatedProps = reactiveOmit(props, "class", "selected", "showChevron");

const isNativeButton = computed(() => props.as === "button" && !props.asChild);
const interactive = computed(() => props.as !== "div");
</script>

<template>
  <Primitive
    data-slot="inset-group-item"
    :data-selected="selected ? 'true' : undefined"
    :class="
      cn(
        'flex w-full items-center gap-3 rounded-lg px-2.5 py-2.5 text-left text-sm outline-none transition-colors duration-150 ease-out',
        'focus-visible:ring-ring/50 focus-visible:ring-2',
        'disabled:pointer-events-none disabled:opacity-50',
        '[&_svg]:pointer-events-none [&_svg]:shrink-0',
        selected
          ? 'bg-primary font-semibold text-primary-foreground hover:bg-primary hover:text-primary-foreground data-[selected=true]:[&_.text-muted-foreground]:text-primary-foreground/70'
          : interactive && 'hover:bg-muted/70',
        props.class,
      )
    "
    v-bind="{
      ...delegatedProps,
      ...$attrs,
      ...(isNativeButton ? { type: 'button' } : {}),
    }"
  >
    <span
      v-if="$slots.leading"
      class="flex size-8 shrink-0 items-center justify-center [&_svg:not([class*='size-'])]:size-4"
    >
      <slot name="leading" />
    </span>
    <span class="min-w-0 flex-1">
      <slot />
    </span>
    <span v-if="$slots.trailing" class="shrink-0">
      <slot name="trailing" />
    </span>
    <ChevronRight
      v-if="showChevron"
      class="size-4 shrink-0 opacity-40"
      :class="selected ? 'text-primary-foreground opacity-80' : 'text-muted-foreground'"
    />
  </Primitive>
</template>
