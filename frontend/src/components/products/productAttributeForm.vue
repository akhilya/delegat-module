<script>
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { EmptyState } from "@/components/ui/empty-state";
import { FormControl, FormField, FormItem } from "@/components/ui/form";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { ATTRIBUTE_GROUP_OPTIONS } from "@/constants/attributeGroups";
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
    EmptyState,
    SearchableSelect,
    MultiSelectDropdown,
    BarcodeInput,
    SettingsFormField,
    FormControl,
    FormField,
    FormItem,
    Accordion,
    AccordionContent,
    AccordionItem,
    AccordionTrigger,
  },
  props: {
    attributes: {
      type: Array,
      required: true,
    },
    fieldPrefix: {
      type: String,
      default: "categoryAttributes",
    },
  },
  data() {
    return {
      groupOrder: ATTRIBUTE_GROUP_OPTIONS,
    };
  },
  computed: {
    sortedAttributes() {
      return [...this.attributes]
        .filter((attr) => attr.showOnProductPage !== false)
        .sort((a, b) => a.sortOrder - b.sortOrder);
    },
    groupedAttributes() {
      const groups = {};

      for (const attr of this.sortedAttributes) {
        const groupName = attr.group || "Прочее";
        if (!groups[groupName]) {
          groups[groupName] = [];
        }
        groups[groupName].push(attr);
      }

      const result = [];

      for (const groupName of this.groupOrder) {
        if (groupName === "Прочее") continue;
        if (groups[groupName]) {
          result.push({ name: groupName, attributes: groups[groupName] });
          delete groups[groupName];
        }
      }

      for (const [groupName, attrs] of Object.entries(groups)) {
        if (groupName === "Прочее") continue;
        result.push({ name: groupName, attributes: attrs });
      }

      if (groups["Прочее"]) {
        result.push({ name: "Прочее", attributes: groups["Прочее"] });
      }

      return result;
    },
    showGroupHeaders() {
      return (
        this.groupedAttributes.length > 1 ||
        (this.groupedAttributes.length === 1 &&
          this.groupedAttributes[0].name !== "Прочее")
      );
    },
    defaultOpenGroups() {
      return this.groupedAttributes.map((group) => group.name);
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
  <div class="min-w-0 space-y-8">
    <Accordion
      v-if="showGroupHeaders"
      type="multiple"
      :default-value="defaultOpenGroups"
    >
      <AccordionItem
        v-for="group in groupedAttributes"
        :key="group.name"
        :value="group.name"
      >
        <AccordionTrigger class="text-muted-foreground py-3">
          {{ group.name }}
        </AccordionTrigger>
        <AccordionContent>
          <div
            class="grid min-w-0 grid-cols-1 gap-5 md:grid-cols-[repeat(2,minmax(0,1fr))]"
          >
            <div
              v-for="attribute in group.attributes"
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
                      v-else-if="
                        isType(attribute.type, 'text') && attribute.isMultiline
                      "
                    >
                      <Textarea
                        :id="'attr-' + attribute.id"
                        v-bind="componentField"
                        :placeholder="attribute.defaultValue || ''"
                        class="min-h-24 resize-y"
                      />
                    </FormControl>
                    <FormControl v-else-if="isType(attribute.type, 'text')">
                      <Input
                        :id="'attr-' + attribute.id"
                        type="text"
                        v-bind="componentField"
                        :placeholder="attribute.defaultValue || ''"
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
                        :options="getSelectOptions(attribute)"
                        :placeholder="attribute.defaultValue || 'Выберите...'"
                        :invalid="!!errorMessage"
                        @update:model-value="setValue"
                      />
                    </FormControl>
                    <FormControl
                      v-else-if="isType(attribute.type, 'multiselect')"
                    >
                      <MultiSelectDropdown
                        :options="
                          getSelectOptions(attribute).map(
                            (option) => option.label,
                          )
                        "
                        :model-value="getMultiSelectLabels(attribute, value)"
                        :invalid="!!errorMessage"
                        placeholder="Выберите..."
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
                        :placeholder="`Неизвестный тип: ${attribute.type}`"
                      />
                    </FormControl>
                  </SettingsFormField>
                </FormItem>
              </FormField>
            </div>
          </div>
        </AccordionContent>
      </AccordionItem>
    </Accordion>

    <template v-else>
      <div
        v-for="group in groupedAttributes"
        :key="group.name"
        class="space-y-5"
      >
        <div
          class="grid min-w-0 grid-cols-1 gap-5 md:grid-cols-[repeat(2,minmax(0,1fr))]"
        >
          <div
            v-for="attribute in group.attributes"
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
                    v-else-if="
                      isType(attribute.type, 'text') && attribute.isMultiline
                    "
                  >
                    <Textarea
                      :id="'attr-' + attribute.id"
                      v-bind="componentField"
                      :placeholder="attribute.defaultValue || ''"
                      class="min-h-24 resize-y"
                    />
                  </FormControl>
                  <FormControl v-else-if="isType(attribute.type, 'text')">
                    <Input
                      :id="'attr-' + attribute.id"
                      type="text"
                      v-bind="componentField"
                      :placeholder="attribute.defaultValue || ''"
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
                      :options="getSelectOptions(attribute)"
                      :placeholder="attribute.defaultValue || 'Выберите...'"
                      :invalid="!!errorMessage"
                      @update:model-value="setValue"
                    />
                  </FormControl>
                  <FormControl
                    v-else-if="isType(attribute.type, 'multiselect')"
                  >
                    <MultiSelectDropdown
                      :options="
                        getSelectOptions(attribute).map(
                          (option) => option.label,
                        )
                      "
                      :model-value="getMultiSelectLabels(attribute, value)"
                      :invalid="!!errorMessage"
                      placeholder="Выберите..."
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
                      :placeholder="`Неизвестный тип: ${attribute.type}`"
                    />
                  </FormControl>
                </SettingsFormField>
              </FormItem>
            </FormField>
          </div>
        </div>
      </div>
    </template>

    <EmptyState
      v-if="sortedAttributes.length === 0"
      size="sm"
      title="Нет атрибутов для заполнения"
      description="Добавьте атрибуты в категорию товара — они появятся здесь."
    />
  </div>
</template>
