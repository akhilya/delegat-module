<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Save,
  Plus,
  Trash2,
  GripVertical,
  Settings2,
  Download,
  Upload,
} from "lucide-vue-next";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { EmptyState } from "@/components/ui/empty-state";
import AttributeGroupIconPicker from "@/components/products/AttributeGroupIconPicker.vue";
import { suggestedGroupIcon } from "@/lib/attributeGroupIcon";
import { setPageTitle } from "@/router";
import draggable from "vuedraggable";
import {
  ATTRIBUTE_GROUP_OPTIONS,
  ATTRIBUTE_GROUP_VALUES,
} from "@/constants/attributeGroups";

// Threshold for large options lists (above this - show count only, not full list)
const LARGE_OPTIONS_THRESHOLD = 50;

// Lookup map for type i18n keys (supports both numeric and string keys from JSON)
const ATTRIBUTE_TYPES = {
  0: { i18nKey: "text", value: 0 },
  1: { i18nKey: "number", value: 1 },
  2: { i18nKey: "boolean", value: 2 },
  3: { i18nKey: "select", value: 3 },
  4: { i18nKey: "multiSelect", value: 4 },
  // String keys for JSON enum serialization (JsonStringEnumConverter)
  Text: { i18nKey: "text", value: 0 },
  Number: { i18nKey: "number", value: 1 },
  Boolean: { i18nKey: "boolean", value: 2 },
  Select: { i18nKey: "select", value: 3 },
  MultiSelect: { i18nKey: "multiSelect", value: 4 },
};

