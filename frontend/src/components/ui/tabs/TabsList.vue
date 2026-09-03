<script setup>
import { reactiveOmit } from "@vueuse/core";
import { TabsList } from "reka-ui";
import { cn } from "@/lib/utils";
import { useTabsIndicator } from "@/composables/useTabsIndicator";

const props = defineProps({
  loop: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});

const delegatedProps = reactiveOmit(props, "class");
const { setListRef, indicatorStyle } = useTabsIndicator();
</script>

<template>
  <TabsList
    :ref="setListRef"
    data-slot="tabs-list"
    v-bind="delegatedProps"
    :class="
      cn(
        'relative isolate bg-muted text-muted-foreground inline-flex h-9 w-fit items-center justify-center rounded-lg p-[3px]',
        props.class,
      )
    "
  >
    <span
      data-slot="tabs-indicator"
      aria-hidden="true"
      class="pointer-events-none absolute top-0 left-0 z-0 rounded-md bg-background shadow-sm dark:border dark:border-input dark:bg-input/30 dark:shadow-none"
      :style="indicatorStyle"
    />
    <slot />
  </TabsList>
</template>
