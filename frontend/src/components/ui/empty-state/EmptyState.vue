<script setup>
import { useSlots, useAttrs, computed } from "vue";
import { Loader2 } from "lucide-vue-next";
import { cn } from "@/lib/utils";
import {
  Empty,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
  EmptyDescription,
  EmptyContent,
} from "@/components/ui/empty";

const props = defineProps({
  loading: { type: Boolean, default: false },
  title: { type: String, default: "" },
  description: { type: String, default: "" },
  // "sm" for dense inline cases only; page/tab empties use default
  size: { type: String, default: "default" },
  class: { type: null, required: false },
});

defineOptions({ inheritAttrs: false });

const slots = useSlots();
const attrs = useAttrs();

const frameClass = computed(() =>
  cn(
    // Expand inside flex parents (PageSection / fill layouts) and keep a visible frame.
    "flex w-full min-h-0 flex-1 flex-col items-center justify-center rounded-lg border",
    props.size === "sm" ? "gap-3 p-4 md:p-6" : "gap-4 p-6 md:p-12",
    props.class,
  ),
);
</script>

<template>
  <div
    v-if="loading"
    :class="frameClass"
    v-bind="attrs"
  >
    <Loader2 class="size-8 animate-spin text-muted-foreground" />
  </div>
  <Empty v-else :class="frameClass" v-bind="attrs">
    <EmptyHeader>
      <EmptyMedia
        v-if="slots.icon"
        variant="icon"
        :class="props.size === 'sm' ? 'size-10 [&_svg]:size-5' : undefined"
      >
        <slot name="icon" />
      </EmptyMedia>
      <EmptyTitle
        v-if="props.title"
        :class="props.size === 'sm' ? 'text-sm' : undefined"
      >
        {{ props.title }}
      </EmptyTitle>
      <EmptyDescription
        v-if="props.description"
        :class="props.size === 'sm' ? 'text-xs' : undefined"
      >
        {{ props.description }}
      </EmptyDescription>
    </EmptyHeader>
    <EmptyContent v-if="slots.default">
      <slot />
    </EmptyContent>
  </Empty>
</template>
