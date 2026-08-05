<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";

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
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  TableHead,
  TableHeader,
  TableRow,
  TableCell,
} from "@/components/ui/table";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Save,
  Plus,
  Trash2,
  GripVertical,
  Settings2,
  Pencil,
  Download,
  Upload,
} from "lucide-vue-next";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_CATEGORIES, setPageTitle } from "@/router";
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
  "Text": { i18nKey: "text", value: 0 },
  "Number": { i18nKey: "number", value: 1 },
  "Boolean": { i18nKey: "boolean", value: 2 },
  "Select": { i18nKey: "select", value: 3 },
  "MultiSelect": { i18nKey: "multiSelect", value: 4 },
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
    Label,
    Badge,
    SettingsFormField,
    FieldGroup,
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
    Card,
    CardAction,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
    TableHead,
    TableHeader,
    TableRow,
    TableCell,
    Save,
    Plus,
    Trash2,
    GripVertical,
    Settings2,
    Pencil,
    Download,
    Upload,
    draggable,
    SearchableSelect,
    EmptyState
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
      showAttributeDialog: false,
      editingAttribute: null,
      attributeForm: {
        name: "",
        key: "",
        type: 0,
        isRequired: false,
        isUnique: false,
        isMultiline: false,
        showOnProductPage: true,
        options: "",
        defaultValue: "",
        description: "",
        descriptionInTooltip: false,
        group: "",
      },
      customGroupMode: false,
      attributeOptionsJson: null,
      cachedOptionsCount: 0,
      defaultValueOptions: [],
      defaultValueOptionsReady: false,
      isLoadingDefaultValueOptions: false,
      PAGE_CATEGORIES,
      ATTRIBUTE_TYPES,
      ATTRIBUTE_TYPE_VALUES,
      ATTRIBUTE_GROUP_OPTIONS,
      LARGE_OPTIONS_THRESHOLD,
    };
  },
  computed: {
    breadcrumbs() {
      const items = [{ label: this.$t("routes.categories"), to: PAGE_CATEGORIES }];
      if (this.category?.name) {
        items.push({ label: this.category.name });
      } else {
        items.push({ label: this.$t("pages.categoryEditPage.breadcrumbEditing") });
      }
      return items;
    },
    attributeTypeLabel() {
      const i18nKey = ATTRIBUTE_TYPES[this.attributeForm.type]?.i18nKey || "text";
      return this.$t(`pages.categoryEditPage.attributeTypes.${i18nKey}`);
    },
    attributeTypeOptions() {
      return ATTRIBUTE_TYPE_VALUES.map((value) => ({
        value,
        label: this.$t(`pages.categoryEditPage.attributeTypes.${ATTRIBUTE_TYPES[value].i18nKey}`),
      }));
    },
    showOptionsField() {
      const typeValue = ATTRIBUTE_TYPES[this.attributeForm.type]?.value ?? this.attributeForm.type;
      return typeValue === 3 || typeValue === 4;
    },
    isTextAttributeType() {
      const typeValue = ATTRIBUTE_TYPES[this.attributeForm.type]?.value ?? this.attributeForm.type;
      return typeValue === 0;
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
    attributesCountLabel() {
      const n = this.attributes.length;
      const mod10 = n % 10;
      const mod100 = n % 100;
      if (mod100 >= 11 && mod100 <= 14) return this.$t("pages.categoryEditPage.attributesCount.many");
      if (mod10 === 1) return this.$t("pages.categoryEditPage.attributesCount.one");
      if (mod10 >= 2 && mod10 <= 4) return this.$t("pages.categoryEditPage.attributesCount.few");
      return this.$t("pages.categoryEditPage.attributesCount.many");
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
      if (!this.category.name.trim()) {
        displaySonnerError(this.$t("pages.categoryEditPage.nameRequired"));
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
      if (this.defaultValueOptionsReady || this.isLoadingDefaultValueOptions) return;

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
        showOnProductPage: true,
        options: "",
        defaultValue: "",
        description: "",
        descriptionInTooltip: false,
        group: "",
      };
      this.showAttributeDialog = true;
    },

    openEditAttributeDialog(attribute) {
      this.resetAttributeDialogOptionsState();
      this.editingAttribute = attribute;
      this.attributeOptionsJson = null;
      // Normalize the type (may arrive as a string like "Select" or a number like 3)
      const normalizedType = ATTRIBUTE_TYPES[attribute.type]?.value ?? attribute.type;
      const group = attribute.group || "";
      // If the group is not in the predefined list, switch to custom mode
      this.customGroupMode = group !== "" && !ATTRIBUTE_GROUP_VALUES.includes(group);
      const optionsRaw = attribute.options || "";
      this.cachedOptionsCount = optionsRaw ? this.getOptionsCount(optionsRaw) : 0;
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
        showOnProductPage: attribute.showOnProductPage !== false,
        options: this.cachedOptionsCount > LARGE_OPTIONS_THRESHOLD
          ? ""
          : this.formatOptionsForEdit(optionsRaw),
        defaultValue: attribute.defaultValue || "",
        description: attribute.description || "",
        descriptionInTooltip: attribute.descriptionInTooltip === true,
        group,
      };
      this.primeDefaultValueOptions();
      this.showAttributeDialog = true;
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
            "\u0430": "a", "\u0431": "b", "\u0432": "v", "\u0433": "g",
            "\u0434": "d", "\u0435": "e", "\u0451": "yo", "\u0436": "zh",
            "\u0437": "z", "\u0438": "i", "\u0439": "y", "\u043a": "k",
            "\u043b": "l", "\u043c": "m", "\u043d": "n", "\u043e": "o",
            "\u043f": "p", "\u0440": "r", "\u0441": "s", "\u0442": "t",
            "\u0443": "u", "\u0444": "f", "\u0445": "h", "\u0446": "ts",
            "\u0447": "ch", "\u0448": "sh", "\u0449": "sch", "\u044a": "",
            "\u044b": "y", "\u044c": "", "\u044d": "e", "\u044e": "yu",
            "\u044f": "ya",
          };
          return translitMap[char.toLowerCase()] || char;
        });
    },

    async handleSaveAttribute() {
      if (!this.attributeForm.name.trim()) {
        displaySonnerError(this.$t("pages.categoryEditPage.attrNameRequired"));
        return;
      }
      if (!this.attributeForm.key.trim()) {
        displaySonnerError(this.$t("pages.categoryEditPage.attrKeyRequired"));
        return;
      }

      this.isAddingAttribute = true;

      const attributeData = {
        name: this.attributeForm.name.trim(),
        key: this.attributeForm.key.trim(),
        type: this.attributeForm.type,
        isRequired: this.attributeForm.isRequired,
        isUnique: this.attributeForm.isUnique,
        isMultiline: this.isTextAttributeType ? this.attributeForm.isMultiline : false,
        showOnProductPage: this.attributeForm.showOnProductPage,
        options: this.showOptionsField ? this.resolveOptionsForSave() : null,
        defaultValue: this.attributeForm.defaultValue.trim() || null,
        description: this.attributeForm.description.trim() || null,
        descriptionInTooltip: this.attributeForm.descriptionInTooltip,
        group: this.attributeForm.group.trim() || null,
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

    getOptionsRaw() {
      if (this.attributeOptionsJson) return this.attributeOptionsJson;
      if (this.attributeForm.options?.trim()) return this.attributeForm.options;
      return this.editingAttribute?.options || "";
    },

    resolveOptionsForSave() {
      if (this.attributeOptionsJson) return this.attributeOptionsJson;
      if (this.attributeForm.options?.trim()) return this.serializeOptionsForSave();
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
        this.$t("pages.categoryEditPage.importedCount", { count: parsed.length.toLocaleString() }),
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
        this.$t("pages.categoryEditPage.exportedCount", { count: parsed.length.toLocaleString() }),
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
            displaySonnerError(this.$t("pages.categoryEditPage.fileReadFailed"));
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
          return parsed.map(o => ({
            value: o.value || o.label || o,
            label: o.label || o.value || o
          }));
        }
      } catch {
        // Not JSON - parse as comma-separated
      }

      // Fallback: comma-separated string (supports the "value:label" format)
      return optionsStr.split(',').map(o => o.trim()).filter(Boolean).map(item => {
        if (item.includes(':')) {
          const [value, ...labelParts] = item.split(':');
          return { value: value.trim(), label: labelParts.join(':').trim() };
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
          return parsed.map(o => {
            const value = o.value || o.label || o;
            const label = o.label || o.value || o;
            if (typeof o === 'string') return o;
            return value !== label ? `${value}:${label}` : label;
          }).join(', ');
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
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <PageSection :loading="isLoading" class="!min-h-0">
      <div v-if="category" class="space-y-3">
        <div class="flex items-start justify-between gap-4">
          <div class="min-w-0 flex-1 space-y-1">
            <h1 class="text-lg font-semibold">
              {{ category.name || $t("pages.categoryEditPage.editingCategoryFallback") }}
            </h1>
            <p class="text-sm text-muted-foreground">
              {{ $t("pages.categoryEditPage.subtitle") }}
            </p>
          </div>
          <Button
            class="shrink-0"
            @click="handleSaveCategory"
            :disabled="isSaving"
          >
            <Spinner v-if="isSaving" class="mr-2" />
            <Save v-else class="size-4 mr-2" />
            {{ $t("common.save") }}
          </Button>
        </div>

        <!-- Block 1: Settings -->
        <FieldGroup class="max-w-2xl gap-4">
            <SettingsFormField :label="$t('common.name')" label-for="category-name">
              <Input
                id="category-name"
                v-model="category.name"
                :placeholder="$t('pages.categoryEditPage.namePlaceholder')"
              />
            </SettingsFormField>
            <SettingsFormField :label="$t('common.description')" label-for="category-description">
              <Textarea
                id="category-description"
                v-model="category.description"
                :placeholder="$t('pages.categoryEditPage.optionalPlaceholder')"
                class="min-h-24 resize-none"
              />
            </SettingsFormField>
            <SettingsFormField :label="$t('pages.categoryEditPage.sizeGridLabel')">
              <div class="flex items-center gap-2 h-9">
                <Switch
                  id="category-has-sizes"
                  :model-value="category.hasSizes"
                  @update:model-value="category.hasSizes = $event"
                />
                <Label for="category-has-sizes" class="font-normal text-muted-foreground">
                  {{ $t("pages.categoryEditPage.hasSizesLabel") }}
                </Label>
              </div>
            </SettingsFormField>
        </FieldGroup>

        <!-- Block 2: Attributes -->
        <Card class="overflow-hidden border-0 shadow-none !gap-3 !py-0">
          <CardHeader class="!px-0">
            <CardTitle class="text-base">
              {{ $t("pages.categoryEditPage.attributesCardTitle") }}
            </CardTitle>
            <CardDescription>
              {{ attributes.length }} {{ attributesCountLabel }}.
              {{ $t("pages.categoryEditPage.attributesOrderHint") }}
            </CardDescription>
            <CardAction>
              <Button
                variant="outline"
                size="sm"
                @click="openAddAttributeDialog"
              >
                <Plus class="size-4 mr-2" />
                {{ $t("common.add") }}
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent class="!px-0 pb-6 pt-0">
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

            <div
              v-else
              class="overflow-hidden rounded-lg border"
            >
              <div class="overflow-x-auto">
                <table class="w-full caption-bottom text-sm">
                  <TableHeader>
                    <TableRow class="hover:bg-transparent">
                      <TableHead class="w-9 px-3" />
                      <TableHead class="min-w-[11rem] px-3 sm:min-w-[16rem]">{{ $t("common.name") }}</TableHead>
                      <TableHead class="hidden sm:table-cell px-3">{{ $t("pages.categoryEditPage.keyLabel") }}</TableHead>
                      <TableHead class="hidden md:table-cell px-3">{{ $t("pages.categoryEditPage.groupLabel") }}</TableHead>
                      <TableHead class="px-3">{{ $t("pages.categoryEditPage.typeHeader") }}</TableHead>
                      <TableHead class="hidden lg:table-cell w-28 text-right px-3">{{ $t("pages.categoryEditPage.optionsHeader") }}</TableHead>
                      <TableHead class="hidden xl:table-cell px-3">{{ $t("common.description") }}</TableHead>
                      <TableHead class="w-24 px-2" />
                    </TableRow>
                  </TableHeader>
                  <draggable
                    v-model="attributes"
                    tag="tbody"
                    item-key="id"
                    handle=".drag-handle"
                    ghost-class="opacity-50"
                    @end="handleReorder"
                  >
                    <template #item="{ element: attribute }">
                      <TableRow class="group">
                        <TableCell class="w-9 px-3">
                          <GripVertical
                            class="drag-handle size-4 text-muted-foreground/70 cursor-grab active:cursor-grabbing hover:text-foreground"
                          />
                        </TableCell>
                        <TableCell class="min-w-[11rem] px-3 font-medium sm:min-w-[16rem]">
                          <div class="flex min-w-0 items-center gap-0.5">
                            <span class="truncate">{{ attribute.name }}</span>
                            <span
                              v-if="attribute.isRequired"
                              class="shrink-0 text-destructive"
                              :title="$t('pages.categoryEditPage.requiredFieldTitle')"
                            >*</span>
                            <span
                              v-if="attribute.isUnique"
                              class="shrink-0 ml-1 text-[10px] font-medium uppercase tracking-wide text-muted-foreground"
                              :title="$t('pages.categoryEditPage.uniqueFieldTitle')"
                            >{{ $t("pages.categoryEditPage.uniqueBadge") }}</span>
                          </div>
                          <span class="text-xs text-muted-foreground font-mono truncate block sm:hidden">
                            {{ attribute.key }}
                          </span>
                        </TableCell>
                        <TableCell class="hidden sm:table-cell px-3 font-mono text-xs text-muted-foreground">
                          {{ attribute.key }}
                        </TableCell>
                        <TableCell class="hidden md:table-cell px-3">
                          <Badge
                            v-if="attribute.group"
                            variant="secondary"
                            class="font-normal text-xs"
                          >
                            {{ attribute.group }}
                          </Badge>
                          <span v-else class="text-muted-foreground">{{ $t("common.emDash") }}</span>
                        </TableCell>
                        <TableCell class="px-3 text-muted-foreground">
                          {{ getAttributeTypeLabel(attribute.type) }}
                        </TableCell>
                        <TableCell class="hidden lg:table-cell px-3 text-right tabular-nums text-muted-foreground">
                          {{ attribute.options ? getOptionsCount(attribute.options) : $t("common.emDash") }}
                        </TableCell>
                        <TableCell class="hidden xl:table-cell px-3 text-muted-foreground max-w-xs truncate">
                          {{ attribute.description || $t("common.emDash") }}
                        </TableCell>
                        <TableCell class="px-2">
                          <div class="flex items-center justify-end opacity-0 group-hover:opacity-100 transition-opacity">
                            <Button
                              variant="ghost"
                              size="icon"
                              class="size-8"
                              @click="openEditAttributeDialog(attribute)"
                            >
                              <Pencil class="size-4" />
                            </Button>
                            <AlertDialog>
                              <AlertDialogTrigger as-child>
                                <Button
                                  variant="ghost"
                                  size="icon"
                                  class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                                >
                                  <Trash2 class="size-4" />
                                </Button>
                              </AlertDialogTrigger>
                              <AlertDialogContent>
                                <AlertDialogHeader>
                                  <AlertDialogTitle>{{ $t("pages.categoryEditPage.deleteAttributeTitle") }}</AlertDialogTitle>
                                  <AlertDialogDescription>
                                    {{ $t("pages.categoryEditPage.deleteAttributeDescription", { name: attribute.name }) }}
                                  </AlertDialogDescription>
                                </AlertDialogHeader>
                                <AlertDialogFooter>
                                  <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
                                  <AlertDialogAction
                                    variant="destructive"
                                    @click="handleDeleteAttribute(attribute.id)"
                                    :disabled="isDeletingAttribute"
                                  >
                                    <Spinner v-if="isDeletingAttribute" class="mr-2" />
                                    {{ $t("common.delete") }}
                                  </AlertDialogAction>
                                </AlertDialogFooter>
                              </AlertDialogContent>
                            </AlertDialog>
                          </div>
                        </TableCell>
                      </TableRow>
                    </template>
                  </draggable>
                </table>
              </div>
            </div>
          </CardContent>
        </Card>
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
            {{ editingAttribute ? $t("pages.categoryEditPage.editAttributeTitle") : $t("pages.categoryEditPage.newAttributeTitle") }}
          </DialogTitle>
          <DialogDescription>
            {{ $t("pages.categoryEditPage.attributeDialogSubtitle") }}
          </DialogDescription>
        </DialogHeader>

        <div class="flex-1 overflow-y-auto px-6 py-5">
          <FieldGroup class="gap-6">
            <section class="space-y-4">
              <h3 class="text-sm font-medium">{{ $t("pages.categoryEditPage.basicSection") }}</h3>
              <div class="grid gap-4 sm:grid-cols-2">
                <SettingsFormField :label="$t('common.name')" label-for="attr-name">
                  <Input
                    id="attr-name"
                    v-model="attributeForm.name"
                    :placeholder="$t('pages.categoryEditPage.namePlaceholderExamples')"
                    @input="!editingAttribute && generateKey()"
                  />
                </SettingsFormField>
                <SettingsFormField :label="$t('pages.categoryEditPage.keyLabel')" label-for="attr-key">
                  <template #description>{{ $t("pages.categoryEditPage.keyDescription") }}</template>
                  <Input
                    id="attr-key"
                    v-model="attributeForm.key"
                    placeholder="color, size, brand"
                    class="font-mono text-sm"
                  />
                </SettingsFormField>
              </div>
              <SettingsFormField :label="$t('pages.categoryEditPage.typeLabel')" label-for="attr-type">
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
            </section>

            <section v-if="showOptionsField" class="space-y-3">
              <div class="flex items-center justify-between gap-3">
                <h3 class="text-sm font-medium">{{ $t("pages.categoryEditPage.optionsSection") }}</h3>
                <div class="flex items-center gap-2">
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
                </div>
              </div>
              <input
                ref="optionsFileInput"
                type="file"
                accept=".json,.csv,.txt,application/json,text/plain,text/csv"
                class="hidden"
                @change="handleOptionsImport"
              />

              <div
                v-if="hasLargeOptions"
                class="flex items-center justify-between gap-4 rounded-lg border px-4 py-3"
              >
                <div class="min-w-0">
                  <p class="text-sm">
                    <span class="font-medium tabular-nums">
                      {{ editingOptionsCount.toLocaleString() }}
                    </span>
                    <span class="text-muted-foreground"> {{ $t("pages.categoryEditPage.optionsWord") }}</span>
                  </p>
                  <p class="text-xs text-muted-foreground mt-0.5">
                    {{ $t("pages.categoryEditPage.largeOptionsHint") }}
                  </p>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  class="shrink-0"
                  @click="clearAttributeOptions"
                >
                  <Trash2 class="size-3.5 mr-1.5" />
                  {{ $t("pages.categoryEditPage.clearOptions") }}
                </Button>
              </div>
              <Textarea
                v-else
                v-model="attributeForm.options"
                :placeholder="$t('pages.categoryEditPage.optionsPlaceholderExample')"
                class="min-h-24 resize-none font-mono text-sm"
                @input="onOptionsTextInput"
              />
              <p class="text-xs text-muted-foreground">
                {{ $t("pages.categoryEditPage.optionsFormatHint") }}
              </p>
            </section>

            <section class="space-y-4">
              <h3 class="text-sm font-medium">{{ $t("pages.categoryEditPage.additionalSection") }}</h3>

              <SettingsFormField :label="$t('pages.categoryEditPage.defaultValueLabel')" label-for="attr-default">
                <div
                  v-if="attributeForm.type === 2"
                  class="flex items-center gap-2 h-9"
                >
                  <Switch
                    id="attr-default"
                    :model-value="attributeForm.defaultValue === 'true'"
                    @update:model-value="attributeForm.defaultValue = $event ? 'true' : 'false'"
                  />
                  <span class="text-sm text-muted-foreground">
                    {{ attributeForm.defaultValue === "true" ? $t("common.yes") : $t("common.no") }}
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
                    :placeholder="$t('pages.categoryEditPage.notSelectedPlaceholder')"
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
                  :placeholder="attributeForm.type === 4 ? $t('pages.categoryEditPage.multiValuePlaceholder') : $t('pages.categoryEditPage.optionalPlaceholder')"
                  :type="attributeForm.type === 1 ? 'number' : 'text'"
                />
                <template
                  v-if="attributeForm.type === 4 && editingOptionsCount"
                  #description
                >
                  {{ $t("pages.categoryEditPage.multiValueHint") }}
                </template>
              </SettingsFormField>

              <SettingsFormField :label="$t('common.description')" label-for="attr-description">
                <Textarea
                  id="attr-description"
                  v-model="attributeForm.description"
                  :placeholder="$t('pages.categoryEditPage.descriptionPlaceholder')"
                  class="min-h-20 resize-none"
                />
              </SettingsFormField>

              <SettingsFormField :label="$t('pages.categoryEditPage.descriptionInTooltipLabel')">
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="attr-description-tooltip"
                    :model-value="attributeForm.descriptionInTooltip"
                    @update:model-value="attributeForm.descriptionInTooltip = $event"
                  />
                  <Label for="attr-description-tooltip" class="font-normal text-muted-foreground">
                    {{ $t("pages.categoryEditPage.descriptionInTooltipHint") }}
                  </Label>
                </div>
              </SettingsFormField>

              <SettingsFormField :label="$t('pages.categoryEditPage.groupLabel')" label-for="attr-group">
                <Input
                  v-if="customGroupMode"
                  id="attr-group"
                  v-model="attributeForm.group"
                  :placeholder="$t('pages.categoryEditPage.groupNamePlaceholder')"
                />
                <SearchableSelect
                  v-else
                  id="attr-group"
                  :model-value="attributeForm.group"
                  @update:model-value="attributeForm.group = $event"
                  :options="groupSelectOptions"
                  :placeholder="$t('pages.categoryEditPage.noGroupPlaceholder')"
                  class="w-full"
                />
                <template #description>
                  <Button
                    type="button"
                    variant="link"
                    class="h-auto p-0 text-xs"
                    @click="customGroupMode = !customGroupMode; if (!customGroupMode) attributeForm.group = ''"
                  >
                    {{ customGroupMode ? $t("pages.categoryEditPage.chooseFromListLabel") : $t("pages.categoryEditPage.customGroupLabel") }}
                  </Button>
                </template>
              </SettingsFormField>

              <SettingsFormField
                v-if="isTextAttributeType"
                :label="$t('pages.categoryEditPage.multilineLabel')"
              >
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="attr-multiline"
                    :model-value="attributeForm.isMultiline"
                    @update:model-value="attributeForm.isMultiline = $event"
                  />
                  <Label for="attr-multiline" class="font-normal text-muted-foreground">
                    {{ $t("pages.categoryEditPage.multilineHint") }}
                  </Label>
                </div>
              </SettingsFormField>

              <SettingsFormField :label="$t('pages.categoryEditPage.showOnProductPageLabel')">
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="attr-visible"
                    :model-value="attributeForm.showOnProductPage"
                    @update:model-value="attributeForm.showOnProductPage = $event"
                  />
                  <Label for="attr-visible" class="font-normal text-muted-foreground">
                    {{ $t("pages.categoryEditPage.showOnProductPageHint") }}
                  </Label>
                </div>
              </SettingsFormField>

              <SettingsFormField :label="$t('pages.categoryEditPage.requiredFieldTitle')">
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="attr-required"
                    :model-value="attributeForm.isRequired"
                    @update:model-value="attributeForm.isRequired = $event"
                  />
                  <Label for="attr-required" class="font-normal text-muted-foreground">
                    {{ $t("pages.categoryEditPage.requiredFieldHint") }}
                  </Label>
                </div>
              </SettingsFormField>

              <SettingsFormField :label="$t('pages.categoryEditPage.uniqueFieldTitle')">
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="attr-unique"
                    :model-value="attributeForm.isUnique"
                    @update:model-value="attributeForm.isUnique = $event"
                  />
                  <Label for="attr-unique" class="font-normal text-muted-foreground">
                    {{ $t("pages.categoryEditPage.uniqueFieldHint") }}
                  </Label>
                </div>
              </SettingsFormField>
            </section>
          </FieldGroup>
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
