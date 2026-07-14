<script>
// StoreAttributeForm - Renders a dynamic form for store-level (global) attribute values with type-aware inputs.
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Label from "@/components/ui/label/Label.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import { EmptyState } from "@/components/ui/empty-state";

// Attribute types: 0=Text, 1=Number, 2=Boolean, 3=Select, 4=MultiSelect
const TYPE_TEXT = [0, "Text", "text"];
const TYPE_NUMBER = [1, "Number", "number"];
const TYPE_BOOLEAN = [2, "Boolean", "boolean"];
const TYPE_SELECT = [3, "Select", "select"];
const TYPE_MULTISELECT = [4, "MultiSelect", "multiSelect", "multiselect"];
const TYPE_BARCODE = [5, "Barcode", "barcode"];

export default {
  components: {
    Input,
    Textarea,
    Switch,
    Label,
    SearchableSelect,
    MultiSelectDropdown,
    BarcodeInput,
    SettingsFormField,
    EmptyState,
  },
  props: {
    attributes: {
      type: Array,
      required: true,
    },
    modelValue: {
      type: Array,
      default: () => [],
    },
    errors: {
      type: Object,
      default: () => ({}),
    },
  },
  emits: ["update:modelValue"],
  computed: {
    sortedAttributes() {
      return [...this.attributes]
        .filter((attr) => attr.showOnProductPage !== false)
        .sort((a, b) => a.sortOrder - b.sortOrder);
    },
  },
  methods: {
    isType(attrType, typeName) {
      const typeMap = {
        text: TYPE_TEXT,
        number: TYPE_NUMBER,
        boolean: TYPE_BOOLEAN,
        select: TYPE_SELECT,
        multiselect: TYPE_MULTISELECT,
        barcode: TYPE_BARCODE,
      };
      return typeMap[typeName]?.includes(attrType) ?? false;
    },

    getValue(storeAttributeId) {
      const attrValue = this.modelValue.find(
        (av) => av.storeAttributeId === storeAttributeId,
      );
      return attrValue?.value || "";
    },

    setValue(storeAttributeId, value) {
      const newValues = [...this.modelValue];
      const index = newValues.findIndex(
        (av) => av.storeAttributeId === storeAttributeId,
      );

      if (index >= 0) {
        if (value === "" || value === null || value === undefined) {
          newValues.splice(index, 1);
        } else {
          newValues[index] = { storeAttributeId, value: String(value) };
        }
      } else if (value !== "" && value !== null && value !== undefined) {
        newValues.push({ storeAttributeId, value: String(value) });
      }

      this.$emit("update:modelValue", newValues);
    },

    /**
     * Parses options from a string (supports JSON and comma-separated for backward compatibility).
     * @returns {Array<{value: string, label: string}>}
     */
    getSelectOptions(attribute) {
      if (!attribute.options) return [];

      // Try to parse as JSON
      try {
        const parsed = JSON.parse(attribute.options);
        if (Array.isArray(parsed)) {
          return parsed.map(o => ({
            value: String(o.value || o.label || o),
            label: o.label || o.value || String(o)
          }));
        }
      } catch {
        // Not JSON - parse as comma-separated
      }

      // Fallback: comma-separated string
      return attribute.options
        .split(",")
        .map((opt) => opt.trim())
        .filter(Boolean)
        .map(label => ({ value: label, label }));
    },

    getMultiSelectValues(storeAttributeId) {
      const value = this.getValue(storeAttributeId);
      if (!value) return [];
      try {
        const parsed = JSON.parse(value);
        // Ensure we always return an array
        if (Array.isArray(parsed)) {
          return parsed;
        }
        // If parsed is not an array, treat as single value
        return [String(parsed)];
      } catch {
        return value
          .split(",")
          .map((v) => v.trim())
          .filter(Boolean);
      }
    },

    setMultiSelectValues(storeAttributeId, values) {
      this.setValue(storeAttributeId, JSON.stringify(values));
    },

    /**
     * Returns display labels for the currently selected multiselect values.
     */
    getMultiSelectLabels(attribute) {
      const values = this.getMultiSelectValues(attribute.id);
      const options = this.getSelectOptions(attribute);
      // Convert values (which may be ids) to labels
      return values.map(v => {
        const opt = options.find(o => o.value === v || o.label === v);
        return opt ? opt.label : v;
      });
    },

    /**
     * Sets multiselect values by labels, converting them back to stored values.
     */
    setMultiSelectByLabels(attribute, labels) {
      const options = this.getSelectOptions(attribute);
      // Convert labels back to values
      const values = labels.map(l => {
        const opt = options.find(o => o.label === l);
        return opt ? opt.value : l;
      });
      this.setMultiSelectValues(attribute.id, values);
    },

    /**
     * Returns the validation error message for a given attribute, or null.
     */
    getError(attributeId) {
      return this.errors[attributeId] || null;
    },

    /**
     * Checks whether a given attribute has a validation error.
     */
    hasError(attributeId) {
      return !!this.errors[attributeId];
    },

    attributeLabel(attribute) {
      return attribute.isRequired ? `${attribute.name} *` : attribute.name;
    },

    isFullWidthField(attribute) {
      return this.isType(attribute.type, "text") && attribute.isMultiline;
    },
  },
};
</script>

