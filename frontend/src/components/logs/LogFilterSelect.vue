<script setup>
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

defineProps({
  modelValue: { type: [String, Number], default: "" },
  /** Placeholder when nothing selected */
  placeholder: { type: String, default: "" },
  /** Value for the “all / any” option */
  allValue: { type: String, default: "__all__" },
  allLabel: { type: String, default: "" },
  /** @type {{ value: string|number, label: string }[]} */
  options: { type: Array, default: () => [] },
  triggerClass: { type: String, default: "h-9 w-full sm:w-40" },
});

defineEmits(["update:modelValue"]);
</script>

<template>
  <Select
    :model-value="modelValue"
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <SelectTrigger :class="triggerClass">
      <SelectValue :placeholder="placeholder" />
    </SelectTrigger>
    <SelectContent>
      <SelectItem v-if="allLabel" :value="allValue">{{ allLabel }}</SelectItem>
      <SelectItem
        v-for="opt in options"
        :key="String(opt.value)"
        :value="String(opt.value)"
      >
        {{ opt.label }}
      </SelectItem>
    </SelectContent>
  </Select>
</template>
