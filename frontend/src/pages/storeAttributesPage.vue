<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { PageHeader } from "@/components/ui/page-header";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Label from "@/components/ui/label/Label.vue";
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
  TableHead,
  TableHeader,
  TableRow,
  TableCell,
} from "@/components/ui/table";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Loader2,
  Plus,
  Trash2,
  GripVertical,
  Settings2,
  Pencil,
  Download,
  Upload,
} from "lucide-vue-next";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { PAGE_PRODUCTS } from "@/router";
import draggable from "vuedraggable";

const LARGE_OPTIONS_THRESHOLD = 50;

const ATTRIBUTE_TYPES = {
  0: { label: "Текст", value: 0 },
  1: { label: "Число", value: 1 },
  2: { label: "Да/Нет", value: 2 },
  3: { label: "Выбор", value: 3 },
  4: { label: "Множественный выбор", value: 4 },
  5: { label: "Штрихкод (Barcode)", value: 5 },
  // String keys for JSON enum serialization (JsonStringEnumConverter)
  "Text": { label: "Текст", value: 0 },
  "Number": { label: "Число", value: 1 },
  "Boolean": { label: "Да/Нет", value: 2 },
  "Select": { label: "Выбор", value: 3 },
  "MultiSelect": { label: "Множественный выбор", value: 4 },
  "Barcode": { label: "Штрихкод (Barcode)", value: 5 },
};

