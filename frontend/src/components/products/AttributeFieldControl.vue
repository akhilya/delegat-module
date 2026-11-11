<script>
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
  InputGroupText,
} from "@/components/ui/input-group";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Upload } from "lucide-vue-next";
import { FormControl, FormField, FormItem } from "@/components/ui/form";
import { isAttributeType } from "@/lib/productAttributeValidation";
import { parseAttributeFileIds } from "@/lib/productAttributeFiles";
import { getAttributeUnit } from "@/lib/attributeUnits";
import {
  labelsToStoredValues,
  parseAttributeOptions,
  resolveAttributeOptionLabel,
  resolveAttributeOptionLabels,
} from "@/lib/attributeOptions";
import { http } from "@/main";
import { displaySonnerError } from "@/store/sonnerHelper";

export default {
  name: "AttributeFieldControl",
  components: {
    Input,
    Textarea,
    Switch,
    SearchableSelect,
    MultiSelectDropdown,
    BarcodeInput,
    InputGroup,
    InputGroupAddon,
    InputGroupInput,
    InputGroupText,
    SettingsFormField,
    FormControl,
    FormField,
    FormItem,
    Button,
    Spinner,
    Upload,
  },
  emits: ["files-uploaded"],
  props: {
    attribute: { type: Object, required: true },
    fieldPrefix: { type: String, required: true },
    idPrefix: { type: String, default: "attr-" },
    files: { type: Array, default: () => [] },
    productId: { type: Number, default: null },
    categoryId: { type: Number, default: null },
    onFileUpload: { type: Function, default: null },
  },
  data() {
    return {
      isUploadingFile: false,
      isLoadingOptions: false,
      loadedOptions: null,
    };
  },
  watch: {
    "attribute.id"() {
      this.loadedOptions = null;
      this.isLoadingOptions = false;
    },
    categoryId() {
      this.loadedOptions = null;
      this.isLoadingOptions = false;
    },
  },
  methods: {
    isType(attrType, typeName) {
      return isAttributeType(attrType, typeName);
    },
    fieldName() {
      return `${this.fieldPrefix}.${this.attribute.id}`;
    },
    fieldId() {
      return `${this.idPrefix}${this.attribute.id}`;
    },
    getSelectOptions() {
      return parseAttributeOptions(
        this.loadedOptions ?? this.attribute.options,
      );
    },
    optionSource() {
      return this.loadedOptions ?? this.attribute.options;
    },
    async loadOptions() {
      if (
        this.isLoadingOptions ||
        this.loadedOptions !== null ||
        (this.attribute.options && !this.attribute.optionsPartial) ||
        !this.categoryId ||
        (!this.isType(this.attribute.type, "select") &&
          !this.isType(this.attribute.type, "multiselect"))
      )
        return;
      const categoryId = this.categoryId;
      const attributeId = this.attribute.id;
      this.isLoadingOptions = true;
      try {
        const response = await http.get(
          `/categories/${categoryId}/attributes/${attributeId}`,
        );
        if (this.categoryId !== categoryId || this.attribute.id !== attributeId)
          return;
        const definition = response.data?.value ?? response.data;
        this.loadedOptions = definition?.options ?? "[]";
      } catch (error) {
        if (this.categoryId === categoryId && this.attribute.id === attributeId)
          displaySonnerError(error);
      } finally {
        if (this.categoryId === categoryId && this.attribute.id === attributeId)
          this.isLoadingOptions = false;
      }
    },
    getMultiSelectLabels(rawValue) {
      if (!rawValue) return [];
      try {
        const parsed = JSON.parse(rawValue);
        if (Array.isArray(parsed)) {
          return resolveAttributeOptionLabels(this.optionSource(), parsed);
        }
        return [resolveAttributeOptionLabel(this.optionSource(), parsed)];
      } catch {
        return String(rawValue)
          .split(",")
          .map((value) => value.trim())
          .filter(Boolean)
          .map((value) =>
            resolveAttributeOptionLabel(this.optionSource(), value),
          );
      }
    },
    setMultiSelectByLabels(labels, setValue) {
      setValue(
        JSON.stringify(labelsToStoredValues(this.optionSource(), labels)),
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
    fileIds(rawValue) {
      return parseAttributeFileIds(rawValue) || [];
    },
    toggleFile(fileId, rawValue, setValue) {
      const ids = this.fileIds(rawValue);
      const id = Number(fileId);
      const next = ids.includes(id)
        ? ids.filter((item) => item !== id)
        : [...ids, id];
      setValue(
        JSON.stringify(
          this.attribute.allowMultipleFiles ? next : next.slice(-1),
        ),
      );
    },
    async uploadAttributeFiles(event, rawValue, setValue) {
      const input = event.target;
      const selected = Array.from(input.files || []);
      if (!selected.length) return;

      this.isUploadingFile = true;
      try {
        let uploaded;
        if (this.productId) {
          const data = new FormData();
          for (const file of selected) data.append("files", file, file.name);
          const response = await http({
            url: `/products/${this.productId}/files`,
            method: "PUT",
            data,
            timeout: 60 * 60 * 1000,
          });
          const body = response.data?.value ?? response.data;
          const rows = Array.isArray(body) ? body : [body];
          uploaded = rows.map((file) => ({
            ...file,
            id: file.id ?? file.Id,
            fileName: file.fileName ?? file.displayName ?? file.name ?? "",
          }));
          this.$emit("files-uploaded", uploaded);
        } else if (this.onFileUpload) {
          uploaded = await this.onFileUpload(selected, this.attribute);
        } else {
          return;
        }

        const newIds = (uploaded || [])
          .map((file) => Number(file.id ?? file.Id))
          .filter(Number.isInteger);
        if (!newIds.length) return;

        const current = this.fileIds(rawValue);
        const combined = this.attribute.allowMultipleFiles
          ? [...current, ...newIds]
          : newIds.slice(-1);
        setValue(JSON.stringify([...new Set(combined)].slice(0, 100)));
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.isUploadingFile = false;
        input.value = "";
      }
    },
  },
  computed: {
    unitDisplay() {
      const code = String(this.attribute?.unitCode ?? "").trim();
      if (!code) return "";

      const unit = getAttributeUnit(code);
      if (!unit) return code;

      return this.$t(`components.attributeUnitSelect.units.${unit.code}`);
    },
    fieldConstraints() {
      const constraints = [];
      const attribute = this.attribute;

      if (this.isType(attribute?.type, "number")) {
        const min = attribute.minValue ?? null;
        const max = attribute.maxValue ?? null;
        const hasMin = min !== null && min !== "";
        const hasMax = max !== null && max !== "";

        if (hasMin && hasMax) {
          constraints.push({
            key: "number-range",
            text: this.$t("lib.productAttributeValidation.numberRange", {
              min,
              max,
            }),
          });
        } else if (hasMin) {
          constraints.push({
            key: "min-value",
            text: this.$t("lib.productAttributeValidation.minValue", { min }),
          });
        } else if (hasMax) {
          constraints.push({
            key: "max-value",
            text: this.$t("lib.productAttributeValidation.maxValue", { max }),
          });
        }
      }

      const maxLength = this.valueMaxLength();
      if (maxLength && !this.isType(attribute?.type, "file")) {
        constraints.push({
          key: "max-length",
          text: this.$t("lib.productAttributeValidation.tooLong", {
            max: maxLength,
          }),
        });
      }

      const maxValues = this.isType(attribute?.type, "multiselect")
        ? this.valueMaxSelections()
        : 0;
      if (maxValues) {
        constraints.push({
          key: "max-values",
          text: this.$t("lib.productAttributeValidation.tooManyValues", {
            max: maxValues,
          }),
        });
      }

      const pattern = String(attribute?.validationPattern ?? "").trim();
      if (
        pattern &&
        !this.isType(attribute?.type, "boolean") &&
        !this.isType(attribute?.type, "multiselect") &&
        !this.isType(attribute?.type, "file")
      ) {
        constraints.push({
          key: "pattern",
          label: this.$t(
            "components.attributeFieldControl.constraints.pattern",
          ),
          value: pattern,
        });
      }

      return constraints;
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
        :label-for="fieldId()"
        :description="attribute.description"
        :description-in-tooltip="attribute.descriptionInTooltip === true"
        :required="attribute.isRequired"
        :error="errorMessage"
        :inline="isType(attribute.type, 'boolean')"
        :class="isType(attribute.type, 'boolean') ? 'self-end' : ''"
      >
        <Switch
          v-if="isType(attribute.type, 'boolean')"
          :id="fieldId()"
          :model-value="value === 'true'"
          @update:model-value="setValue($event ? 'true' : 'false')"
        />
        <FormControl
          v-else-if="isType(attribute.type, 'text') && attribute.isMultiline"
        >
          <Textarea
            :id="fieldId()"
            v-bind="componentField"
            :placeholder="attribute.defaultValue || ''"
            :maxlength="valueMaxLength()"
            class="min-h-24 resize-y"
          />
        </FormControl>
        <FormControl v-else-if="isType(attribute.type, 'text')">
          <Input
            :id="fieldId()"
            type="text"
            v-bind="componentField"
            :placeholder="attribute.defaultValue || ''"
            :maxlength="valueMaxLength()"
          />
        </FormControl>
        <div
          v-else-if="isType(attribute.type, 'number') && unitDisplay"
          class="min-w-0"
        >
          <InputGroup>
            <FormControl>
              <InputGroupInput
                :id="fieldId()"
                type="text"
                inputmode="decimal"
                v-bind="componentField"
                :placeholder="attribute.defaultValue || ''"
                :aria-label="`${attribute.name} ${unitDisplay}`"
              />
            </FormControl>
            <InputGroupAddon
              align="inline-end"
              class="max-w-[60%] min-w-0 gap-1.5 overflow-hidden"
            >
              <InputGroupText
                class="min-w-0 truncate whitespace-nowrap text-xs font-medium"
                :class="errorMessage ? 'text-destructive' : 'text-foreground'"
                :title="unitDisplay"
              >
                {{ unitDisplay }}
              </InputGroupText>
            </InputGroupAddon>
          </InputGroup>
        </div>
        <FormControl v-else-if="isType(attribute.type, 'number')">
          <Input
            :id="fieldId()"
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
            @open="loadOptions"
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
            @open="loadOptions"
            @update:model-value="setMultiSelectByLabels($event, setValue)"
          />
        </FormControl>
        <FormControl v-else-if="isType(attribute.type, 'barcode')">
          <BarcodeInput
            :model-value="value || ''"
            @update:model-value="setValue"
          />
        </FormControl>
        <div v-else-if="isType(attribute.type, 'file')" class="grid gap-2">
          <div class="flex flex-wrap items-center gap-2">
            <input
              ref="attributeFileInput"
              type="file"
              class="sr-only"
              :multiple="attribute.allowMultipleFiles === true"
              :aria-label="$t('components.attributeFieldControl.uploadFile')"
              @change="uploadAttributeFiles($event, value, setValue)"
            />
            <Button
              type="button"
              variant="outline"
              size="sm"
              :disabled="isUploadingFile"
              @click="$refs.attributeFileInput?.click()"
            >
              <Spinner v-if="isUploadingFile" class="mr-2 size-4" />
              <Upload v-else class="mr-2 size-4" />
              {{ $t("components.attributeFieldControl.uploadFile") }}
            </Button>
            <span class="text-xs text-muted-foreground">
              {{
                attribute.allowMultipleFiles
                  ? $t("components.attributeFieldControl.multipleFilesHint")
                  : $t("components.attributeFieldControl.singleFileHint")
              }}
            </span>
          </div>
          <label
            v-for="file in files"
            :key="file.id"
            class="flex min-w-0 items-center gap-2 rounded-md border px-3 py-2 text-sm"
          >
            <input
              type="checkbox"
              :disabled="isUploadingFile"
              :checked="fileIds(value).includes(file.id)"
              @change="toggleFile(file.id, value, setValue)"
            />
            <span class="min-w-0 truncate">{{
              file.fileName || file.displayName || file.name
            }}</span>
            <span class="ml-auto shrink-0 text-xs text-muted-foreground">{{
              file.contentType
            }}</span>
          </label>
          <p v-if="!files.length" class="text-sm text-muted-foreground">
            {{ $t("products.productAttributeForm.noFiles") }}
          </p>
        </div>
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
        <div
          v-if="fieldConstraints.length"
          class="flex min-w-0 flex-wrap items-center text-xs leading-4 text-muted-foreground"
        >
          <span
            v-for="(constraint, index) in fieldConstraints"
            :key="constraint.key"
            class="inline-flex min-w-0 items-center"
          >
            <span
              v-if="index > 0"
              aria-hidden="true"
              class="mx-2 h-3 w-px shrink-0 bg-border/70"
            />
            <template v-if="constraint.value !== undefined">
              <span>{{ constraint.label }}</span>
              <code
                class="ms-1.5 max-w-48 truncate font-mono text-foreground/80"
                :title="constraint.value"
              >
                {{ constraint.value }}
              </code>
            </template>
            <template v-else>{{ constraint.text }}</template>
          </span>
        </div>
      </SettingsFormField>
    </FormItem>
  </FormField>
</template>