<template>
  <div class="grid grid-cols-1 md:grid-cols-2 gap-5">
    <div
      v-for="attribute in sortedAttributes"
      :key="attribute.id"
      :class="isFullWidthField(attribute) ? 'md:col-span-2' : ''"
    >
      <div
        v-if="isType(attribute.type, 'boolean')"
        class="flex flex-col gap-1.5 self-end"
      >
        <div class="flex items-center gap-3 h-9">
          <Switch
            :id="'store-attr-' + attribute.id"
            :model-value="getValue(attribute.id) === 'true'"
            @update:model-value="setValue(attribute.id, $event ? 'true' : 'false')"
          />
          <Label
            :for="'store-attr-' + attribute.id"
            class="text-sm font-medium leading-none cursor-pointer"
          >
            {{ attribute.name }}
            <span v-if="attribute.isRequired" class="text-destructive">*</span>
          </Label>
        </div>
        <p
          v-if="attribute.description"
          class="text-xs text-muted-foreground pl-[calc(2.25rem+0.75rem)]"
        >
          {{ attribute.description }}
        </p>
        <p v-if="getError(attribute.id)" class="text-sm text-destructive">
          {{ getError(attribute.id) }}
        </p>
      </div>

      <SettingsFormField
        v-else
        :label="attributeLabel(attribute)"
        :label-for="'store-attr-' + attribute.id"
        :description="attribute.description"
      >
      <Textarea
        v-if="isType(attribute.type, 'text') && attribute.isMultiline"
        :id="'store-attr-' + attribute.id"
        :model-value="getValue(attribute.id)"
        @update:model-value="setValue(attribute.id, $event)"
        :placeholder="attribute.defaultValue || ''"
        :required="attribute.isRequired"
        class="min-h-24 resize-y"
        :class="{ 'border-destructive focus-visible:ring-destructive': hasError(attribute.id) }"
      />
      <Input
        v-else-if="isType(attribute.type, 'text')"
        :id="'store-attr-' + attribute.id"
        :model-value="getValue(attribute.id)"
        @update:model-value="setValue(attribute.id, $event)"
        :placeholder="attribute.defaultValue || ''"
        :required="attribute.isRequired"
        :class="{ 'border-destructive focus-visible:ring-destructive': hasError(attribute.id) }"
      />
      <Input
        v-else-if="isType(attribute.type, 'number')"
        :id="'store-attr-' + attribute.id"
        type="number"
        :model-value="getValue(attribute.id)"
        @update:model-value="setValue(attribute.id, $event)"
        :placeholder="attribute.defaultValue || ''"
        :required="attribute.isRequired"
        :class="{ 'border-destructive focus-visible:ring-destructive': hasError(attribute.id) }"
      />
      <SearchableSelect
        v-else-if="isType(attribute.type, 'select')"
        :model-value="getValue(attribute.id)"
        @update:model-value="setValue(attribute.id, $event)"
        :options="getSelectOptions(attribute)"
        :placeholder="attribute.defaultValue || 'Выберите...'"
        :class="{ 'border-destructive': hasError(attribute.id) }"
      />
      <MultiSelectDropdown
        v-else-if="isType(attribute.type, 'multiselect')"
        :options="getSelectOptions(attribute).map(o => o.label)"
        :model-value="getMultiSelectLabels(attribute)"
        @update:model-value="setMultiSelectByLabels(attribute, $event)"
        placeholder="Выберите..."
        :class="{ 'border-destructive': hasError(attribute.id) }"
      />
      <BarcodeInput
        v-else-if="isType(attribute.type, 'barcode')"
        :model-value="getValue(attribute.id)"
        @update:model-value="setValue(attribute.id, $event)"
      />
      <Input
        v-else
        :model-value="getValue(attribute.id)"
        @update:model-value="setValue(attribute.id, $event)"
        :placeholder="`Неизвестный тип: ${attribute.type}`"
        :class="{ 'border-destructive focus-visible:ring-destructive': hasError(attribute.id) }"
      />

      <p v-if="getError(attribute.id)" class="text-sm text-destructive">
        {{ getError(attribute.id) }}
      </p>
      </SettingsFormField>
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
