<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import LogFilterSelect from "./LogFilterSelect.vue";

const props = defineProps({
  modelValue: { type: String, default: "__all__" },
  allValue: { type: String, default: "__all__" },
  allLabel: { type: String, default: "" },
  placeholder: { type: String, default: "" },
  errorsLabel: { type: String, default: "" },
});

defineEmits(["update:modelValue"]);

const { t } = useI18n();

const options = computed(() => [
  { value: "2xx", label: "2xx" },
  { value: "4xx", label: "4xx" },
  { value: "5xx", label: "5xx" },
  { value: "error", label: props.errorsLabel || t("components.logs.errors") },
]);

const resolvedAllLabel = computed(
  () => props.allLabel || t("components.logs.anyStatus"),
);
const resolvedPlaceholder = computed(
  () => props.placeholder || t("components.logs.code"),
);
</script>

<template>
  <LogFilterSelect
    :model-value="modelValue"
    :all-value="allValue"
    :all-label="resolvedAllLabel"
    :placeholder="resolvedPlaceholder"
    :options="options"
    @update:model-value="$emit('update:modelValue', $event)"
  />
</template>
