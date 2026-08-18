<script>
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FormControl, FormField, FormItem } from "@/components/ui/form";
import { isAttributeType } from "@/lib/productAttributeValidation";
import {
  labelsToStoredValues,
  parseAttributeOptions,
  resolveAttributeOptionLabel,
  resolveAttributeOptionLabels,
} from "@/lib/attributeOptions";

export default {
  name: "AttributeFieldControl",
  components: {
    Input,
    Textarea,
    Switch,
    SearchableSelect,
    MultiSelectDropdown,
    BarcodeInput,
    SettingsFormField,
    FormControl,
    FormField,
    FormItem,
  },
  props: {
    attribute: { type: Object, required: true },
    fieldPrefix: { type: String, required: true },
  },
  methods: {
    isType(attrType, typeName) {
      return isAttributeType(attrType, typeName);
    },
    fieldName() {
      return `${this.fieldPrefix}.${this.attribute.id}`;
    },
    getSelectOptions() {
      return parseAttributeOptions(this.attribute.options);
    },
    getMultiSelectLabels(rawValue) {
      if (!rawValue) return [];
      try {
        const parsed = JSON.parse(rawValue);
        if (Array.isArray(parsed)) {
          return resolveAttributeOptionLabels(this.attribute.options, parsed);
        }
        return [resolveAttributeOptionLabel(this.attribute.options, parsed)];
      } catch {
        return String(rawValue)
          .split(",")
          .map((value) => value.trim())
          .filter(Boolean)
          .map((value) =>
            resolveAttributeOptionLabel(this.attribute.options, value),
          );
      }
    },
    setMultiSelectByLabels(labels, setValue) {
      setValue(
        JSON.stringify(labelsToStoredValues(this.attribute.options, labels)),
      );
    },
    valueMaxLength() {
      const n = Number(this.attribute?.maxLength);
      return Number.isInteger(n) && n > 0 ? n : undefined;
    },
    valueMaxSelections() {
      const n = Number(this.attribute?.maxValues);
      return Number.isInteger(n) && n > 0 ? n : 0;
    },
  },
};
</script>

<template>
  <FormField
    v-slot="{ componentField, value, setValue, errorMessage }"
    :name="fieldName()"
    keep-value-on-unmount
  >
    <FormItem class="gap-0">
      <SettingsFormField
        :label="attribute.name"
        :label-for="'attr-' + attribute.id"
        :description="attribute.description"
        :description-in-tooltip="attribute.descriptionInTooltip === true"
        :required="attribute.isRequired"
        :error="errorMessage"
        :inline="isType(attribute.type, 'boolean')"
        :class="isType(attribute.type, 'boolean') ? 'self-end' : ''"
      >
        <Switch
          v-if="isType(attribute.type, 'boolean')"
          :id="'attr-' + attribute.id"
          :model-value="value === 'true'"
          @update:model-value="setValue($event ? 'true' : 'false')"
        />
        <FormControl
          v-else-if="isType(attribute.type, 'text') && attribute.isMultiline"
        >
          <Textarea
            :id="'attr-' + attribute.id"
            v-bind="componentField"
            :placeholder="attribute.defaultValue || ''"
            :maxlength="valueMaxLength()"
            class="min-h-24 resize-y"
          />
        </FormControl>
        <FormControl v-else-if="isType(attribute.type, 'text')">
          <Input
            :id="'attr-' + attribute.id"
            type="text"
            v-bind="componentField"
            :placeholder="attribute.defaultValue || ''"
            :maxlength="valueMaxLength()"
          />
        </FormControl>
        <FormControl v-else-if="isType(attribute.type, 'number')">
          <Input
            :id="'attr-' + attribute.id"
            type="text"
            inputmode="decimal"
            v-bind="componentField"
            :placeholder="attribute.defaultValue || ''"
          />
        </FormControl>
        <FormControl v-else-if="isType(attribute.type, 'select')">
          <SearchableSelect
            :model-value="value || ''"
            :options="getSelectOptions()"
            :placeholder="
              attribute.defaultValue ||
              $t('products.productAttributeForm.selectPlaceholder')
            "
            :invalid="!!errorMessage"
            :allow-create="!!attribute.allowCustomValues"
            @update:model-value="setValue"
          />
        </FormControl>
        <FormControl v-else-if="isType(attribute.type, 'multiselect')">
          <MultiSelectDropdown
            :options="getSelectOptions().map((option) => option.label)"
            :model-value="getMultiSelectLabels(value)"
            :invalid="!!errorMessage"
            :placeholder="$t('products.productAttributeForm.selectPlaceholder')"
            :allow-create="!!attribute.allowCustomValues"
            :max-selections="valueMaxSelections()"
            @update:model-value="setMultiSelectByLabels($event, setValue)"
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
            :placeholder="
              $t('products.productAttributeForm.unknownTypePlaceholder', {
                type: attribute.type,
              })
            "
          />
        </FormControl>
      </SettingsFormField>
    </FormItem>
  </FormField>
</template>