const ATTRIBUTE_TYPE_OPTIONS = [
  { label: "Текст", value: 0 },
  { label: "Число", value: 1 },
  { label: "Да/Нет", value: 2 },
  { label: "Выбор", value: 3 },
  { label: "Множественный выбор", value: 4 },
  { label: "Штрихкод (Barcode)", value: 5 },
];

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    PageHeader,
    Button,
    Input,
    Textarea,
    Switch,
    Label,
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
    TableHead,
    TableHeader,
    TableRow,
    TableCell,
    Loader2,
    Plus,
    Trash2,
    GripVertical,
    Settings2,
    Pencil,
    Download,
    Upload,
    SearchableSelect,
    draggable,
  },
  data() {
    return {
      attributes: [],
      isLoading: false,
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
      },
      attributeOptionsJson: null,
      cachedOptionsCount: 0,
      defaultValueOptions: [],
      defaultValueOptionsReady: false,
      isLoadingDefaultValueOptions: false,
      PAGE_PRODUCTS,
      ATTRIBUTE_TYPES,
      ATTRIBUTE_TYPE_OPTIONS,
      LARGE_OPTIONS_THRESHOLD,
      breadcrumbs: [
        { label: "Товары", to: PAGE_PRODUCTS },
        { label: "Глобальные атрибуты" },
      ],
    };
  },
  computed: {
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
    hasLargeOptions() {
      return this.cachedOptionsCount > LARGE_OPTIONS_THRESHOLD;
    },
    editingOptionsCount() {
      return this.cachedOptionsCount;
    },
    defaultValueSelectOptions() {
      if (!this.defaultValueOptionsReady) return [];
      return this.defaultValueOptions;
    },
  },
  methods: {
    ...mapActions([
      "getAllStoreAttributes",
      "createStoreAttribute",
      "updateStoreAttribute",
      "deleteStoreAttribute",
      "reorderStoreAttributes",
    ]),

    async fetchAttributes() {
      this.isLoading = true;
      await this.getAllStoreAttributes({
        onSuccess: (response) => {
          this.attributes = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
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
      };
      this.showAttributeDialog = true;
    },

    openEditAttributeDialog(attribute) {
      this.resetAttributeDialogOptionsState();
      this.editingAttribute = attribute;
      const normalizedType = ATTRIBUTE_TYPES[attribute.type]?.value ?? attribute.type;
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
      };

      if (this.editingAttribute) {
        await this.updateStoreAttribute({
          id: this.editingAttribute.id,
          attribute: attributeData,
          onSuccess: () => {
            displaySonnerSuccess("Атрибут обновлён");
            this.showAttributeDialog = false;
            this.fetchAttributes();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } else {
        await this.createStoreAttribute({
          attribute: attributeData,
          onSuccess: () => {
            displaySonnerSuccess("Атрибут добавлен");
            this.showAttributeDialog = false;
            this.fetchAttributes();
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
      await this.deleteStoreAttribute({
        id: attributeId,
        onSuccess: (response) => {
          const deletedCount = response.data?.value?.deletedValuesCount || 0;
          if (deletedCount > 0) {
            displaySonnerSuccess(`Атрибут удалён (${deletedCount} значений удалено)`);
          } else {
            displaySonnerSuccess("Атрибут удалён");
          }
          this.fetchAttributes();
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
      await this.reorderStoreAttributes({
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

    parseOptions(optionsStr) {
      if (!optionsStr) return [];

      try {
        const parsed = JSON.parse(optionsStr);
        if (Array.isArray(parsed)) {
          return parsed.map((o) => ({
            value: o.value || o.label || o,
            label: o.label || o.value || o,
          }));
        }
      } catch {
        // not JSON
      }

      return optionsStr.split(",").map((o) => o.trim()).filter(Boolean).map((item) => {
        if (item.includes(":")) {
          const [value, ...labelParts] = item.split(":");
          return { value: value.trim(), label: labelParts.join(":").trim() };
        }
        return { value: item, label: item };
      });
    },

    formatOptionsForEdit(optionsStr) {
      if (!optionsStr) return "";

      try {
        const parsed = JSON.parse(optionsStr);
        if (Array.isArray(parsed)) {
          if (parsed.length > LARGE_OPTIONS_THRESHOLD) {
            return "";
          }
          return parsed.map((o) => {
            const value = o.value || o.label || o;
            const label = o.label || o.value || o;
            if (typeof o === "string") return o;
            return value !== label ? `${value}:${label}` : label;
          }).join(", ");
        }
      } catch {
        // already formatted
      }

      return optionsStr;
    },

    serializeOptionsForSave() {
      const raw = this.attributeForm.options.trim();
      if (!raw) return null;
      const parsed = this.parseOptions(raw);
      if (parsed.length === 0) return null;
      return JSON.stringify(parsed);
    },

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
    this.fetchAttributes();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      title="Глобальные атрибуты"
      description="Атрибуты, применяемые ко всем товарам магазина независимо от категории"
    >
      <Button @click="openAddAttributeDialog">
        <Plus class="size-4 mr-2" />
        Добавить атрибут
      </Button>
    </PageHeader>

    <PageSection
      :loading="isLoading"
      :empty="attributes.length === 0"
      :empty-icon="Settings2"
      empty-title="Нет глобальных атрибутов"
      empty-description="Глобальные атрибуты доступны во всех категориях — удобно для общих свойств вроде бренда или страны"
      class="flex flex-col"
    >
      <template #empty-action>
          <Button @click="openAddAttributeDialog">
            <Plus class="size-4 mr-2" />
            Добавить атрибут
          </Button>
      </template>

        <div class="rounded-lg border overflow-hidden">
          <div class="overflow-x-auto">
            <table class="w-full caption-bottom text-sm">
              <TableHeader>
                <TableRow class="hover:bg-transparent">
                  <TableHead class="w-9 px-3" />
                  <TableHead class="px-3">Название</TableHead>
                  <TableHead class="hidden sm:table-cell px-3">Ключ</TableHead>
                  <TableHead class="px-3">Тип</TableHead>
                  <TableHead class="hidden md:table-cell w-28 text-right px-3">Опции</TableHead>
                  <TableHead class="w-16 text-center px-2">Обяз.</TableHead>
                  <TableHead class="hidden lg:table-cell px-3">Описание</TableHead>
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
                    <TableCell class="px-3 font-medium max-w-0">
                      <span class="truncate block">{{ attribute.name }}</span>
                      <span class="text-xs text-muted-foreground font-mono truncate block sm:hidden">
                        {{ attribute.key }}
                      </span>
                    </TableCell>
                    <TableCell class="hidden sm:table-cell px-3 font-mono text-xs text-muted-foreground">
                      {{ attribute.key }}
                    </TableCell>
                    <TableCell class="px-3 text-muted-foreground">
                      {{ getAttributeTypeLabel(attribute.type) }}
                    </TableCell>
                    <TableCell class="hidden md:table-cell px-3 text-right tabular-nums text-muted-foreground">
                      {{ attribute.options ? getOptionsCount(attribute.options) : "—" }}
                    </TableCell>
                    <TableCell class="px-2 text-center text-muted-foreground">
                      <span v-if="attribute.isRequired" class="text-destructive">*</span>
                      <span v-else>—</span>
                    </TableCell>
                    <TableCell class="hidden lg:table-cell px-3 text-muted-foreground max-w-xs truncate">
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
    </PageSection>

    <!-- Attribute Dialog -->
    <Dialog v-model:open="showAttributeDialog">
      <DialogContent
        size="2xl"
        class="max-h-[min(90vh,48rem)] flex flex-col gap-0 p-0 overflow-hidden"
      >
        <DialogHeader class="shrink-0 border-b px-6 pt-6 pb-4">
          <DialogTitle>
            {{ editingAttribute ? "Редактировать атрибут" : "Новый глобальный атрибут" }}
          </DialogTitle>
          <DialogDescription>
            Параметры поля для всех товаров магазина
          </DialogDescription>
        </DialogHeader>

        <div class="flex-1 overflow-y-auto px-6 py-5">
          <FieldGroup class="gap-6">
            <section class="space-y-4">
              <h3 class="text-sm font-medium">Основное</h3>
              <div class="grid gap-4 sm:grid-cols-2">
                <SettingsFormField label="Название" label-for="store-attr-name">
                  <Input
                    id="store-attr-name"
                    v-model="attributeForm.name"
                    placeholder="Бренд, Страна, Артикул"
                    @input="!editingAttribute && generateKey()"
                  />
                </SettingsFormField>
                <SettingsFormField label="Ключ" label-for="store-attr-key">
                  <template #description>Используется в API и экспорте</template>
                  <Input
                    id="store-attr-key"
                    v-model="attributeForm.key"
                    placeholder="brand, country, sku"
                    class="font-mono text-sm"
                  />
                </SettingsFormField>
              </div>
              <SettingsFormField label="Тип значения" label-for="store-attr-type">
                <Select v-model="attributeForm.type">
                  <SelectTrigger id="store-attr-type" class="w-full">
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

              <SettingsFormField label="Значение по умолчанию" label-for="store-attr-default">
                <div
                  v-if="attributeForm.type === 2"
                  class="flex items-center gap-2 h-9"
                >
                  <Switch
                    id="store-attr-default"
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
                  id="store-attr-default"
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

              <SettingsFormField label="Описание" label-for="store-attr-description">
                <Textarea
                  id="store-attr-description"
                  v-model="attributeForm.description"
                  placeholder="Подсказка для пользователя на странице товара"
                  class="min-h-20 resize-none"
                />
              </SettingsFormField>

              <SettingsFormField
                v-if="isTextAttributeType"
                label="Многострочный текст"
              >
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="store-attr-multiline"
                    :model-value="attributeForm.isMultiline"
                    @update:model-value="attributeForm.isMultiline = $event"
                  />
                  <Label for="store-attr-multiline" class="font-normal text-muted-foreground">
                    Поле с переносами строк на странице товара
                  </Label>
                </div>
              </SettingsFormField>

              <SettingsFormField label="На странице товара">
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="store-attr-visible"
                    :model-value="attributeForm.showOnProductPage"
                    @update:model-value="attributeForm.showOnProductPage = $event"
                  />
                  <Label for="store-attr-visible" class="font-normal text-muted-foreground">
                    Показывать при редактировании товара
                  </Label>
                </div>
              </SettingsFormField>

              <SettingsFormField label="Обязательное поле">
                <div class="flex items-center gap-2 h-9">
                  <Switch
                    id="store-attr-required"
                    :model-value="attributeForm.isRequired"
                    @update:model-value="attributeForm.isRequired = $event"
                  />
                  <Label for="store-attr-required" class="font-normal text-muted-foreground">
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
