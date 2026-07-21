<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import LogFilterSelect from "./LogFilterSelect.vue";

const props = defineProps({
  modelValue: { type: String, default: "__all__" },
  allValue: { type: String, default: "__all__" },
  allLabel: { type: String, default: "" },
  placeholder: { type: String, default: "" },
  /** Extra methods beyond the default set */
  extraMethods: { type: Array, default: () => [] },
  triggerClass: { type: String, default: "h-9 w-full sm:w-32" },
});

defineEmits(["update:modelValue"]);

const { t } = useI18n();

const options = computed(() => {
  const base = ["GET", "POST", "PUT", "PATCH", "DELETE"];
  const seen = new Set(base);
  const list = [...base];
  for (const m of props.extraMethods) {
    if (!seen.has(m)) {
      seen.add(m);
      list.push(m);
    }
  }
  return list.map((value) => ({ value, label: value }));
});

const resolvedAllLabel = computed(
  () => props.allLabel || t("components.logs.anyMethod"),
);
const resolvedPlaceholder = computed(
  () => props.placeholder || t("components.logs.method"),
);
</script>

<template>
  <LogFilterSelect
    :model-value="modelValue"
    :all-value="allValue"
    :all-label="resolvedAllLabel"
    :placeholder="resolvedPlaceholder"
    :options="options"
    :trigger-class="triggerClass"
    @update:model-value="$emit('update:modelValue', $event)"
  />
</template>
