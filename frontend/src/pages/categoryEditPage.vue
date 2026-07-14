<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
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
  Loader2,
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
import { ATTRIBUTE_GROUP_OPTIONS } from "@/constants/attributeGroups";

// Threshold for large options lists (above this - show count only, not full list)
const LARGE_OPTIONS_THRESHOLD = 50;

// Lookup map for type labels (supports both numeric and string keys from JSON)
const ATTRIBUTE_TYPES = {
  0: { label: "Текст", value: 0 },
  1: { label: "Число", value: 1 },
  2: { label: "Да/Нет", value: 2 },
  3: { label: "Выбор", value: 3 },
  4: { label: "Множественный выбор", value: 4 },
  // String keys for JSON enum serialization (JsonStringEnumConverter)
  "Text": { label: "Текст", value: 0 },
  "Number": { label: "Число", value: 1 },
  "Boolean": { label: "Да/Нет", value: 2 },
  "Select": { label: "Выбор", value: 3 },
  "MultiSelect": { label: "Множественный выбор", value: 4 },
};

// Unique list of types for dropdown (no duplicates)
const ATTRIBUTE_TYPE_OPTIONS = [
  { label: "Текст", value: 0 },
  { label: "Число", value: 1 },
  { label: "Да/Нет", value: 2 },
  { label: "Выбор", value: 3 },
  { label: "Множественный выбор", value: 4 },
];

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
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
    Loader2,
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
    EmptyState,
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
        isMultiline: false,
        showOnProductPage: true,
        options: "",
        defaultValue: "",
        description: "",
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
      ATTRIBUTE_TYPE_OPTIONS,
      ATTRIBUTE_GROUP_OPTIONS,
      LARGE_OPTIONS_THRESHOLD,
    };
  },
  computed: {
    breadcrumbs() {
      const items = [{ label: "Категории", to: PAGE_CATEGORIES }];
      if (this.category?.name) {
        items.push({ label: this.category.name });
      } else {
        items.push({ label: "Редактирование" });
      }
      return items;
    },
    attributeTypeLabel() {
      return ATTRIBUTE_TYPES[this.attributeForm.type]?.label || "Текст";
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
     * Проверяет, содержит ли текущий атрибут большой список опций
     * (для таких списков показываем только count, не весь текст)
     */
    hasLargeOptions() {
      return this.cachedOptionsCount > LARGE_OPTIONS_THRESHOLD;
    },
    editingOptionsCount() {
      return this.cachedOptionsCount;
    },
    groupSelectOptions() {
      return [
        { value: "", label: "Без группы (Прочее)" },
        ...ATTRIBUTE_GROUP_OPTIONS.map((g) => ({ value: g, label: g })),
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
      if (mod100 >= 11 && mod100 <= 14) return "атрибутов";
      if (mod10 === 1) return "атрибут";
      if (mod10 >= 2 && mod10 <= 4) return "атрибута";
      return "атрибутов";
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
        displaySonnerError("Введите название категории");
        return;
      }

      this.isSaving = true;
      await this.updateCategory({
        id: this.categoryId,
        name: this.category.name.trim(),
        description: this.category.description?.trim() || null,
        hasSizes: this.category.hasSizes ?? false,
        onSuccess: () => {
          displaySonnerSuccess("Категория сохранена");
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
        isMultiline: false,
        showOnProductPage: true,
        options: "",
        defaultValue: "",
        description: "",
        group: "",
      };
      this.showAttributeDialog = true;
    },

    openEditAttributeDialog(attribute) {
      this.resetAttributeDialogOptionsState();
      this.editingAttribute = attribute;
      this.attributeOptionsJson = null;
      // Нормализуем тип (может прийти как строка "Select" или число 3)
      const normalizedType = ATTRIBUTE_TYPES[attribute.type]?.value ?? attribute.type;
      const group = attribute.group || "";
      // If the group is not in the predefined list, switch to custom mode
      this.customGroupMode = group !== "" && !ATTRIBUTE_GROUP_OPTIONS.includes(group);
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
        isMultiline: attribute.isMultiline ?? false,
        showOnProductPage: attribute.showOnProductPage !== false,
        options: this.cachedOptionsCount > LARGE_OPTIONS_THRESHOLD
          ? ""
          : this.formatOptionsForEdit(optionsRaw),
        defaultValue: attribute.defaultValue || "",
        description: attribute.description || "",
        group,
      };
      this.primeDefaultValueOptions();
      this.showAttributeDialog = true;
    },

    generateKey() {
      if (!this.attributeForm.name) return;
      // Convert name to snake_case key
      this.attributeForm.key = this.attributeForm.name
        .toLowerCase()
        .replace(/[^a-zа-яё0-9\s]/gi, "")
        .replace(/\s+/g, "_")
        .replace(/[а-яё]/gi, (char) => {
          const translitMap = {
            а: "a", б: "b", в: "v", г: "g", д: "d", е: "e", ё: "yo",
            ж: "zh", з: "z", и: "i", й: "y", к: "k", л: "l", м: "m",
            н: "n", о: "o", п: "p", р: "r", с: "s", т: "t", у: "u",
            ф: "f", х: "h", ц: "ts", ч: "ch", ш: "sh", щ: "sch",
            ъ: "", ы: "y", ь: "", э: "e", ю: "yu", я: "ya",
          };
          return translitMap[char.toLowerCase()] || char;
        });
    },

    async handleSaveAttribute() {
      if (!this.attributeForm.name.trim()) {
        displaySonnerError("Введите название атрибута");
        return;
      }
      if (!this.attributeForm.key.trim()) {
        displaySonnerError("Введите ключ атрибута");
        return;
      }

      this.isAddingAttribute = true;

      const attributeData = {
        name: this.attributeForm.name.trim(),
        key: this.attributeForm.key.trim(),
        type: this.attributeForm.type,
        isRequired: this.attributeForm.isRequired,
        isMultiline: this.isTextAttributeType ? this.attributeForm.isMultiline : false,
        showOnProductPage: this.attributeForm.showOnProductPage,
        options: this.showOptionsField ? this.resolveOptionsForSave() : null,
        defaultValue: this.attributeForm.defaultValue.trim() || null,
        description: this.attributeForm.description.trim() || null,
        group: this.attributeForm.group.trim() || null,
      };

      if (this.editingAttribute) {
        await this.updateAttribute({
          categoryId: this.categoryId,
          attributeId: this.editingAttribute.id,
          attribute: attributeData,
          onSuccess: () => {
            displaySonnerSuccess("Атрибут обновлён");
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
            displaySonnerSuccess("Атрибут добавлен");
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
          displaySonnerSuccess("Атрибут удалён");
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
          displaySonnerSuccess("Порядок атрибутов сохранён");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isReordering = false;
    },

    getAttributeTypeLabel(type) {
      return ATTRIBUTE_TYPES[type]?.label || "Неизвестный тип";
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
        displaySonnerError("Файл не содержит вариантов");
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
      displaySonnerSuccess(`Импортировано вариантов: ${parsed.length.toLocaleString()}`);
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
        displaySonnerError("Нет вариантов для экспорта");
        return;
      }

      const parsed = this.parseOptions(raw);
      if (!parsed.length) {
        displaySonnerError("Нет вариантов для экспорта");
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
      displaySonnerSuccess(`Экспортировано вариантов: ${parsed.length.toLocaleString()}`);
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
            displaySonnerError("Не удалось прочитать файл");
            return;
          }
          const parsed = this.parseImportedOptionsText(text);
          this.applyImportedOptions(parsed);
        } catch {
          displaySonnerError("Ошибка чтения файла");
        }
      };
      reader.onerror = () => {
        displaySonnerError("Ошибка чтения файла");
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
     * Парсит опции из строки (поддерживает JSON и comma-separated для обратной совместимости)
     * @returns {Array<{value: string, label: string}>}
     */
    parseOptions(optionsStr) {
      if (!optionsStr) return [];

      // Пробуем распарсить как JSON
      try {
        const parsed = JSON.parse(optionsStr);
        if (Array.isArray(parsed)) {
          return parsed.map(o => ({
            value: o.value || o.label || o,
            label: o.label || o.value || o
          }));
        }
      } catch {
        // Не JSON - парсим как comma-separated
      }

      // Fallback: comma-separated строка (поддержка формата "value:label")
      return optionsStr.split(',').map(o => o.trim()).filter(Boolean).map(item => {
        if (item.includes(':')) {
          const [value, ...labelParts] = item.split(':');
          return { value: value.trim(), label: labelParts.join(':').trim() };
        }
        return { value: item, label: item };
      });
    },

    /**
     * Форматирует опции для отображения в textarea (JSON -> comma-separated labels)
     * Для больших списков (>LARGE_OPTIONS_THRESHOLD) возвращает пустую строку,
     * т.к. они отображаются в read-only режиме
     */
    formatOptionsForEdit(optionsStr) {
      if (!optionsStr) return "";

      // Пробуем распарсить как JSON
      try {
        const parsed = JSON.parse(optionsStr);
        if (Array.isArray(parsed)) {
          // Для больших списков не форматируем - они будут показаны как count
          if (parsed.length > LARGE_OPTIONS_THRESHOLD) {
            return ""; // Пустая строка - UI покажет информационный блок
          }
          // Формат: "value:label" если они разные, иначе просто label
          return parsed.map(o => {
            const value = o.value || o.label || o;
            const label = o.label || o.value || o;
            if (typeof o === 'string') return o;
            return value !== label ? `${value}:${label}` : label;
          }).join(', ');
        }
      } catch {
        // Уже в нужном формате
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
     * Количество опций
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
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageSection :loading="isLoading" class="!min-h-0">
      <div v-if="category" class="space-y-3">
        <div class="flex items-start justify-between gap-4">
          <div class="min-w-0 flex-1 space-y-1">
            <h1 class="text-lg font-semibold">
              {{ category.name || "Редактирование категории" }}
            </h1>
            <p class="text-sm text-muted-foreground">
              Название, описание и настройки размерной сетки для товаров категории.
            </p>
          </div>
          <Button
            class="shrink-0"
            @click="handleSaveCategory"
            :disabled="isSaving"
          >
            <Loader2 v-if="isSaving" class="size-4 mr-2 animate-spin" />
            <Save v-else class="size-4 mr-2" />
            Сохранить
          </Button>
        </div>

        <!-- Block 1: Settings -->
        <div class="max-w-2xl space-y-4">
            <SettingsFormField label="Название" label-for="category-name">
              <Input
                id="category-name"
                v-model="category.name"
                placeholder="Введите название"
              />
            </SettingsFormField>
            <SettingsFormField label="Описание" label-for="category-description">
              <Textarea
                id="category-description"
                v-model="category.description"
                placeholder="Необязательно"
                class="min-h-24 resize-none"
              />
            </SettingsFormField>
            <SettingsFormField label="Размерная сетка">
              <div class="flex items-center gap-2 h-9">
                <Switch
                  id="category-has-sizes"
                  :model-value="category.hasSizes"
                  @update:model-value="category.hasSizes = $event"
                />
                <Label for="category-has-sizes" class="font-normal text-muted-foreground">
                  Товары имеют размеры
                </Label>
              </div>
            </SettingsFormField>
        </div>

        <!-- Block 2: Attributes -->
        <Card class="overflow-hidden border-0 shadow-none !gap-3 !py-0">
          <CardHeader class="!px-0">
            <CardTitle class="text-base">
              Атрибуты категории
            </CardTitle>
            <CardDescription>
              {{ attributes.length }} {{ attributesCountLabel }}.
              Порядок строк определяет порядок полей на странице товара —
              перетащите строки, чтобы изменить порядок.
            </CardDescription>
            <CardAction>
              <Button
                variant="outline"
                size="sm"
                @click="openAddAttributeDialog"
              >
                <Plus class="size-4 mr-2" />
                Добавить
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent class="!px-0 pb-6 pt-0">
            <EmptyState
              v-if="attributes.length === 0"
              title="Нет атрибутов"
              description="Атрибуты определяют свойства товаров — размер, цвет, материал и другие характеристики"
              class="py-10"
            >
              <template #icon>
                <Settings2 />
              </template>
              <Button variant="outline" @click="openAddAttributeDialog">
                <Plus class="size-4 mr-2" />
                Добавить атрибут
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
                      <TableHead class="min-w-[11rem] px-3 sm:min-w-[16rem]">Название</TableHead>
                      <TableHead class="hidden sm:table-cell px-3">Ключ</TableHead>
                      <TableHead class="hidden md:table-cell px-3">Группа</TableHead>
                      <TableHead class="px-3">Тип</TableHead>
                      <TableHead class="hidden lg:table-cell w-28 text-right px-3">Опции</TableHead>
                      <TableHead class="hidden xl:table-cell px-3">Описание</TableHead>
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
                              title="Обязательное поле"
                            >*</span>
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
                          <span v-else class="text-muted-foreground">—</span>
                        </TableCell>
                        <TableCell class="px-3 text-muted-foreground">
                          {{ getAttributeTypeLabel(attribute.type) }}
                        </TableCell>
                        <TableCell class="hidden lg:table-cell px-3 text-right tabular-nums text-muted-foreground">
                          {{ attribute.options ? getOptionsCount(attribute.options) : "—" }}
                        </TableCell>
                        <TableCell class="hidden xl:table-cell px-3 text-muted-foreground max-w-xs truncate">
                          {{ attribute.description || "—" }}
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
                                <Button variant="ghost" size="icon" class="size-8">
                                  <Trash2 class="size-4 text-destructive" />
                                </Button>
                              </AlertDialogTrigger>
                              <AlertDialogContent>
                                <AlertDialogHeader>
                                  <AlertDialogTitle>Удалить атрибут?</AlertDialogTitle>
                                  <AlertDialogDescription>
                                    Вы уверены что хотите удалить атрибут "{{ attribute.name }}"?
                                    Все значения этого атрибута у товаров будут удалены.
                                  </AlertDialogDescription>
                                </AlertDialogHeader>
                                <AlertDialogFooter>
                                  <AlertDialogCancel>Отмена</AlertDialogCancel>
                                  <AlertDialogAction
                                    @click="handleDeleteAttribute(attribute.id)"
                                    :disabled="isDeletingAttribute"
                                  >
                                    <Loader2 v-if="isDeletingAttribute" class="size-4 mr-2 animate-spin" />
                                    Удалить
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
        <DialogHeader class="shrink-0 border-b px-6 pt-6 pb-4">
          <DialogTitle>
            {{ editingAttribute ? "Редактировать атрибут" : "Новый атрибут" }}
          </DialogTitle>
          <DialogDescription>
            Параметры поля на странице товара в этой категории
          </DialogDescription>
        </DialogHeader>

        <div class="flex-1 overflow-y-auto px-6 py-5">
          <FieldGroup class="gap-6">
            <section class="space-y-4">
              <h3 class="text-sm font-medium">Основное</h3>
              <div class="grid gap-4 sm:grid-cols-2">
                <SettingsFormField label="Название" label-for="attr-name">
                  <Input
                    id="attr-name"
                    v-model="attributeForm.name"
                    placeholder="Цвет, Размер, Бренд"
                    @input="!editingAttribute && generateKey()"
                  />
                </SettingsFormField>
                <SettingsFormField label="Ключ" label-for="attr-key">
                  <template #description>Используется в API и экспорте</template>
                  <Input
                    id="attr-key"
                    v-model="attributeForm.key"
                    placeholder="color, size, brand"
                    class="font-mono text-sm"
                  />
                </SettingsFormField>
              </div>
              <SettingsFormField label="Тип значения" label-for="attr-type">
                <Select v-model="attributeForm.type">
                  <SelectTrigger id="attr-type" class="w-full">
                    <SelectValue :placeholder="attributeTypeLabel" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem
                      v-for="typeInfo in ATTRIBUTE_TYPE_OPTIONS"
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
                <h3 class="text-sm font-medium">Варианты</h3>
                <div class="flex items-center gap-2">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    :disabled="!editingOptionsCount"
                    @click="exportAttributeOptions"
                  >
                    <Download class="size-3.5 mr-1.5" />
                    Экспорт
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    @click="triggerOptionsImport"
                  >
                    <Upload class="size-3.5 mr-1.5" />
                    Импорт
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
                    <span class="text-muted-foreground"> вариантов</span>
                  </p>
                  <p class="text-xs text-muted-foreground mt-0.5">
                    Список хранится в JSON — редактируйте через импорт и экспорт
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
                  Очистить
                </Button>
              </div>
              <Textarea
                v-else
                v-model="attributeForm.options"
                placeholder="Красный, Синий, Зелёный — или value:подпись"
                class="min-h-24 resize-none font-mono text-sm"
                @input="onOptionsTextInput"
              />
              <p class="text-xs text-muted-foreground">
                JSON, CSV или текст — по одному варианту на строку или через запятую
              </p>
            </section>

            <section class="space-y-4">
              <h3 class="text-sm font-medium">Дополнительно</h3>

              <SettingsFormField label="Значение по умолчанию" label-for="attr-default">
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
                    {{ attributeForm.defaultValue === "true" ? "Да" : "Нет" }}
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
                    placeholder="Не выбрано"
                    class="w-full"
                    @open="loadDefaultValueOptionsIfNeeded"
                  />
                  <p
                    v-if="hasLargeOptions && !defaultValueOptionsReady"
                    class="text-xs text-muted-foreground"
                  >
                    Список загрузится при открытии выбора
                  </p>
                </div>
                <Input
                  v-else
                  id="attr-default"
                  v-model="attributeForm.defaultValue"
                  :placeholder="attributeForm.type === 4 ? 'значение1, значение2' : 'Необязательно'"
                  :type="attributeForm.type === 1 ? 'number' : 'text'"
                />
                <template
                  v-if="attributeForm.type === 4 && editingOptionsCount"
                  #description
                >
                  Несколько значений через запятую
                </template>
              </SettingsFormField>

              <SettingsFormField label="Описание" label-for="attr-description">
                <Textarea
                  id="attr-description"
                  v-model="attributeForm.description"
                  placeholder="Подсказка для пользователя на странице товара"
                  class="min-h-20 resize-none"
                />
              </SettingsFormField>

              <SettingsFormField label="Группа" label-for="attr-group">
                <Input
                  v-if="customGroupMode"
                  id="attr-group"
                  v-model="attributeForm.group"
                  placeholder="Название группы"
                />
                <SearchableSelect
                  v-else
                  id="attr-group"
                  :model-value="attributeForm.group"
                  @update:model-value="attributeForm.group = $event"
                  :options="groupSelectOptions"
                  placeholder="Без группы"
                  class="w-full"
                />
                <template #description>
                  <Button
                    type="button"
                    variant="link"
                    class="h-auto p-0 text-xs"
                    @click="customGroupMode = !customGroupMode; if (!customGroupMode) attributeForm.group = ''"
                  >
                    {{ customGroupMode ? "Выбрать из списка" : "Своя группа" }}
                  </Button>
                </template>
              </SettingsFormField>

              <SettingsFormField
                v-if="isTextAttributeType"
                label="Многострочный текст"
              >
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="attr-multiline"
                    :model-value="attributeForm.isMultiline"
                    @update:model-value="attributeForm.isMultiline = $event"
                  />
                  <Label for="attr-multiline" class="font-normal text-muted-foreground">
                    Поле с переносами строк на странице товара
                  </Label>
                </div>
              </SettingsFormField>

              <SettingsFormField label="На странице товара">
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="attr-visible"
                    :model-value="attributeForm.showOnProductPage"
                    @update:model-value="attributeForm.showOnProductPage = $event"
                  />
                  <Label for="attr-visible" class="font-normal text-muted-foreground">
                    Показывать при редактировании товара
                  </Label>
                </div>
              </SettingsFormField>

              <SettingsFormField label="Обязательное поле">
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="attr-required"
                    :model-value="attributeForm.isRequired"
                    @update:model-value="attributeForm.isRequired = $event"
                  />
                  <Label for="attr-required" class="font-normal text-muted-foreground">
                    Требовать заполнение при создании товара
                  </Label>
                </div>
              </SettingsFormField>
            </section>
          </FieldGroup>
        </div>

        <DialogFooter class="shrink-0 border-t px-6 py-4">
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button @click="handleSaveAttribute" :disabled="isAddingAttribute">
            <Loader2 v-if="isAddingAttribute" class="size-4 mr-2 animate-spin" />
            {{ editingAttribute ? "Сохранить" : "Добавить" }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </Page>
</template>
