<script>
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import { EmptyState } from "@/components/ui/empty-state";
import { FormControl, FormField, FormItem } from "@/components/ui/form";
import { isAttributeType } from "@/lib/productAttributeValidation";

export default {
  components: {
    Input,
    Textarea,
    Switch,
    SearchableSelect,
    MultiSelectDropdown,
    BarcodeInput,
    SettingsFormField,
    EmptyState,
    FormControl,
    FormField,
    FormItem,
  },
  props: {
    attributes: {
      type: Array,
      required: true,
    },
    fieldPrefix: {
      type: String,
      default: "storeAttributes",
    },
  },
  computed: {
    sortedAttributes() {
      return [...this.attributes]
        .filter((attr) => attr.showOnProductPage !== false)
        .sort((a, b) => a.sortOrder - b.sortOrder);
    },
  },
  methods: {
    isType(attrType, typeName) {
      return isAttributeType(attrType, typeName);
    },

    fieldName(attributeId) {
      return `${this.fieldPrefix}.${attributeId}`;
    },

    getSelectOptions(attribute) {
      if (!attribute.options) return [];

      try {
        const parsed = JSON.parse(attribute.options);
        if (Array.isArray(parsed)) {
          return parsed.map((option) => ({
            value: String(option.value || option.label || option),
            label: option.label || option.value || String(option),
          }));
        }
      } catch {
        // Not JSON - parse as comma-separated
      }

      return attribute.options
        .split(",")
        .map((opt) => opt.trim())
        .filter(Boolean)
        .map((label) => ({ value: label, label }));
    },

    getMultiSelectLabels(attribute, rawValue) {
      if (!rawValue) return [];
      try {
        const parsed = JSON.parse(rawValue);
        if (Array.isArray(parsed)) {
          return parsed.map((value) => this.labelForStoredValue(attribute, value));
        }
        return [String(parsed)];
      } catch {
        return rawValue
          .split(",")
          .map((value) => value.trim())
          .filter(Boolean);
      }
    },

    labelForStoredValue(attribute, storedValue) {
      const options = this.getSelectOptions(attribute);
      const option = options.find((item) => item.value === storedValue || item.label === storedValue);
      return option ? option.label : storedValue;
    },

    setMultiSelectByLabels(attribute, labels, setValue) {
      const options = this.getSelectOptions(attribute);
      const values = labels.map((label) => {
        const option = options.find((item) => item.label === label);
        return option ? option.value : label;
      });
      setValue(JSON.stringify(values));
    },

    isFullWidthField(attribute) {
      return this.isType(attribute.type, "text") && attribute.isMultiline;
    },
  },
};
</script>

<template>
  <div class="grid min-w-0 grid-cols-1 md:grid-cols-2 gap-5">
    <div
      v-for="attribute in sortedAttributes"
      :key="attribute.id"
      class="min-w-0 overflow-hidden"
      :class="isFullWidthField(attribute) ? 'md:col-span-2' : ''"
    >
      <FormField
        v-slot="{ componentField, value, setValue, errorMessage }"
        :name="fieldName(attribute.id)"
      >
        <FormItem class="gap-0">
          <SettingsFormField
            :label="attribute.name"
            :label-for="'store-attr-' + attribute.id"
            :description="attribute.description"
            :required="attribute.isRequired"
            :error="errorMessage"
            :inline="isType(attribute.type, 'boolean')"
            :class="isType(attribute.type, 'boolean') ? 'self-end' : ''"
          >
            <Switch
              v-if="isType(attribute.type, 'boolean')"
              :id="'store-attr-' + attribute.id"
              :model-value="value === 'true'"
              @update:model-value="setValue($event ? 'true' : 'false')"
            />
            <FormControl v-else-if="isType(attribute.type, 'text') && attribute.isMultiline">
              <Textarea
                :id="'store-attr-' + attribute.id"
                v-bind="componentField"
                :placeholder="attribute.defaultValue || ''"
                class="min-h-24 resize-y"
              />
            </FormControl>
            <FormControl v-else-if="isType(attribute.type, 'text')">
              <Input
                :id="'store-attr-' + attribute.id"
                type="text"
                v-bind="componentField"
                :placeholder="attribute.defaultValue || ''"
              />
            </FormControl>
            <FormControl v-else-if="isType(attribute.type, 'number')">
              <Input
                :id="'store-attr-' + attribute.id"
                type="text"
                inputmode="decimal"
                v-bind="componentField"
                :placeholder="attribute.defaultValue || ''"
              />
            </FormControl>
            <FormControl v-else-if="isType(attribute.type, 'select')">
              <SearchableSelect
                :model-value="value || ''"
                :options="getSelectOptions(attribute)"
                :placeholder="attribute.defaultValue || 'Выберите...'"
                :invalid="!!errorMessage"
                @update:model-value="setValue"
              />
            </FormControl>
            <FormControl v-else-if="isType(attribute.type, 'multiselect')">
              <MultiSelectDropdown
                :options="getSelectOptions(attribute).map((option) => option.label)"
                :model-value="getMultiSelectLabels(attribute, value)"
                :invalid="!!errorMessage"
                placeholder="Выберите..."
                @update:model-value="setMultiSelectByLabels(attribute, $event, setValue)"
              />
            </FormControl>
            <FormControl v-else-if="isType(attribute.type, 'barcode')">
              <BarcodeInput
                :model-value="value || ''"
                @update:model-value="setValue"
              />
            </FormControl>
            <FormControl v-else>
              <Input
                v-bind="componentField"
                type="text"
                :placeholder="`Неизвестный тип: ${attribute.type}`"
              />
            </FormControl>
          </SettingsFormField>
        </FormItem>
      </FormField>
    </div>

    <EmptyState
      v-if="sortedAttributes.length === 0"
      size="sm"
      title="Нет глобальных атрибутов"
      description="Добавьте глобальные атрибуты в настройках магазина — они появятся здесь."
      class="md:col-span-2 py-8"
    />
  </div>
</template>