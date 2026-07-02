<script setup>
import { ref } from "vue";
import Input from "./Input.vue";
import { Eye, EyeOff } from "lucide-vue-next";
import { cn } from "@/lib/utils";

const props = defineProps({
  defaultValue: { type: [String, Number], required: false },
  modelValue: { type: [String, Number], required: false },
  class: { type: null, required: false },
  disabled: { type: Boolean, default: false },
});

const emits = defineEmits(["update:modelValue"]);

const showPassword = ref(false);
</script>

<template>
  <div class="relative">
    <Input
      v-bind="$attrs"
      :type="showPassword ? 'text' : 'password'"
      :model-value="modelValue"
      :default-value="defaultValue"
      :disabled="disabled"
      :class="cn('pr-9', props.class)"
      @update:model-value="emits('update:modelValue', $event)"
    />
    <button
      type="button"
      :disabled="disabled"
      tabindex="-1"
      class="absolute right-0 top-0 h-9 w-9 flex items-center justify-center text-muted-foreground hover:text-foreground disabled:pointer-events-none disabled:opacity-50"
      @click="showPassword = !showPassword"
    >
      <EyeOff v-if="showPassword" class="size-4" />
      <Eye v-else class="size-4" />
    </button>
  </div>
</template>
