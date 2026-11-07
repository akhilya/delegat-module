<script setup>
import {
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
} from "@/components/ui/dropdown-menu";

defineProps({
  modelValue: { type: [String, Number], default: "" },
  label: { type: String, required: true },
  options: { type: Array, default: () => [] },
});
defineEmits(["update:modelValue"]);
</script>

<template>
  <DropdownMenuSub>
    <DropdownMenuSubTrigger :title="label">
      <span class="min-w-0 flex-1 truncate">{{ label }}</span>
    </DropdownMenuSubTrigger>
    <DropdownMenuSubContent
      class="w-56 max-h-(--reka-dropdown-menu-content-available-height) overflow-y-auto"
    >
      <DropdownMenuRadioGroup
        :model-value="String(modelValue)"
        @update:model-value="$emit('update:modelValue', $event)"
      >
        <DropdownMenuRadioItem
          v-for="option in options"
          :key="String(option.value)"
          :value="String(option.value)"
          :text-value="option.label"
          :title="option.label"
        >
          <span class="min-w-0 flex-1 truncate">{{ option.label }}</span>
        </DropdownMenuRadioItem>
      </DropdownMenuRadioGroup>
    </DropdownMenuSubContent>
  </DropdownMenuSub>
</template>
