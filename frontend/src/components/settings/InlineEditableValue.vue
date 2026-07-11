<script setup>
import { ref, watch, nextTick } from "vue";
import { Pencil } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { cn } from "@/lib/utils";

const props = defineProps({
  modelValue: { type: [String, Number], default: "" },
  disabled: { type: Boolean, default: false },
  placeholder: { type: String, default: "не задано" },
  type: { type: String, default: "text" },
  min: { type: String, default: undefined },
  step: { type: String, default: undefined },
  emptyLabel: { type: String, default: "не задано" },
  class: { type: null, default: undefined },
});

const emit = defineEmits(["update:modelValue"]);

const isEditing = ref(false);
const draft = ref("");
const inputEl = ref(null);

const displayValue = () => {
  const value = props.modelValue;
  if (value === null || value === undefined || value === "") return props.emptyLabel;
  return String(value);
};

function startEdit() {
  if (props.disabled) return;
  draft.value = props.modelValue === null || props.modelValue === undefined ? "" : String(props.modelValue);
  isEditing.value = true;
  nextTick(() => {
    const el = inputEl.value?.$el;
    el?.focus?.();
    el?.select?.();
  });
}

function commitEdit() {
  isEditing.value = false;
  emit("update:modelValue", draft.value);
}

function cancelEdit() {
  isEditing.value = false;
}

watch(
  () => props.modelValue,
  () => {
    if (!isEditing.value) draft.value = props.modelValue === null || props.modelValue === undefined ? "" : String(props.modelValue);
  },
);
</script>

<template>
  <div :class="cn('max-w-md', props.class)">
    <Input
      v-if="isEditing"
      ref="inputEl"
      v-model="draft"
      :type="type"
      :min="min"
      :step="step"
      :placeholder="placeholder"
      class="h-9 max-w-md"
      @blur="commitEdit"
      @keydown.enter.prevent="commitEdit"
      @keydown.escape.prevent="cancelEdit"
    />
    <Button
      v-else
      type="button"
      variant="ghost"
      :disabled="disabled"
      class="group/value -ml-2 h-auto max-w-md justify-start gap-2 px-2 py-1 font-normal hover:bg-accent/60"
      @click="startEdit"
    >
      <span
        class="text-sm"
        :class="displayValue() === emptyLabel ? 'text-muted-foreground' : 'font-medium text-foreground'"
      >
        {{ displayValue() }}
      </span>
      <Pencil
        v-if="!disabled"
        class="size-3 shrink-0 text-muted-foreground opacity-0 transition-opacity group-hover/value:opacity-100"
      />
    </Button>
  </div>
</template>