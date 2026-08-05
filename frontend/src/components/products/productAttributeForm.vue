<script>
import { ref, watch, nextTick } from "vue";
import { useFormErrors, useFormValues, useSubmitCount } from "vee-validate";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import { EmptyState } from "@/components/ui/empty-state";
import { FormControl, FormField, FormItem } from "@/components/ui/form";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import {
  ATTRIBUTE_GROUP_OPTIONS,
  attributeGroupKeyForValue,
} from "@/constants/attributeGroups";
import {
  isAttributeType,
  isEmptyAttributeValue,
} from "@/lib/productAttributeValidation";
import {
  labelsToStoredValues,
  parseAttributeOptions,
  resolveAttributeOptionLabel,
  resolveAttributeOptionLabels,
} from "@/lib/attributeOptions";

// Fallback bucket name for attributes without a group, kept as the literal Cyrillic
// value stored on legacy attribute records (written as a Unicode escape to avoid
// non-ASCII source). Displayed via t("attributeGroups.other").
const OTHER_GROUP = "\u041f\u0440\u043e\u0447\u0435\u0435";

/** Collect attribute ids under `prefix` that currently have a form error. */
function collectInvalidIds(errors, prefix) {
  const ids = new Set();
  if (!errors || typeof errors !== "object") return ids;

  const nested = errors[prefix];
  if (nested && typeof nested === "object" && !Array.isArray(nested)) {
    for (const [key, value] of Object.entries(nested)) {
      if (value) ids.add(String(key));
    }
  }

  for (const [key, value] of Object.entries(errors)) {
    if (!value || typeof value === "object") continue;

    // vee-validate + zod normalize numeric keys to brackets:
    // categoryAttributes[12]  (not categoryAttributes.12)
    if (key.startsWith(`${prefix}[`)) {
      const end = key.indexOf("]");
      if (end > prefix.length + 1) {
        ids.add(key.slice(prefix.length + 1, end));
      }
      continue;
    }

    if (key.startsWith(`${prefix}.`)) {
      ids.add(key.slice(prefix.length + 1).split(/[.\[]/)[0]);
    }
  }

  return ids;
}

/** Required-but-empty attrs from form values (works when fields are unmounted). */
function collectEmptyRequiredIds(attributes, valuesById) {
  const ids = new Set();
  const values = valuesById || {};
  for (const attr of attributes || []) {
    if (attr.showOnProductPage === false) continue;
    if (!attr.isRequired) continue;
    const id = String(attr.id);
    const raw = values[id] ?? values[attr.id];
    if (isEmptyAttributeValue(attr, raw)) ids.add(id);
  }
  return ids;
}

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
    FieldGroup,
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
  setup(props) {
    const formErrors = useFormErrors();
    const formValues = useFormValues();
    const submitCount = useSubmitCount();
    /** Attribute ids whose group header should show as destructive. */
    const flaggedAttributeIds = ref([]);

    /**
     * Full replace - never sticky. Red while the field still has a form error
     * or (after a save attempt) is still a required empty value.
     */
    function recomputeFlaggedIds() {
      const next = collectInvalidIds(formErrors.value, props.fieldPrefix);

      if (submitCount.value > 0) {
        const bucket = formValues.value?.[props.fieldPrefix];
        for (const id of collectEmptyRequiredIds(props.attributes, bucket)) {
          next.add(id);
        }
      }

      const alive = new Set(
        (props.attributes || [])
          .filter((a) => a.showOnProductPage !== false)
          .map((a) => String(a.id)),
      );
      flaggedAttributeIds.value = [...next].filter((id) => alive.has(id));
    }

    watch(formErrors, recomputeFlaggedIds, { deep: true, immediate: true });
    watch(formValues, recomputeFlaggedIds, { deep: true });
    watch(submitCount, async () => {
      await nextTick();
      recomputeFlaggedIds();
    });
    watch(() => props.attributes, recomputeFlaggedIds);

    return {
      flaggedAttributeIds,
    };
  },
  data() {
    return {
      groupOrder: ATTRIBUTE_GROUP_OPTIONS.map((option) => option.value),
    };
  },
  computed: {
    sortedAttributes() {
      return [...this.attributes]
        .filter((attr) => attr.showOnProductPage !== false)
        .sort((a, b) => a.sortOrder - b.sortOrder);
    },
    groupedAttributes() {
      // Groups are bucketed by the raw value stored on each attribute record; the
      // display label is resolved separately via groupLabel() so predefined groups
      // can be translated while custom (user-typed) group names pass through as-is.
      const groups = {};

      for (const attr of this.sortedAttributes) {
        const groupName = attr.group || OTHER_GROUP;
        if (!groups[groupName]) {
          groups[groupName] = [];
        }
        groups[groupName].push(attr);
      }

      const result = [];

      for (const groupName of this.groupOrder) {
        if (groupName === OTHER_GROUP) continue;
        if (groups[groupName]) {
          result.push({
            name: groupName,
            label: this.groupLabel(groupName),
            attributes: groups[groupName],
          });
          delete groups[groupName];
        }
      }

      for (const [groupName, attrs] of Object.entries(groups)) {
        if (groupName === OTHER_GROUP) continue;
        result.push({
          name: groupName,
          label: this.groupLabel(groupName),
          attributes: attrs,
        });
      }

      if (groups[OTHER_GROUP]) {
        result.push({
          name: OTHER_GROUP,
          label: this.groupLabel(OTHER_GROUP),
          attributes: groups[OTHER_GROUP],
        });
      }

      return result;
    },
    showGroupHeaders() {
      return (
        this.groupedAttributes.length > 1 ||
        (this.groupedAttributes.length === 1 &&
          this.groupedAttributes[0].name !== OTHER_GROUP)
      );
    },
    defaultOpenGroups() {
      return this.groupedAttributes.map((group) => group.name);
    },
    /** Reactive map: group name → has flagged invalid attribute. */
    groupErrorState() {
      const flagged = new Set(this.flaggedAttributeIds);
      const state = {};
      for (const group of this.groupedAttributes) {
        state[group.name] = group.attributes.some((attr) =>
          flagged.has(String(attr.id)),
        );
      }
      return state;
    },
  },
  methods: {
    groupLabel(groupName) {
      const key = attributeGroupKeyForValue(groupName);
      return key ? this.$t(`attributeGroups.${key}`) : groupName;
    },

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
        :data-invalid="groupErrorState[group.name] ? 'true' : undefined"
        :class="groupErrorState[group.name] ? 'border-destructive' : undefined"
      >
        <AccordionTrigger
          class="py-3"
          :class="
            groupErrorState[group.name]
              ? 'text-destructive [&_svg]:text-destructive'
              : 'text-muted-foreground'
          "
        >
          <span
            :class="
              groupErrorState[group.name]
                ? 'text-destructive'
                : undefined
            "
            :style="
              groupErrorState[group.name]
                ? { color: 'var(--destructive)' }
                : undefined
            "
          >
            {{ group.label }}
          </span>
        </AccordionTrigger>
        <AccordionContent>
          <FieldGroup class="gap-5">
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
                        :placeholder="attribute.defaultValue || $t('products.productAttributeForm.selectPlaceholder')"
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
                        :placeholder="$t('products.productAttributeForm.selectPlaceholder')"
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
                        :placeholder="$t('products.productAttributeForm.unknownTypePlaceholder', { type: attribute.type })"
                      />
                    </FormControl>
                  </SettingsFormField>
                </FormItem>
              </FormField>
            </div>
            </div>
          </FieldGroup>
        </AccordionContent>
      </AccordionItem>
    </Accordion>

    <template v-else>
      <div
        v-for="group in groupedAttributes"
        :key="group.name"
        class="space-y-5"
      >
        <FieldGroup class="gap-5">
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
                      :placeholder="attribute.defaultValue || $t('products.productAttributeForm.selectPlaceholder')"
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
                      :placeholder="$t('products.productAttributeForm.selectPlaceholder')"
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
                      :placeholder="$t('products.productAttributeForm.unknownTypePlaceholder', { type: attribute.type })"
                    />
                  </FormControl>
                </SettingsFormField>
              </FormItem>
            </FormField>
          </div>
          </div>
        </FieldGroup>
      </div>
    </template>

    <EmptyState
      v-if="sortedAttributes.length === 0"
      size="sm"
      :title="$t('products.productAttributeForm.emptyTitle')"
      :description="$t('products.productAttributeForm.emptyDescription')"
    />
  </div>
</template>
