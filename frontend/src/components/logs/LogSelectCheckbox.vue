<script setup>
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";

defineProps({
  modelValue: { type: [Boolean, String, null], default: false },
  indeterminate: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
});

defineEmits(["update:modelValue"]);
</script>

<template>
  <div class="flex items-center justify-center" @click.stop>
    <Checkbox
      :model-value="modelValue"
      :indeterminate="indeterminate"
      :disabled="disabled"
      @update:model-value="$emit('update:modelValue', $event)"
    />
  </div>
</template>
