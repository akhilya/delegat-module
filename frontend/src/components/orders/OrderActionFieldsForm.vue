<script setup>
import { computed } from "vue";
import { FieldGroup } from "@/components/ui/field";
import Input from "@/components/ui/input/Input.vue";
import { Textarea } from "@/components/ui/textarea";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  orderActionFieldIsRequired,
  orderActionFieldIsVisible,
} from "@/lib/orderActionFields";

const props = defineProps({
  fields: { type: Array, default: () => [] },
  modelValue: { type: Object, default: () => ({}) },
  idPrefix: { type: String, default: "order-action" },
});

const emit = defineEmits(["update:modelValue"]);

const values = computed({
  get: () => props.modelValue || {},
  set: (next) => emit("update:modelValue", next),
});

function visibleFields(fields, current) {
  return (fields || []).filter((field) => orderActionFieldIsVisible(field, current));
}

function setValue(key, value) {
  values.value = { ...values.value, [key]: value };
}

function fieldId(field) {
  return `${props.idPrefix}-${field.key}`;
}
</script>

<template>
  <FieldGroup class="gap-3">
    <SettingsFormField
      v-for="field in visibleFields(fields, values)"
      :key="field.key"
      :label="field.name"
      :label-for="fieldId(field)"
      :description="field.description"
      :required="orderActionFieldIsRequired(field, values)"
    >
      <Select
        v-if="field.type === 'select'"
        :model-value="values[field.key] || ''"
        @update:model-value="setValue(field.key, $event)"
      >
        <SelectTrigger :id="fieldId(field)">
          <SelectValue :placeholder="field.placeholder || field.name" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem
            v-for="option in field.options"
            :key="option.value"
            :value="option.value"
          >
            {{ option.label || option.value }}
          </SelectItem>
        </SelectContent>
      </Select>
      <label
        v-else-if="field.type === 'checkbox'"
        class="flex items-center gap-2 text-sm"
      >
        <Checkbox
          :id="fieldId(field)"
          :model-value="Boolean(values[field.key])"
          @update:model-value="setValue(field.key, $event === true)"
        />
      </label>
      <Textarea
        v-else-if="field.type === 'textarea'"
        :id="fieldId(field)"
        :model-value="values[field.key] || ''"
        :placeholder="field.placeholder"
        @update:model-value="setValue(field.key, $event)"
      />
      <Input
        v-else
        :id="fieldId(field)"
        :model-value="values[field.key] || ''"
        :placeholder="field.placeholder"
        @update:model-value="setValue(field.key, $event)"
      />
    </SettingsFormField>
  </FieldGroup>
</template>
