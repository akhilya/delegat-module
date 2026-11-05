<script setup>
import { ref, useId } from "vue";
import { t } from "@/i18n";
import { Checkbox } from "@/components/ui/checkbox";
import { InsetGroupItem } from "@/components/ui/inset-group";

defineProps({
  modelValue: { type: [Boolean, String], default: false },
  disabled: { type: Boolean, default: false },
});
const emit = defineEmits(["update:modelValue"]);
const id = useId();
const checkbox = ref(null);
defineExpose({ focus: () => checkbox.value?.$el?.focus() });
</script>

<template>
  <InsetGroupItem
    as="div"
    data-slot="list-select-all"
    @click="!disabled && emit('update:modelValue', modelValue !== true)"
  >
    <template #leading>
      <Checkbox
        :id="id"
        ref="checkbox"
        :model-value="modelValue"
        :disabled="disabled"
        :aria-label="t('components.listSelection.selectAll')"
        @click.stop
        @update:model-value="emit('update:modelValue', $event === true)"
      />
    </template>
    <span class="text-sm font-normal text-muted-foreground">{{
      t("components.listSelection.selectAll")
    }}</span>
  </InsetGroupItem>
</template>