// Unique list of type values for the dropdown (no duplicates); labels are resolved via i18n in the component
const ATTRIBUTE_TYPE_VALUES = [0, 1, 2, 3, 4];

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    Button,
    Input,
    Textarea,
    Switch,
    SettingsFormField,
    SettingsFormSection,
    InsetGroup,
    InsetGroupItem,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogFooter,
    DialogClose,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogTrigger,
    Save,
    Plus,
    Trash2,
    GripVertical,
    Settings2,
    Download,
    Upload,
    draggable,
    SearchableSelect,
    EmptyState,
    AttributeGroupIconPicker,
  },
  data() {
    const categoryId = parseInt(this.$route.params.id);
    return {
      categoryId,
      category: null,
      attributes: [],
      isLoading: false,
      isSaving: false,
      isAddingAttribute: false,
      isDeletingAttribute: false,
      isReordering: false,
      FIELD_LIMITS,
      showAttributeDialog: false,
      editingAttribute: null,
      attributeForm: {
        name: "",
        key: "",
        type: 0,
        isRequired: false,
        isUnique: false,
        isMultiline: false,
        allowCustomValues: false,
        showOnProductPage: true,
        options: "",
        defaultValue: "",
        description: "",
        descriptionInTooltip: false,
        group: "",
        groupIcon: "",
        maxLength: "",
        maxValues: "",
        minValue: "",
        maxValue: "",
        validationPattern: "",
      },
      customGroupMode: false,
      attributeOptionsJson: null,
      cachedOptionsCount: 0,
      defaultValueOptions: [],
      defaultValueOptionsReady: false,
      isLoadingDefaultValueOptions: false,
      ATTRIBUTE_TYPES,
      ATTRIBUTE_TYPE_VALUES,
      ATTRIBUTE_GROUP_OPTIONS,
      LARGE_OPTIONS_THRESHOLD,
    };
  },
  computed: {
    attributeTypeLabel() {
      const i18nKey =
        ATTRIBUTE_TYPES[this.attributeForm.type]?.i18nKey || "text";
      return this.$t(`pages.categoryEditPage.attributeTypes.${i18nKey}`);
    },
    attributeTypeOptions() {
      return ATTRIBUTE_TYPE_VALUES.map((value) => ({
        value,
        label: this.$t(
          `pages.categoryEditPage.attributeTypes.${ATTRIBUTE_TYPES[value].i18nKey}`,
        ),
      }));
    },
    showOptionsField() {
      const typeValue =
        ATTRIBUTE_TYPES[this.attributeForm.type]?.value ??
        this.attributeForm.type;
      return typeValue === 3 || typeValue === 4;
    },
    isTextAttributeType() {
      const typeValue =
        ATTRIBUTE_TYPES[this.attributeForm.type]?.value ??
        this.attributeForm.type;
      return typeValue === 0;
    },
    isNumberAttributeType() {
      const typeValue =
        ATTRIBUTE_TYPES[this.attributeForm.type]?.value ??
        this.attributeForm.type;
      return typeValue === 1;
    },
    /**
     * Whether the current attribute has a large options list
     * (for such lists we only show a count, not the full text)
     */
    hasLargeOptions() {
      return this.cachedOptionsCount > LARGE_OPTIONS_THRESHOLD;
    },
    editingOptionsCount() {
      return this.cachedOptionsCount;
    },
    groupSelectOptions() {
      return [
        { value: "", label: this.$t("pages.categoryEditPage.noGroupOption") },
        ...ATTRIBUTE_GROUP_OPTIONS.map((option) => ({
          value: option.value,
          label: this.$t(`attributeGroups.${option.key}`),
        })),
      ];
    },
    defaultValueSelectOptions() {
      if (!this.defaultValueOptionsReady) return [];
      return this.defaultValueOptions;
    },
  },
  methods: {
    ...mapActions([
      "getCategory",
      "updateCategory",
      "getCategoryAttributes",
      "createAttribute",
      "updateAttribute",
      "deleteAttribute",
      "reorderAttributes",
    ]),

    parseOptionalInt(value) {
      const n = Number.parseInt(String(value ?? "").trim(), 10);
      return Number.isInteger(n) && n > 0 ? n : null;
    },

    parseOptionalDecimal(value) {
      const raw = String(value ?? "").trim();
      if (!raw) return null;
      const n = Number(raw);
      return Number.isFinite(n) ? n : null;
    },

    async fetchCategory() {
      this.isLoading = true;
      await this.getCategory({
        id: this.categoryId,
        onSuccess: (response) => {
          this.category = response.data.value;
          setPageTitle(this.category.name);
          this.attributes = this.category.attributes || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async handleSaveCategory() {
      const nameIssue = fieldLimitIssue(
        this.category.name,
        FIELD_LIMITS.categoryName,
      );
      if (nameIssue === "empty") {
        displaySonnerError(this.$t("pages.categoryEditPage.nameRequired"));
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.categoryName }),
        );
        return;
      }
      if (
        fieldLimitIssue(
          this.category.description,
          FIELD_LIMITS.categoryDescription,
          { required: false },
        ) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.categoryDescription }),
        );
        return;
      }

      this.isSaving = true;
      await this.updateCategory({
        id: this.categoryId,
        name: this.category.name.trim(),
        description: this.category.description?.trim() || null,
        hasSizes: this.category.hasSizes ?? false,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.categoryEditPage.saved"));
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isSaving = false;
    },

    resetAttributeDialogOptionsState() {
      this.cachedOptionsCount = 0;
      this.defaultValueOptions = [];
      this.defaultValueOptionsReady = false;
      this.isLoadingDefaultValueOptions = false;
    },

    syncOptionsCountCache() {
      const raw = this.getOptionsRaw();
      this.cachedOptionsCount = raw ? this.getOptionsCount(raw) : 0;
    },

    primeDefaultValueOptions() {
      const raw = this.getOptionsRaw();
      if (!raw) {
        this.defaultValueOptions = [];
        this.defaultValueOptionsReady = true;
        return;
      }

      if (this.cachedOptionsCount <= LARGE_OPTIONS_THRESHOLD) {
        this.defaultValueOptions = this.parseOptions(raw);
        this.defaultValueOptionsReady = true;
        return;
      }

      this.defaultValueOptions = [];
      this.defaultValueOptionsReady = false;
    },

    loadDefaultValueOptionsIfNeeded() {
      if (this.defaultValueOptionsReady || this.isLoadingDefaultValueOptions)
        return;

      const raw = this.getOptionsRaw();
      if (!raw) {
        this.defaultValueOptions = [];
        this.defaultValueOptionsReady = true;
        return;
      }

      this.isLoadingDefaultValueOptions = true;
      window.setTimeout(() => {
        this.defaultValueOptions = this.parseOptions(raw);
        this.defaultValueOptionsReady = true;
        this.isLoadingDefaultValueOptions = false;
      }, 0);
    },

    openAddAttributeDialog() {
      this.editingAttribute = null;
      this.customGroupMode = false;
      this.attributeOptionsJson = null;
      this.resetAttributeDialogOptionsState();
      this.attributeForm = {
        name: "",
        key: "",
        type: 0,
        isRequired: false,
        isUnique: false,
        isMultiline: false,
        allowCustomValues: false,
        showOnProductPage: true,
        options: "",
        defaultValue: "",
        description: "",
        descriptionInTooltip: false,
        group: "",
        groupIcon: "",
        maxLength: "",
        maxValues: "",
        minValue: "",
        maxValue: "",
        validationPattern: "",
      };
      this.showAttributeDialog = true;
    },

    openEditAttributeDialog(attribute) {
      this.resetAttributeDialogOptionsState();
      this.editingAttribute = attribute;
      this.attributeOptionsJson = null;
      // Normalize the type (may arrive as a string like "Select" or a number like 3)
      const normalizedType =
        ATTRIBUTE_TYPES[attribute.type]?.value ?? attribute.type;
      const group = attribute.group || "";
      // If the group is not in the predefined list, switch to custom mode
      this.customGroupMode =
        group !== "" && !ATTRIBUTE_GROUP_VALUES.includes(group);
      const optionsRaw = attribute.options || "";
      this.cachedOptionsCount = optionsRaw
        ? this.getOptionsCount(optionsRaw)
        : 0;
      this.attributeOptionsJson =
        this.cachedOptionsCount > LARGE_OPTIONS_THRESHOLD && optionsRaw
          ? optionsRaw
          : null;
      this.attributeForm = {
        name: attribute.name,
        key: attribute.key,
        type: normalizedType,
        isRequired: attribute.isRequired,
        isUnique: attribute.isUnique === true,
        isMultiline: attribute.isMultiline ?? false,
        allowCustomValues: attribute.allowCustomValues === true,
        showOnProductPage:
          attribute.isRequired || attribute.showOnProductPage !== false,
        options:
          this.cachedOptionsCount > LARGE_OPTIONS_THRESHOLD
            ? ""
            : this.formatOptionsForEdit(optionsRaw),
        defaultValue: attribute.defaultValue || "",
        description: attribute.description || "",
        descriptionInTooltip: attribute.descriptionInTooltip === true,
        group,
        groupIcon: attribute.groupIcon || "",
        maxLength:
          attribute.maxLength != null ? String(attribute.maxLength) : "",
        maxValues:
          attribute.maxValues != null ? String(attribute.maxValues) : "",
        minValue: attribute.minValue != null ? String(attribute.minValue) : "",
        maxValue: attribute.maxValue != null ? String(attribute.maxValue) : "",
        validationPattern: attribute.validationPattern || "",
      };
      this.primeDefaultValueOptions();
      this.showAttributeDialog = true;
    },

    onGroupSelect(value) {
      this.attributeForm.group = value || "";
      this.applyGroupIconHint(this.attributeForm.group);
    },

    onGroupNameHint() {
      this.applyGroupIconHint(this.attributeForm.group);
    },

    applyGroupIconHint(group) {
      const name = String(group || "").trim();
      if (!name) {
        this.attributeForm.groupIcon = "";
        return;
      }
      const editingId = this.editingAttribute?.id;
      const sibling = this.attributes.find(
        (attr) =>
          attr.group === name && attr.groupIcon && attr.id !== editingId,
      );
      if (sibling?.groupIcon) {
        this.attributeForm.groupIcon = sibling.groupIcon;
        return;
      }
      const suggested = suggestedGroupIcon(name);
      if (suggested) this.attributeForm.groupIcon = suggested;
    },

    generateKey() {
      if (!this.attributeForm.name) return;
      // Convert name to snake_case key. Cyrillic chars are written as \uXXXX escapes
      // below so this source file has no non-ASCII glyphs; runtime behavior is unchanged.
      this.attributeForm.key = this.attributeForm.name
        .toLowerCase()
        .replace(/[^a-z\u0400-\u04FF0-9\s]/gi, "")
        .replace(/\s+/g, "_")
        .replace(/[\u0400-\u04FF]/gi, (char) => {
          const translitMap = {
            "\u0430": "a",
            "\u0431": "b",
            "\u0432": "v",
            "\u0433": "g",
            "\u0434": "d",
            "\u0435": "e",
            "\u0451": "yo",
            "\u0436": "zh",
            "\u0437": "z",
            "\u0438": "i",
            "\u0439": "y",
            "\u043a": "k",
            "\u043b": "l",
            "\u043c": "m",
            "\u043d": "n",
            "\u043e": "o",
            "\u043f": "p",
            "\u0440": "r",
            "\u0441": "s",
            "\u0442": "t",
            "\u0443": "u",
            "\u0444": "f",
            "\u0445": "h",
            "\u0446": "ts",
            "\u0447": "ch",
            "\u0448": "sh",
            "\u0449": "sch",
            "\u044a": "",
            "\u044b": "y",
            "\u044c": "",
            "\u044d": "e",
            "\u044e": "yu",
            "\u044f": "ya",
          };
          return translitMap[char.toLowerCase()] || char;
        });
    },

    setAttributeRequired(value) {
      this.attributeForm.isRequired = !!value;
      if (this.attributeForm.isRequired) {
        this.attributeForm.showOnProductPage = true;
      }
    },

    setShowOnProductPage(value) {
      if (this.attributeForm.isRequired) {
        this.attributeForm.showOnProductPage = true;
        return;
      }
      this.attributeForm.showOnProductPage = !!value;
    },

    async handleSaveAttribute() {
      const nameIssue = fieldLimitIssue(
        this.attributeForm.name,
        FIELD_LIMITS.attributeName,
      );
      if (nameIssue === "empty") {
        displaySonnerError(this.$t("pages.categoryEditPage.attrNameRequired"));
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeName }),
        );
        return;
      }
      const keyIssue = fieldLimitIssue(
        this.attributeForm.key,
        FIELD_LIMITS.attributeKey,
      );
      if (keyIssue === "empty") {
        displaySonnerError(this.$t("pages.categoryEditPage.attrKeyRequired"));
        return;
      }
      if (keyIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeKey }),
        );
        return;
      }
      if (
        fieldLimitIssue(
          this.attributeForm.description,
          FIELD_LIMITS.attributeDescription,
          { required: false },
        ) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeDescription }),
        );
        return;
      }
      if (
        fieldLimitIssue(
          this.attributeForm.defaultValue,
          FIELD_LIMITS.attributeDefault,
          { required: false },
        ) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeDefault }),
        );
        return;
      }
      if (
        fieldLimitIssue(this.attributeForm.group, FIELD_LIMITS.attributeGroup, {
          required: false,
        }) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeGroup }),
        );
        return;
      }

      this.isAddingAttribute = true;

      const attributeData = {
        name: this.attributeForm.name.trim(),
        key: this.attributeForm.key.trim(),
        type: this.attributeForm.type,
        isRequired: this.attributeForm.isRequired,
        isUnique: this.attributeForm.isUnique,
        isMultiline: this.isTextAttributeType
          ? this.attributeForm.isMultiline
          : false,
        allowCustomValues: this.showOptionsField
          ? !!this.attributeForm.allowCustomValues
          : false,
        showOnProductPage:
          this.attributeForm.isRequired || this.attributeForm.showOnProductPage,
        options: this.showOptionsField ? this.resolveOptionsForSave() : null,
        defaultValue: this.attributeForm.defaultValue.trim() || null,
        description: this.attributeForm.description.trim() || null,
        descriptionInTooltip: this.attributeForm.descriptionInTooltip,
        group: this.attributeForm.group.trim() || null,
        groupIcon: this.attributeForm.group.trim()
          ? this.attributeForm.groupIcon.trim() || null
          : null,
        maxLength: this.parseOptionalInt(this.attributeForm.maxLength),
        maxValues: this.showOptionsField
          ? this.parseOptionalInt(this.attributeForm.maxValues)
          : null,
        minValue: this.parseOptionalDecimal(this.attributeForm.minValue),
        maxValue: this.parseOptionalDecimal(this.attributeForm.maxValue),
        validationPattern: this.attributeForm.validationPattern.trim() || null,
      };

      if (this.editingAttribute) {
        await this.updateAttribute({
          categoryId: this.categoryId,
          attributeId: this.editingAttribute.id,
          attribute: attributeData,
          onSuccess: () => {
            displaySonnerSuccess(this.$t("pages.categoryEditPage.attrUpdated"));
            this.showAttributeDialog = false;
            this.fetchCategory();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } else {
        await this.createAttribute({
          categoryId: this.categoryId,
          attribute: attributeData,
          onSuccess: () => {
            displaySonnerSuccess(this.$t("pages.categoryEditPage.attrAdded"));
            this.showAttributeDialog = false;
            this.fetchCategory();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      }

      this.isAddingAttribute = false;
    },

    async handleDeleteAttribute(attributeId) {
      this.isDeletingAttribute = true;
      await this.deleteAttribute({
        categoryId: this.categoryId,
        attributeId,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.categoryEditPage.attrDeleted"));
          this.fetchCategory();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeletingAttribute = false;
    },

    async handleReorder() {
      this.isReordering = true;
      const attributeIds = this.attributes.map((a) => a.id);
      await this.reorderAttributes({
        categoryId: this.categoryId,
        attributeIds,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.categoryEditPage.orderSaved"));
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isReordering = false;
    },

    getAttributeTypeLabel(type) {
      const i18nKey = ATTRIBUTE_TYPES[type]?.i18nKey;
      return i18nKey
        ? this.$t(`pages.categoryEditPage.attributeTypes.${i18nKey}`)
        : this.$t("pages.categoryEditPage.attributeTypes.unknown");
    },

    attributeRowSubtitle(attribute) {
      const parts = [
        attribute.key,
        this.getAttributeTypeLabel(attribute.type),
        attribute.group,
      ].filter(Boolean);
      const optionsCount = attribute.options
        ? this.getOptionsCount(attribute.options)
        : 0;
      if (optionsCount > 0) {
        parts.push(
          `${optionsCount} ${this.$t("pages.categoryEditPage.optionsWord")}`,
        );
      }
      return parts.join(" · ");
    },

    getOptionsRaw() {
      if (this.attributeOptionsJson) return this.attributeOptionsJson;
      if (this.attributeForm.options?.trim()) return this.attributeForm.options;
      return this.editingAttribute?.options || "";
    },

    resolveOptionsForSave() {
      if (this.attributeOptionsJson) return this.attributeOptionsJson;
      if (this.attributeForm.options?.trim())
        return this.serializeOptionsForSave();
      if (this.editingAttribute?.options) return this.editingAttribute.options;
      return null;
    },

    formatOptionsForTextarea(options) {
      return this.parseOptions(options)
        .map((option) =>
          option.value !== option.label
            ? `${option.value}:${option.label}`
            : option.label,
        )
        .join(", ");
    },

    applyImportedOptions(parsed) {
      if (!parsed.length) {
        displaySonnerError(this.$t("pages.categoryEditPage.fileNoOptions"));
        return;
      }

      const json = JSON.stringify(parsed);
      if (parsed.length > LARGE_OPTIONS_THRESHOLD) {
        this.attributeOptionsJson = json;
        this.attributeForm.options = "";
      } else {
        this.attributeOptionsJson = null;
        this.attributeForm.options = this.formatOptionsForTextarea(json);
      }

      if (this.editingAttribute) {
        this.editingAttribute = { ...this.editingAttribute, options: json };
      }

      this.cachedOptionsCount = parsed.length;
      this.primeDefaultValueOptions();
      displaySonnerSuccess(
        this.$t("pages.categoryEditPage.importedCount", {
          count: parsed.length.toLocaleString(),
        }),
      );
    },

    parseImportedOptionsText(text) {
      const trimmed = text.trim();
      if (!trimmed) return [];

      try {
        const json = JSON.parse(trimmed);
        if (Array.isArray(json)) {
          return json
            .map((item) => {
              if (typeof item === "string") {
                return { value: item, label: item };
              }
              const value = item.value ?? item.label ?? "";
              const label = item.label ?? item.value ?? "";
              return { value: String(value), label: String(label) };
            })
            .filter((option) => option.value);
        }
        if (Array.isArray(json.options)) {
          return this.parseImportedOptionsText(JSON.stringify(json.options));
        }
      } catch {
        // plain text / csv
      }

      const items = trimmed.includes("\n")
        ? trimmed.split(/\r?\n/)
        : trimmed.split(",");

      return items
        .map((item) => item.trim())
        .filter(Boolean)
        .map((item) => {
          if (item.includes(":")) {
            const [value, ...labelParts] = item.split(":");
            return {
              value: value.trim(),
              label: labelParts.join(":").trim(),
            };
          }
          return { value: item, label: item };
        });
    },

    exportAttributeOptions() {
      const raw = this.getOptionsRaw();
      if (!raw) {
        displaySonnerError(this.$t("pages.categoryEditPage.noOptionsToExport"));
        return;
      }

      const parsed = this.parseOptions(raw);
      if (!parsed.length) {
        displaySonnerError(this.$t("pages.categoryEditPage.noOptionsToExport"));
        return;
      }

      const json = JSON.stringify(parsed, null, 2);
      const blob = new Blob([json], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      const key = this.attributeForm.key?.trim() || "attribute";
      anchor.href = url;
      anchor.download = `${key}-options.json`;
      anchor.click();
      URL.revokeObjectURL(url);
      displaySonnerSuccess(
        this.$t("pages.categoryEditPage.exportedCount", {
          count: parsed.length.toLocaleString(),
        }),
      );
    },

    triggerOptionsImport() {
      this.$refs.optionsFileInput?.click();
    },

    handleOptionsImport(event) {
      const file = event.target.files?.[0];
      if (!file) return;

      const reader = new FileReader();
      reader.onload = (loadEvent) => {
        try {
          const text = loadEvent.target?.result;
          if (typeof text !== "string") {
            displaySonnerError(
              this.$t("pages.categoryEditPage.fileReadFailed"),
            );
            return;
          }
          const parsed = this.parseImportedOptionsText(text);
          this.applyImportedOptions(parsed);
        } catch {
          displaySonnerError(this.$t("pages.categoryEditPage.fileReadError"));
        }
      };
      reader.onerror = () => {
        displaySonnerError(this.$t("pages.categoryEditPage.fileReadError"));
      };
      reader.readAsText(file);
      event.target.value = "";
    },

    clearAttributeOptions() {
      this.attributeOptionsJson = null;
      this.attributeForm.options = "";
      this.attributeForm.defaultValue = "";
      if (this.editingAttribute) {
        this.editingAttribute = { ...this.editingAttribute, options: null };
      }
      this.resetAttributeDialogOptionsState();
    },

    onOptionsTextInput() {
      if (!this.hasLargeOptions) {
        this.syncOptionsCountCache();
        this.primeDefaultValueOptions();
      }
    },

    /**
     * Parses options from a string (supports JSON and comma-separated for backward compatibility)
     * @returns {Array<{value: string, label: string}>}
     */
    parseOptions(optionsStr) {
      if (!optionsStr) return [];

      // Try to parse as JSON
      try {
        const parsed = JSON.parse(optionsStr);
        if (Array.isArray(parsed)) {
          return parsed.map((o) => ({
            value: o.value || o.label || o,
            label: o.label || o.value || o,
          }));
        }
      } catch {
        // Not JSON - parse as comma-separated
      }

      // Fallback: comma-separated string (supports the "value:label" format)
      return optionsStr
        .split(",")
        .map((o) => o.trim())
        .filter(Boolean)
        .map((item) => {
          if (item.includes(":")) {
            const [value, ...labelParts] = item.split(":");
            return { value: value.trim(), label: labelParts.join(":").trim() };
          }
          return { value: item, label: item };
        });
    },

    /**
     * Formats options for display in the textarea (JSON -> comma-separated labels)
     * For large lists (>LARGE_OPTIONS_THRESHOLD) returns an empty string,
     * since they're displayed in read-only mode
     */
    formatOptionsForEdit(optionsStr) {
      if (!optionsStr) return "";

      // Try to parse as JSON
      try {
        const parsed = JSON.parse(optionsStr);
        if (Array.isArray(parsed)) {
          // Don't format large lists - they're shown as a count instead
          if (parsed.length > LARGE_OPTIONS_THRESHOLD) {
            return ""; // Empty string - the UI will show an info block
          }
          // Format: "value:label" if they differ, otherwise just the label
          return parsed
            .map((o) => {
              const value = o.value || o.label || o;
              const label = o.label || o.value || o;
              if (typeof o === "string") return o;
              return value !== label ? `${value}:${label}` : label;
            })
            .join(", ");
        }
      } catch {
        // Already in the right format
      }

      return optionsStr;
    },

    /**
     * Converts edited comma-separated options back to JSON for saving.
     * Reverses formatOptionsForEdit() so the DB always stores JSON format.
     */
    serializeOptionsForSave() {
      const raw = this.attributeForm.options.trim();
      if (!raw) return null;
      const parsed = this.parseOptions(raw);
      if (parsed.length === 0) return null;
      return JSON.stringify(parsed);
    },

    /**
     * Options count
     */
    getOptionsCount(optionsStr) {
      if (!optionsStr) return 0;

      try {
        const parsed = JSON.parse(optionsStr);
        if (Array.isArray(parsed)) return parsed.length;
      } catch {
        // not JSON
      }

      return this.parseOptions(optionsStr).length;
    },
  },
  mounted() {
    this.fetchCategory();
  },
};
</script>

<template>
  <AppPageChrome>
    <PageSection :loading="isLoading" class="!min-h-0">
      <div v-if="category" class="max-w-2xl space-y-1">
        <SettingsFormSection>
          <SettingsFormField
            :label="$t('common.name')"
            label-for="category-name"
          >
            <Input
              id="category-name"
              v-model="category.name"
              :placeholder="$t('pages.categoryEditPage.namePlaceholder')"
              :maxlength="FIELD_LIMITS.categoryName"
            />
          </SettingsFormField>
          <SettingsFormField
            :label="$t('common.description')"
            label-for="category-description"
          >
            <Textarea
              id="category-description"
              v-model="category.description"
              :placeholder="$t('pages.categoryEditPage.optionalPlaceholder')"
              class="min-h-24 resize-none"
              :maxlength="FIELD_LIMITS.categoryDescription"
            />
          </SettingsFormField>
          <SettingsFormField
            :label="$t('pages.categoryEditPage.hasSizesLabel')"
            :description="$t('pages.categoryEditPage.hasSizesDescription')"
            label-for="category-has-sizes"
            inline
          >
            <Switch
              id="category-has-sizes"
              :model-value="category.hasSizes"
              @update:model-value="category.hasSizes = $event"
            />
          </SettingsFormField>
          <template #footer>
            <Button
              @click="handleSaveCategory"
              :disabled="isSaving"
            >
              <Spinner v-if="isSaving" class="mr-2" />
              <Save v-else class="size-4 mr-2" />
              {{ $t("common.save") }}
            </Button>
          </template>
        </SettingsFormSection>

        <SettingsFormSection
          :title="$t('pages.categoryEditPage.attributesCardTitle')"
          :description="$t('pages.categoryEditPage.attributesOrderHint')"
        >
          <template #actions>
            <Button
              variant="outline"
              size="sm"
              @click="openAddAttributeDialog"
            >
              <Plus class="size-4 mr-2" />
              {{ $t("common.add") }}
            </Button>
          </template>
          <EmptyState
            v-if="attributes.length === 0"
            :title="$t('pages.categoryEditPage.noAttributesTitle')"
            :description="$t('pages.categoryEditPage.noAttributesDescription')"
            class="py-10"
          >
            <template #icon>
              <Settings2 />
            </template>
            <Button variant="outline" @click="openAddAttributeDialog">
              <Plus class="size-4 mr-2" />
              {{ $t("pages.categoryEditPage.addAttribute") }}
            </Button>
          </EmptyState>
          <InsetGroup v-else>
            <draggable
              v-model="attributes"
              item-key="id"
              handle=".drag-handle"
              ghost-class="opacity-50"
              class="flex flex-col gap-0.5"
              tag="div"
              @end="handleReorder"
            >
              <template #item="{ element: attribute }">
                <div>
                  <InsetGroupItem
                    as="div"
                    @click="openEditAttributeDialog(attribute)"
                  >
                    <template #leading>
                      <span
                        class="drag-handle pointer-events-auto flex size-8 cursor-grab items-center justify-center active:cursor-grabbing"
                        @click.stop
                      >
                        <GripVertical class="size-4 text-muted-foreground/70" />
                      </span>
                    </template>
                    <p class="truncate">
                      {{ attribute.name }}
                      <span
                        v-if="attribute.isRequired"
                        class="text-destructive"
                        :title="$t('pages.categoryEditPage.requiredFieldTitle')"
                      >*</span>
                      <span
                        v-if="attribute.isUnique"
                        class="ml-1 text-[10px] font-medium uppercase tracking-wide text-muted-foreground"
                        :title="$t('pages.categoryEditPage.uniqueFieldTitle')"
                      >{{ $t("pages.categoryEditPage.uniqueBadge") }}</span>
                    </p>
                    <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
                      {{ attributeRowSubtitle(attribute) }}
                    </p>
                    <template #trailing>
                      <AlertDialog>
                        <AlertDialogTrigger as-child>
                          <Button
                            variant="ghost"
                            size="icon"
                            class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                            :aria-label="$t('common.delete')"
                            @click.stop
                          >
                            <Trash2 class="size-4" />
                          </Button>
                        </AlertDialogTrigger>
                        <AlertDialogContent>
                          <AlertDialogHeader>
                            <AlertDialogTitle>{{
                              $t("pages.categoryEditPage.deleteAttributeTitle")
                            }}</AlertDialogTitle>
                            <AlertDialogDescription>
                              {{
                                $t(
                                  "pages.categoryEditPage.deleteAttributeDescription",
                                  { name: attribute.name },
                                )
                              }}
                            </AlertDialogDescription>
                          </AlertDialogHeader>
                          <AlertDialogFooter>
                            <AlertDialogCancel>{{
                              $t("common.cancel")
                            }}</AlertDialogCancel>
                            <AlertDialogAction
                              variant="destructive"
                              :disabled="isDeletingAttribute"
                              @click="handleDeleteAttribute(attribute.id)"
                            >
                              <Spinner
                                v-if="isDeletingAttribute"
                                class="mr-2"
                              />
                              {{ $t("common.delete") }}
                            </AlertDialogAction>
                          </AlertDialogFooter>
                        </AlertDialogContent>
                      </AlertDialog>
                    </template>
                  </InsetGroupItem>
                </div>
              </template>
            </draggable>
          </InsetGroup>
        </SettingsFormSection>
      </div>
    </PageSection>

    <!-- Attribute Dialog -->
    <Dialog v-model:open="showAttributeDialog">
      <DialogContent
        size="2xl"
        class="max-h-[min(90vh,48rem)] flex flex-col gap-0 p-0 overflow-hidden"
      >
        <DialogHeader variant="bar">
          <DialogTitle>
            {{
              editingAttribute
                ? $t("pages.categoryEditPage.editAttributeTitle")
                : $t("pages.categoryEditPage.newAttributeTitle")
            }}
          </DialogTitle>
          <DialogDescription>
            {{ $t("pages.categoryEditPage.attributeDialogSubtitle") }}
          </DialogDescription>
        </DialogHeader>

        <div class="flex-1 overflow-y-auto px-6 py-5">
          <SettingsFormSection
            class="!max-w-none"
            :title="$t('pages.categoryEditPage.basicSection')"
          >
              <div class="grid gap-4 sm:grid-cols-2">
                <SettingsFormField
                  :label="$t('common.name')"
                  label-for="attr-name"
                >
                  <Input
                    id="attr-name"
                    v-model="attributeForm.name"
                    :placeholder="
                      $t('pages.categoryEditPage.namePlaceholderExamples')
                    "
                    :maxlength="FIELD_LIMITS.attributeName"
                    @input="!editingAttribute && generateKey()"
                  />
                </SettingsFormField>
                <SettingsFormField
                  :label="$t('pages.categoryEditPage.keyLabel')"
                  label-for="attr-key"
                >
                  <template #description>{{
                    $t("pages.categoryEditPage.keyDescription")
                  }}</template>
                  <Input
                    id="attr-key"
                    v-model="attributeForm.key"
                    placeholder="color, size, brand"
                    class="font-mono text-sm"
                    :maxlength="FIELD_LIMITS.attributeKey"
                  />
                </SettingsFormField>
              </div>
              <SettingsFormField
                :label="$t('pages.categoryEditPage.typeLabel')"
                label-for="attr-type"
              >
                <Select v-model="attributeForm.type">
                  <SelectTrigger id="attr-type" class="w-full">
                    <SelectValue :placeholder="attributeTypeLabel" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem
                      v-for="typeInfo in attributeTypeOptions"
                      :key="typeInfo.value"
                      :value="typeInfo.value"
                    >
                      {{ typeInfo.label }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </SettingsFormField>
          </SettingsFormSection>

          <SettingsFormSection
            v-if="showOptionsField"
            class="!max-w-none"
            :title="$t('pages.categoryEditPage.optionsSection')"
          >
            <template #actions>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    :disabled="!editingOptionsCount"
                    @click="exportAttributeOptions"
                  >
                    <Download class="size-3.5 mr-1.5" />
                    {{ $t("pages.categoryEditPage.export") }}
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    @click="triggerOptionsImport"
                  >
                    <Upload class="size-3.5 mr-1.5" />
                    {{ $t("pages.categoryEditPage.import") }}
                  </Button>
            </template>
              <input
                ref="optionsFileInput"
                type="file"
                accept=".json,.csv,.txt,application/json,text/plain,text/csv"
                class="hidden"
                @change="handleOptionsImport"
              />

              <InsetGroup v-if="hasLargeOptions">
                <InsetGroupItem as="div">
                  <p>
                    <span class="tabular-nums font-medium">
                      {{ editingOptionsCount.toLocaleString() }}
                    </span>
                    <span class="text-muted-foreground">
                      {{ $t("pages.categoryEditPage.optionsWord") }}</span>
                  </p>
                  <p class="text-muted-foreground mt-0.5 text-xs font-normal">
                    {{ $t("pages.categoryEditPage.largeOptionsHint") }}
                  </p>
                  <template #trailing>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      @click="clearAttributeOptions"
                    >
                      <Trash2 class="size-3.5 mr-1.5" />
                      {{ $t("pages.categoryEditPage.clearOptions") }}
                    </Button>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
              <Textarea
                v-else
                v-model="attributeForm.options"
                :placeholder="
                  $t('pages.categoryEditPage.optionsPlaceholderExample')
                "
                class="min-h-24 resize-none font-mono text-sm"
                @input="onOptionsTextInput"
              />
              <p class="text-xs text-muted-foreground">
                {{ $t("pages.categoryEditPage.optionsFormatHint") }}
              </p>
              <SettingsFormField
                :label="$t('pages.categoryEditPage.allowCustomValuesLabel')"
                label-for="attr-allow-custom"
                inline
                :description="$t('pages.categoryEditPage.allowCustomValuesHint')"
              >
                <Switch
                  id="attr-allow-custom"
                  :model-value="attributeForm.allowCustomValues"
                  @update:model-value="
                    attributeForm.allowCustomValues = $event
                  "
                />
              </SettingsFormField>
              <SettingsFormField
                :label="$t('pages.categoryEditPage.maxValuesLabel')"
                label-for="attr-max-values"
              >
                <Input
                  id="attr-max-values"
                  v-model="attributeForm.maxValues"
                  type="number"
                  min="1"
                  inputmode="numeric"
                  :placeholder="
                    $t('pages.categoryEditPage.maxValuesPlaceholder')
                  "
                />
                <template #description>
                  {{ $t("pages.categoryEditPage.maxValuesHint") }}
                </template>
              </SettingsFormField>
          </SettingsFormSection>

          <SettingsFormSection
            class="!max-w-none"
            :title="$t('pages.categoryEditPage.additionalSection')"
          >
              <SettingsFormField
                :label="$t('pages.categoryEditPage.defaultValueLabel')"
                label-for="attr-default"
              >
                <div
                  v-if="attributeForm.type === 2"
                  class="flex items-center gap-2 h-9"
                >
                  <Switch
                    id="attr-default"
                    :model-value="attributeForm.defaultValue === 'true'"
                    @update:model-value="
                      attributeForm.defaultValue = $event ? 'true' : 'false'
                    "
                  />
                  <span class="text-sm text-muted-foreground">
                    {{
                      attributeForm.defaultValue === "true"
                        ? $t("common.yes")
                        : $t("common.no")
                    }}
                  </span>
                </div>
                <div
                  v-else-if="attributeForm.type === 3 && editingOptionsCount"
                  class="space-y-1.5"
                >
                  <SearchableSelect
                    id="attr-default"
                    :model-value="attributeForm.defaultValue"
                    @update:model-value="attributeForm.defaultValue = $event"
                    :options="defaultValueSelectOptions"
                    :disabled="isLoadingDefaultValueOptions"
                    :placeholder="
                      $t('pages.categoryEditPage.notSelectedPlaceholder')
                    "
                    :allow-create="!!attributeForm.allowCustomValues"
                    class="w-full"
                    @open="loadDefaultValueOptionsIfNeeded"
                  />
                  <p
                    v-if="hasLargeOptions && !defaultValueOptionsReady"
                    class="text-xs text-muted-foreground"
                  >
                    {{ $t("pages.categoryEditPage.lazyLoadHint") }}
                  </p>
                </div>
                <Input
                  v-else
                  id="attr-default"
                  v-model="attributeForm.defaultValue"
                  :placeholder="
                    attributeForm.type === 4
                      ? $t('pages.categoryEditPage.multiValuePlaceholder')
                      : $t('pages.categoryEditPage.optionalPlaceholder')
                  "
                  :type="attributeForm.type === 1 ? 'number' : 'text'"
                  :maxlength="FIELD_LIMITS.attributeDefault"
                />
                <template
                  v-if="attributeForm.type === 4 && editingOptionsCount"
                  #description
                >
                  {{ $t("pages.categoryEditPage.multiValueHint") }}
                </template>
              </SettingsFormField>

              <SettingsFormField
                :label="$t('common.description')"
                label-for="attr-description"
              >
                <Textarea
                  id="attr-description"
                  v-model="attributeForm.description"
                  :placeholder="
                    $t('pages.categoryEditPage.descriptionPlaceholder')
                  "
                  class="min-h-20 resize-none"
                  :maxlength="FIELD_LIMITS.attributeDescription"
                />
              </SettingsFormField>

              <SettingsFormField
                :label="$t('pages.categoryEditPage.descriptionInTooltipLabel')"
                label-for="attr-description-tooltip"
                inline
                :description="$t('pages.categoryEditPage.descriptionInTooltipHint')"
              >
                <Switch
                  id="attr-description-tooltip"
                  :model-value="attributeForm.descriptionInTooltip"
                  @update:model-value="
                    attributeForm.descriptionInTooltip = $event
                  "
                />
              </SettingsFormField>

              <SettingsFormField
                :label="$t('pages.categoryEditPage.groupLabel')"
                label-for="attr-group"
              >
                <div class="flex min-w-0 items-start gap-2">
                  <AttributeGroupIconPicker
                    :model-value="attributeForm.groupIcon"
                    :group-name="attributeForm.group"
                    @update:model-value="attributeForm.groupIcon = $event"
                  />
                  <div class="min-w-0 flex-1">
                    <Input
                      v-if="customGroupMode"
                      id="attr-group"
                      v-model="attributeForm.group"
                      :placeholder="
                        $t('pages.categoryEditPage.groupNamePlaceholder')
                      "
                      :maxlength="FIELD_LIMITS.attributeGroup"
                      @change="onGroupNameHint"
                    />
                    <SearchableSelect
                      v-else
                      id="attr-group"
                      :model-value="attributeForm.group"
                      :options="groupSelectOptions"
                      :placeholder="
                        $t('pages.categoryEditPage.noGroupPlaceholder')
                      "
                      class="w-full"
                      @update:model-value="onGroupSelect"
                    />
                  </div>
                </div>
                <template #description>
                  <Button
                    type="button"
                    variant="link"
                    class="h-auto p-0 text-xs"
                    @click="
                      customGroupMode = !customGroupMode;
                      if (!customGroupMode) {
                        attributeForm.group = '';
                        attributeForm.groupIcon = '';
                      }
                    "
                  >
                    {{
                      customGroupMode
                        ? $t("pages.categoryEditPage.chooseFromListLabel")
                        : $t("pages.categoryEditPage.customGroupLabel")
                    }}
                  </Button>
                </template>
              </SettingsFormField>

              <SettingsFormField
                v-if="isTextAttributeType"
                :label="$t('pages.categoryEditPage.multilineLabel')"
                label-for="attr-multiline"
                inline
                :description="$t('pages.categoryEditPage.multilineHint')"
              >
                <Switch
                  id="attr-multiline"
                  :model-value="attributeForm.isMultiline"
                  @update:model-value="attributeForm.isMultiline = $event"
                />
              </SettingsFormField>

              <SettingsFormField
                v-if="!isNumberAttributeType"
                :label="$t('pages.categoryEditPage.valueMaxLengthLabel')"
                label-for="attr-max-length"
              >
                <Input
                  id="attr-max-length"
                  v-model="attributeForm.maxLength"
                  type="number"
                  min="1"
                  inputmode="numeric"
                  :placeholder="
                    $t('pages.categoryEditPage.valueMaxLengthPlaceholder')
                  "
                />
                <template #description>
                  {{ $t("pages.categoryEditPage.valueMaxLengthHint") }}
                </template>
              </SettingsFormField>

              <template v-if="isNumberAttributeType">
                <SettingsFormField
                  :label="$t('pages.categoryEditPage.minValueLabel')"
                  label-for="attr-min-value"
                >
                  <Input
                    id="attr-min-value"
                    v-model="attributeForm.minValue"
                    type="text"
                    inputmode="decimal"
                    :placeholder="
                      $t('pages.categoryEditPage.minValuePlaceholder')
                    "
                  />
                </SettingsFormField>
                <SettingsFormField
                  :label="$t('pages.categoryEditPage.maxValueLabel')"
                  label-for="attr-max-value"
                >
                  <Input
                    id="attr-max-value"
                    v-model="attributeForm.maxValue"
                    type="text"
                    inputmode="decimal"
                    :placeholder="
                      $t('pages.categoryEditPage.maxValuePlaceholder')
                    "
                  />
                </SettingsFormField>
              </template>

              <SettingsFormField
                v-if="isTextAttributeType"
                :label="$t('pages.categoryEditPage.validationPatternLabel')"
                label-for="attr-pattern"
              >
                <Input
                  id="attr-pattern"
                  v-model="attributeForm.validationPattern"
                  :placeholder="
                    $t('pages.categoryEditPage.validationPatternPlaceholder')
                  "
                  :maxlength="500"
                />
                <template #description>
                  {{ $t("pages.categoryEditPage.validationPatternHint") }}
                </template>
              </SettingsFormField>

              <SettingsFormField
                :label="$t('pages.categoryEditPage.showOnProductPageLabel')"
                label-for="attr-visible"
                inline
                :description="
                  attributeForm.isRequired
                    ? $t('pages.categoryEditPage.showOnProductPageRequiredHint')
                    : $t('pages.categoryEditPage.showOnProductPageHint')
                "
              >
                <Switch
                  id="attr-visible"
                  :model-value="
                    attributeForm.isRequired ||
                    attributeForm.showOnProductPage
                  "
                  :disabled="attributeForm.isRequired"
                  @update:model-value="setShowOnProductPage"
                />
              </SettingsFormField>

              <SettingsFormField
                :label="$t('pages.categoryEditPage.requiredFieldTitle')"
                label-for="attr-required"
                inline
                :description="$t('pages.categoryEditPage.requiredFieldHint')"
              >
                <Switch
                  id="attr-required"
                  :model-value="attributeForm.isRequired"
                  @update:model-value="setAttributeRequired"
                />
              </SettingsFormField>

              <SettingsFormField
                :label="$t('pages.categoryEditPage.uniqueFieldTitle')"
                label-for="attr-unique"
                inline
                :description="$t('pages.categoryEditPage.uniqueFieldHint')"
              >
                <Switch
                  id="attr-unique"
                  :model-value="attributeForm.isUnique"
                  @update:model-value="attributeForm.isUnique = $event"
                />
              </SettingsFormField>
          </SettingsFormSection>
        </div>

        <DialogFooter variant="bar">
          <DialogClose as-child>
            <Button variant="outline">{{ $t("common.cancel") }}</Button>
          </DialogClose>
          <Button @click="handleSaveAttribute" :disabled="isAddingAttribute">
            <Spinner v-if="isAddingAttribute" class="mr-2" />
            {{ editingAttribute ? $t("common.save") : $t("common.add") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </AppPageChrome>
</template>
