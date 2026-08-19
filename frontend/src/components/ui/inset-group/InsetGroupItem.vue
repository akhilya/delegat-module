<script setup>
import { computed, useAttrs, useSlots } from "vue";
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

const slots = useSlots();
const attrs = useAttrs();

/** A <button> cannot contain another button/link. Trailing actions sit in the row but force a div root. */
const hasTrailing = computed(() => !!slots.trailing);
const resolvedAs = computed(() => {
  if (props.asChild) return props.as;
  if (hasTrailing.value && props.as === "button") return "div";
  return props.as;
});

const delegatedProps = reactiveOmit(props, "class", "selected", "showChevron", "as");
const isNativeButton = computed(
  () => resolvedAs.value === "button" && !props.asChild,
);
const usesDivButtonRole = computed(
  () =>
    !props.asChild &&
    hasTrailing.value &&
    props.as === "button" &&
    resolvedAs.value === "div",
);

const showHover = computed(() => !props.disabled && !props.selected);

function onRowKeydown(event) {
  if (!usesDivButtonRole.value || props.disabled) return;
  if (event.key !== "Enter" && event.key !== " ") return;
  event.preventDefault();
  const click = attrs.onClick;
  if (typeof click === "function") click(event);
  else if (Array.isArray(click)) click.forEach((fn) => fn(event));
}

function onTrailingClick(event) {
  if (event.target.closest("button, a, input, select, textarea, [role='menuitem']")) {
    event.stopPropagation();
  }
}
</script>

<template>
  <Primitive
    data-slot="inset-group-item"
    :as="resolvedAs"
    :data-selected="selected ? 'true' : undefined"
    :class="
      cn(
        'flex w-full items-center gap-3 rounded-lg px-2.5 py-2.5 text-left text-sm outline-none transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]',
        'focus-visible:ring-ring/50 focus-visible:ring-2',
        'disabled:pointer-events-none disabled:opacity-50',
        '[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_.drag-handle]:pointer-events-auto',
        selected
          ? 'bg-primary font-semibold text-primary-foreground hover:bg-primary hover:text-primary-foreground data-[selected=true]:[&_.text-muted-foreground]:text-primary-foreground/70'
          : showHover && 'hover:bg-muted/70',
        props.class,
      )
    "
    v-bind="{
      ...delegatedProps,
      ...attrs,
      ...(isNativeButton ? { type: 'button' } : {}),
      ...(usesDivButtonRole
        ? {
            role: 'button',
            tabindex: props.disabled ? -1 : 0,
            onKeydown: onRowKeydown,
          }
        : {}),
    }"
  >
    <span
      v-if="$slots.leading"
      class="flex size-8 shrink-0 items-center justify-center [&_svg:not([class*='size-'])]:size-4"
    >
      <slot name="leading" />
    </span>
    <div class="min-w-0 flex-1">
      <slot />
    </div>
    <span
      v-if="$slots.trailing"
      class="shrink-0"
      @click="onTrailingClick"
    >
      <slot name="trailing" />
    </span>
    <ChevronRight
      v-if="showChevron"
      class="size-4 shrink-0 opacity-40"
      :class="selected ? 'text-primary-foreground opacity-80' : 'text-muted-foreground'"
    />
  </Primitive>
</template>
