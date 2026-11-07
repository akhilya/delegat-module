<script setup>
import { computed } from "vue";
import FilterMenuGroup from "@/components/FilterMenuGroup.vue";

const props = defineProps({
  modelValue: { type: [String, Number], default: "" },
  placeholder: { type: String, default: "" },
  allValue: { type: String, default: "__all__" },
  allLabel: { type: String, default: "" },
  options: { type: Array, default: () => [] },
});
defineEmits(["update:modelValue"]);
const choices = computed(() =>
  props.allLabel
    ? [{ value: props.allValue, label: props.allLabel }, ...props.options]
    : props.options,
);
</script>

<template>
  <FilterMenuGroup
    :model-value="modelValue"
    :label="placeholder"
    :options="choices"
    @update:model-value="$emit('update:modelValue', $event)"
  />
</template>
