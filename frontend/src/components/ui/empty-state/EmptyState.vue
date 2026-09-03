<script setup>
import { useSlots, useAttrs, computed } from "vue";
import { Spinner } from "@/components/ui/spinner";
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
  /**
   * When false, omit border/radius — use inside an already-framed parent
   * (tab panels, side sheets, cards). Region loaders never frame: a bordered
   * box around a spinner reads as a card, not a page wait.
   */
  framed: { type: Boolean, default: true },
  class: { type: null, required: false },
});

defineOptions({ inheritAttrs: false });

const slots = useSlots();
const attrs = useAttrs();

const frameClass = computed(() =>
  cn(
    "flex w-full min-h-0 flex-1 flex-col items-center justify-center",
    props.framed && !props.loading && "rounded-lg border",
    props.loading && "page-region-in",
    props.size === "sm" ? "gap-3 p-4 md:p-6" : "gap-4 p-6 md:p-12",
    props.class,
  ),
);
</script>

<template>
  <div
    v-if="loading"
    data-slot="empty-state-loading"
    :class="frameClass"
    v-bind="attrs"
    aria-busy="true"
  >
    <Spinner size="2xl" class="text-muted-foreground" />
    <p
      v-if="title || description"
      class="text-sm text-muted-foreground text-center max-w-sm"
      role="status"
      aria-live="polite"
    >
      {{ title || description }}
    </p>
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
