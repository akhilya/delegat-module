<script setup>
import { computed, useSlots } from "vue";
import { useVModel } from "@vueuse/core";
import { Search } from "lucide-vue-next";
import { cn } from "@/lib/utils";

const props = defineProps({
  modelValue: { type: String, default: "" },
  placeholder: { type: String, default: "" },
  disabled: { type: Boolean, default: false },
  class: { type: null, required: false },
});

const emit = defineEmits(["update:modelValue", "submit"]);
const model = useVModel(props, "modelValue", emit, {
  passive: true,
  defaultValue: "",
});
const slots = useSlots();
const hasTrailing = computed(() => typeof slots.trailing === "function");

function onEnter(event) {
  event.preventDefault();
  emit("submit");
}
</script>

<template>
  <div
    data-slot="nav-search"
    :class="
      cn(
        'flex h-9 items-center rounded-md border border-input bg-transparent pl-3 shadow-xs outline-none transition-[box-shadow,border-color] has-[:focus-visible]:border-ring dark:bg-input/30',
        hasTrailing ? 'pr-1' : 'pr-3',
        'w-full min-w-0',
        props.disabled && 'pointer-events-none opacity-50',
        props.class,
      )
    "
  >
    <Search class="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
    <input
      v-model="model"
      type="search"
      :placeholder="placeholder"
      :aria-label="placeholder"
      :disabled="disabled"
      class="h-full min-w-0 flex-1 appearance-none bg-transparent px-2 text-sm outline-none placeholder:text-muted-foreground [&::-webkit-search-cancel-button]:hidden"
      @keydown.enter="onEnter"
    />
    <template v-if="hasTrailing">
      <div
        class="mx-0.5 h-4 w-px shrink-0 bg-foreground/12"
        aria-hidden="true"
      />
      <div class="flex shrink-0 items-center">
        <slot name="trailing" />
      </div>
    </template>
  </div>
</template>
