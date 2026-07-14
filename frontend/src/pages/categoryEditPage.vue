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
import { PageHeader } from "@/components/ui/page-header";
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
    PageHeader,
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
        options: "",
        defaultValue: "",
        description: "",
        group: "",
      },
      customGroupMode: false,
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
    /**
     * Проверяет, содержит ли текущий атрибут большой список опций
     * (для таких списков показываем только count, не весь текст)
     */
    hasLargeOptions() {
      if (!this.editingAttribute?.options) return false;
      return this.getOptionsCount(this.editingAttribute.options) > LARGE_OPTIONS_THRESHOLD;
    },
    /**
     * Количество опций в редактируемом атрибуте
     */
    editingOptionsCount() {
      if (!this.editingAttribute?.options) return 0;
      return this.getOptionsCount(this.editingAttribute.options);
    },
    groupSelectOptions() {
      return [
        { value: "", label: "Без группы (Прочее)" },
        ...ATTRIBUTE_GROUP_OPTIONS.map((g) => ({ value: g, label: g })),
      ];
    },
    parsedFormOptions() {
      return this.parseOptions(this.attributeForm.options);
    },
    pageHeaderTitle() {
      return this.category?.name || "Редактирование категории";
    },
    pageHeaderDescription() {
      return "Основные данные и атрибуты товаров в этой категории";
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

    openAddAttributeDialog() {
      this.editingAttribute = null;
      this.customGroupMode = false;
      this.attributeForm = {
        name: "",
        key: "",
        type: 0,
        isRequired: false,
        options: "",
        defaultValue: "",
        description: "",
        group: "",
      };
      this.showAttributeDialog = true;
    },

    openEditAttributeDialog(attribute) {
      this.editingAttribute = attribute;
      // Нормализуем тип (может прийти как строка "Select" или число 3)
      const normalizedType = ATTRIBUTE_TYPES[attribute.type]?.value ?? attribute.type;
      const group = attribute.group || "";
      // If the group is not in the predefined list, switch to custom mode
      this.customGroupMode = group !== "" && !ATTRIBUTE_GROUP_OPTIONS.includes(group);
      this.attributeForm = {
        name: attribute.name,
        key: attribute.key,
        type: normalizedType,
        isRequired: attribute.isRequired,
        options: this.formatOptionsForEdit(attribute.options),
        defaultValue: attribute.defaultValue || "",
        description: attribute.description || "",
        group,
      };
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
        options: this.showOptionsField
          ? (this.hasLargeOptions && !this.attributeForm.options.trim()
              ? this.editingAttribute.options
              : this.serializeOptionsForSave())
          : null,
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

    <PageHeader
      :title="pageHeaderTitle"
      :description="pageHeaderDescription"
    >
      <Button
        v-if="category"
        @click="handleSaveCategory"
        :disabled="isSaving"
      >
        <Loader2 v-if="isSaving" class="size-4 mr-2 animate-spin" />
        <Save v-else class="size-4 mr-2" />
        Сохранить
      </Button>
    </PageHeader>

    <PageSection :loading="isLoading" class="!min-h-0">
      <div v-if="category" class="space-y-6">
        <!-- Block 1: Settings -->
        <Card class="max-w-2xl border-0 shadow-none">
          <CardHeader>
            <CardTitle class="text-base">
              Основные данные
            </CardTitle>
            <CardDescription>
              Название, описание и настройки размерной сетки для товаров категории.
            </CardDescription>
          </CardHeader>
          <CardContent class="space-y-5">
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
          </CardContent>
        </Card>

        <!-- Block 2: Attributes -->
        <Card class="overflow-hidden border-0 shadow-none">
          <CardHeader>
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
          <CardContent class="px-6 pb-6 pt-0">
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
                      <TableHead class="px-3">Название</TableHead>
                      <TableHead class="hidden sm:table-cell px-3">Ключ</TableHead>
                      <TableHead class="hidden md:table-cell px-3">Группа</TableHead>
                      <TableHead class="px-3">Тип</TableHead>
                      <TableHead class="hidden lg:table-cell w-28 text-right px-3">Опции</TableHead>
                      <TableHead class="w-16 text-center px-2">Обяз.</TableHead>
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
                        <TableCell class="px-3 font-medium max-w-0">
                          <span class="truncate block">{{ attribute.name }}</span>
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
                        <TableCell class="px-2 text-center text-muted-foreground">
                          <span v-if="attribute.isRequired" class="text-destructive">*</span>
                          <span v-else>—</span>
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
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {{ editingAttribute ? 'Редактировать атрибут' : 'Новый атрибут' }}
          </DialogTitle>
          <DialogDescription>
            Настройте параметры атрибута для товаров категории
          </DialogDescription>
        </DialogHeader>

        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label>Название</Label>
            <Input
              v-model="attributeForm.name"
              placeholder="Например: Цвет, Размер, Материал"
              @input="!editingAttribute && generateKey()"
            />
          </div>

          <div class="space-y-2">
            <Label>Ключ (для API)</Label>
            <Input
              v-model="attributeForm.key"
              placeholder="color, size, material"
            />
          </div>

          <div class="space-y-2">
            <Label>Тип значения</Label>
            <Select v-model="attributeForm.type">
              <SelectTrigger>
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
          </div>

          <div v-if="showOptionsField" class="space-y-2">
            <Label>Варианты</Label>
            <!-- Большой список опций - информация + кнопка очистки -->
            <div v-if="hasLargeOptions" class="rounded-md border bg-muted/50 p-3">
              <div class="flex items-center justify-between">
                <div>
                  <div class="flex items-center gap-2 text-sm">
                    <span class="font-medium">{{ editingOptionsCount.toLocaleString() }}</span>
                    <span class="text-muted-foreground">вариантов (импортировано с адаптера)</span>
                  </div>
                  <p class="text-xs text-muted-foreground mt-1">
                    Редактирование большого списка недоступно вручную.
                  </p>
                </div>
                <Button
                  variant="destructive"
                  size="sm"
                  @click="attributeForm.options = ''; editingAttribute = { ...editingAttribute, options: null }"
                >
                  <Trash2 class="size-3.5 mr-1.5" />
                  Очистить
                </Button>
              </div>
            </div>
            <!-- Небольшой список - редактируемое поле -->
            <Textarea
              v-else
              v-model="attributeForm.options"
              placeholder="Красный, Синий, Зелёный"
              class="h-20"
            />
          </div>

          <!-- Default value based on type -->
          <div class="space-y-2">
            <Label>Значение по умолчанию</Label>
            <!-- Boolean type (2) - Switch -->
            <div v-if="attributeForm.type === 2" class="flex items-center gap-2 h-9">
              <Switch
                :model-value="attributeForm.defaultValue === 'true'"
                @update:model-value="attributeForm.defaultValue = $event ? 'true' : 'false'"
              />
              <span class="text-sm text-muted-foreground">
                {{ attributeForm.defaultValue === 'true' ? 'Да' : 'Нет' }}
              </span>
            </div>
            <!-- Select type (3) - Dropdown -->
            <Select
              v-else-if="attributeForm.type === 3 && attributeForm.options"
              :model-value="attributeForm.defaultValue || '__none__'"
              @update:model-value="attributeForm.defaultValue = $event === '__none__' ? '' : $event"
            >
              <SelectTrigger>
                <SelectValue placeholder="Выберите значение..." />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__none__">Не выбрано</SelectItem>
                <SelectItem
                  v-for="option in parsedFormOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
            <!-- Other types - Input -->
            <Input
              v-else
              v-model="attributeForm.defaultValue"
              placeholder="Необязательно"
              :type="attributeForm.type === 1 ? 'number' : 'text'"
            />
          </div>

          <div class="space-y-2">
            <Label>Описание</Label>
            <Textarea
              v-model="attributeForm.description"
              placeholder="Описание атрибута (необязательно)"
              class="h-20"
            />
          </div>

          <div class="space-y-2">
            <Label>Группа атрибута</Label>
            <!-- Custom group mode: free text input -->
            <Input
              v-if="customGroupMode"
              v-model="attributeForm.group"
              placeholder="Название группы"
            />
            <!-- Predefined group mode: searchable dropdown -->
            <SearchableSelect
              v-else
              :model-value="attributeForm.group"
              @update:model-value="attributeForm.group = $event"
              :options="groupSelectOptions"
              placeholder="Выберите группу..."
            />
            <p class="text-xs text-muted-foreground">
              Группировка атрибутов для удобного отображения на странице товара.
              <Button
                type="button"
                variant="link"
                class="h-auto p-0"
                @click="customGroupMode = !customGroupMode; if (!customGroupMode) attributeForm.group = ''"
              >
                {{ customGroupMode ? 'Выбрать из списка' : 'Своя группа' }}
              </Button>
            </p>
          </div>

          <div class="flex items-center gap-2">
            <Switch
              :model-value="attributeForm.isRequired"
              @update:model-value="attributeForm.isRequired = $event"
            />
            <Label>Обязательное поле</Label>
          </div>
        </div>

        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button @click="handleSaveAttribute" :disabled="isAddingAttribute">
            <Loader2 v-if="isAddingAttribute" class="size-4 mr-2 animate-spin" />
            {{ editingAttribute ? 'Сохранить' : 'Добавить' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </Page>
</template>
