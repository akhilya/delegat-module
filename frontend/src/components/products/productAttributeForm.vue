<script>
// ProductAttributeForm - Renders a dynamic form for category attribute values, grouped and sorted by attribute config.
import Input from "@/components/ui/input/Input.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Label from "@/components/ui/label/Label.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { Info } from "lucide-vue-next";
import { ATTRIBUTE_GROUP_OPTIONS } from "@/constants/attributeGroups";

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
    Switch,
    Label,
    SearchableSelect,
    MultiSelectDropdown,
    BarcodeInput,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
    Info,
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
  data() {
    return {
      groupOrder: ATTRIBUTE_GROUP_OPTIONS,
    };
  },
  computed: {
    sortedAttributes() {
      return [...this.attributes].sort((a, b) => a.sortOrder - b.sortOrder);
    },
    /**
     * Groups attributes by their `group` field.
     * Returns an array of { name, attributes } objects in the fixed display order.
     */
    groupedAttributes() {
      const groups = {};

      // Group attributes by their group name
      for (const attr of this.sortedAttributes) {
        const groupName = attr.group || "Прочее";
        if (!groups[groupName]) {
          groups[groupName] = [];
        }
        groups[groupName].push(attr);
      }

      // Convert to array in fixed order
      const result = [];

      // Add predefined groups in order, SKIP "Прочее" (appended last)
      for (const groupName of this.groupOrder) {
        if (groupName === "Прочее") continue;
        if (groups[groupName]) {
          result.push({ name: groupName, attributes: groups[groupName] });
          delete groups[groupName];
        }
      }

      // Append custom groups (not in predefined list, not "Прочее")
      for (const [groupName, attrs] of Object.entries(groups)) {
        if (groupName === "Прочее") continue;
        result.push({ name: groupName, attributes: attrs });
      }

      // Always append "Прочее" last
      if (groups["Прочее"]) {
        result.push({ name: "Прочее", attributes: groups["Прочее"] });
      }

      return result;
    },
    /**
     * Whether to show group headers. Hidden when all attributes are in the single "Other" group.
     */
    showGroupHeaders() {
      return this.groupedAttributes.length > 1 ||
        (this.groupedAttributes.length === 1 && this.groupedAttributes[0].name !== "Прочее");
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

    getValue(attributeId) {
      const attrValue = this.modelValue.find(
        (av) => av.attributeId === attributeId,
      );
      return attrValue?.value || "";
    },

    setValue(attributeId, value) {
      const newValues = [...this.modelValue];
      const index = newValues.findIndex((av) => av.attributeId === attributeId);

      if (index >= 0) {
        if (value === "" || value === null || value === undefined) {
          newValues.splice(index, 1);
        } else {
          newValues[index] = { attributeId, value: String(value) };
        }
      } else if (value !== "" && value !== null && value !== undefined) {
        newValues.push({ attributeId, value: String(value) });
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

    getMultiSelectValues(attributeId) {
      const value = this.getValue(attributeId);
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

    setMultiSelectValues(attributeId, values) {
      this.setValue(attributeId, JSON.stringify(values));
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
  },
};
</script>

<template>
  <div class="space-y-6">
    <!-- Grouped attributes -->
    <div v-for="group in groupedAttributes" :key="group.name" class="space-y-4">
      <!-- Group header -->
      <h4
        v-if="showGroupHeaders"
        class="font-medium text-sm text-muted-foreground pb-2 border-b"
      >
        {{ group.name }}
      </h4>

      <!-- Attributes grid -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div
          v-for="attribute in group.attributes"
          :key="attribute.id"
          class="space-y-2"
        >
          <div class="flex items-center gap-1.5">
            <Label class="leading-none">
              {{ attribute.name }}
              <span v-if="attribute.isRequired" class="text-red-500">*</span>
            </Label>
            <TooltipProvider v-if="attribute.description">
              <Tooltip>
                <TooltipTrigger as-child>
                  <Info class="size-3.5 text-muted-foreground cursor-help shrink-0" />
                </TooltipTrigger>
                <TooltipContent side="top" class="max-w-xs">
                  <p>{{ attribute.description }}</p>
                </TooltipContent>
              </Tooltip>
            </TooltipProvider>
          </div>

          <!-- Text (type 0) -->
          <Input
            v-if="isType(attribute.type, 'text')"
            :model-value="getValue(attribute.id)"
            @update:model-value="setValue(attribute.id, $event)"
            :placeholder="attribute.defaultValue || ''"
            :required="attribute.isRequired"
            :class="{ 'border-red-500 focus-visible:ring-red-500': hasError(attribute.id) }"
          />

          <!-- Number (type 1) -->
          <Input
            v-else-if="isType(attribute.type, 'number')"
            type="number"
            :model-value="getValue(attribute.id)"
            @update:model-value="setValue(attribute.id, $event)"
            :placeholder="attribute.defaultValue || ''"
            :required="attribute.isRequired"
            :class="{ 'border-red-500 focus-visible:ring-red-500': hasError(attribute.id) }"
          />

          <!-- Boolean (type 2) -->
          <div
            v-else-if="isType(attribute.type, 'boolean')"
            class="flex items-center gap-2"
          >
            <Switch
              :model-value="getValue(attribute.id) === 'true'"
              @update:model-value="
                setValue(attribute.id, $event ? 'true' : 'false')
              "
            />
            <span class="text-sm text-muted-foreground">
              {{ getValue(attribute.id) === "true" ? "Да" : "Нет" }}
            </span>
          </div>

          <!-- Select (type 3) - with search for large option lists -->
          <SearchableSelect
            v-else-if="isType(attribute.type, 'select')"
            :model-value="getValue(attribute.id)"
            @update:model-value="setValue(attribute.id, $event)"
            :options="getSelectOptions(attribute)"
            :placeholder="attribute.defaultValue || 'Выберите...'"
            :class="{ 'border-red-500': hasError(attribute.id) }"
          />

          <!-- MultiSelect (type 4) -->
          <MultiSelectDropdown
            v-else-if="isType(attribute.type, 'multiselect')"
            :options="getSelectOptions(attribute).map(o => o.label)"
            :model-value="getMultiSelectLabels(attribute)"
            @update:model-value="setMultiSelectByLabels(attribute, $event)"
            placeholder="Выберите..."
            :class="{ 'border-red-500': hasError(attribute.id) }"
          />

          <!-- Barcode (type 5) -->
          <BarcodeInput
            v-else-if="isType(attribute.type, 'barcode')"
            :model-value="getValue(attribute.id)"
            @update:model-value="setValue(attribute.id, $event)"
          />

          <!-- Fallback for unknown types -->
          <Input
            v-else
            :model-value="getValue(attribute.id)"
            @update:model-value="setValue(attribute.id, $event)"
            :placeholder="`Неизвестный тип: ${attribute.type}`"
            :class="{ 'border-red-500 focus-visible:ring-red-500': hasError(attribute.id) }"
          />

          <!-- Error message -->
          <p v-if="getError(attribute.id)" class="text-sm text-red-500 mt-1">
            {{ getError(attribute.id) }}
          </p>
        </div>
      </div>
    </div>

    <p
      v-if="sortedAttributes.length === 0"
      class="text-sm text-muted-foreground text-center py-4"
    >
      Нет атрибутов для заполнения
    </p>
  </div>
</template>
