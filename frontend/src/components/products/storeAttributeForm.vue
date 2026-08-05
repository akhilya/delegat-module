<script>
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import { EmptyState } from "@/components/ui/empty-state";
import { FormControl, FormField, FormItem } from "@/components/ui/form";
import { isAttributeType } from "@/lib/productAttributeValidation";
import {
  labelsToStoredValues,
  parseAttributeOptions,
  resolveAttributeOptionLabel,
  resolveAttributeOptionLabels,
} from "@/lib/attributeOptions";

export default {
  components: {
    Input,
    Textarea,
    Switch,
    SearchableSelect,
    MultiSelectDropdown,
    BarcodeInput,
    SettingsFormField,
    FieldGroup,
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
      return parseAttributeOptions(attribute.options);
    },

    getMultiSelectLabels(attribute, rawValue) {
      if (!rawValue) return [];
      try {
        const parsed = JSON.parse(rawValue);
        if (Array.isArray(parsed)) {
          return resolveAttributeOptionLabels(attribute.options, parsed);
        }
        return [resolveAttributeOptionLabel(attribute.options, parsed)];
      } catch {
        return rawValue
          .split(",")
          .map((value) => value.trim())
          .filter(Boolean)
          .map((value) =>
            resolveAttributeOptionLabel(attribute.options, value),
          );
      }
    },

    setMultiSelectByLabels(attribute, labels, setValue) {
      setValue(JSON.stringify(labelsToStoredValues(attribute.options, labels)));
    },

    isFullWidthField(attribute) {
      return this.isType(attribute.type, "text") && attribute.isMultiline;
    },
  },
};
</script>

<template>
  <FieldGroup class="gap-5">
    <div
      class="grid min-w-0 grid-cols-1 gap-5 md:grid-cols-[repeat(2,minmax(0,1fr))]"
    >
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
            :description-in-tooltip="attribute.descriptionInTooltip === true"
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
            <FormControl
              v-else-if="
                isType(attribute.type, 'text') && attribute.isMultiline
              "
            >
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
                :placeholder="attribute.defaultValue || $t('products.storeAttributeForm.selectPlaceholder')"
                :invalid="!!errorMessage"
                @update:model-value="setValue"
              />
            </FormControl>
            <FormControl v-else-if="isType(attribute.type, 'multiselect')">
              <MultiSelectDropdown
                :options="
                  getSelectOptions(attribute).map((option) => option.label)
                "
                :model-value="getMultiSelectLabels(attribute, value)"
                :invalid="!!errorMessage"
                :placeholder="$t('products.storeAttributeForm.selectPlaceholder')"
                @update:model-value="
                  setMultiSelectByLabels(attribute, $event, setValue)
                "
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
                :placeholder="$t('products.storeAttributeForm.unknownTypePlaceholder', { type: attribute.type })"
              />
            </FormControl>
          </SettingsFormField>
        </FormItem>
      </FormField>
    </div>

    </div>

    <EmptyState
      v-if="sortedAttributes.length === 0"
      size="sm"
      :title="$t('products.storeAttributeForm.emptyTitle')"
      :description="$t('products.storeAttributeForm.emptyDescription')"
      class="md:col-span-2 py-8"
    />
  </FieldGroup>
</template>
