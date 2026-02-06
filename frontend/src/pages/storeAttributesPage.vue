<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Label from "@/components/ui/label/Label.vue";
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
  CardContent,
} from "@/components/ui/card";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Loader2,
  Plus,
  Trash2,
  GripVertical,
  Settings2,
  Pencil,
  Globe,
} from "lucide-vue-next";
import { PAGE_PRODUCTS } from "@/router";
import draggable from "vuedraggable";

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
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Textarea,
    Switch,
    Label,
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
    CardContent,
    Loader2,
    Plus,
    Trash2,
    GripVertical,
    Settings2,
    Pencil,
    Globe,
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
        options: "",
        defaultValue: "",
        description: "",
      },
      PAGE_PRODUCTS,
      ATTRIBUTE_TYPES,
      ATTRIBUTE_TYPE_OPTIONS,
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
      return this.attributeForm.type === 3 || this.attributeForm.type === 4;
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

    openAddAttributeDialog() {
      this.editingAttribute = null;
      this.attributeForm = {
        name: "",
        key: "",
        type: 0,
        isRequired: false,
        options: "",
        defaultValue: "",
        description: "",
      };
      this.showAttributeDialog = true;
    },

    openEditAttributeDialog(attribute) {
      this.editingAttribute = attribute;
      const normalizedType = ATTRIBUTE_TYPES[attribute.type]?.value ?? attribute.type;
      this.attributeForm = {
        name: attribute.name,
        key: attribute.key,
        type: normalizedType,
        isRequired: attribute.isRequired,
        options: attribute.options || "",
        defaultValue: attribute.defaultValue || "",
        description: attribute.description || "",
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
        options: this.showOptionsField ? this.attributeForm.options.trim() : null,
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

      // Fallback: comma-separated строка
      return optionsStr.split(',').map(o => o.trim()).filter(Boolean).map(label => ({
        value: label,
        label: label
      }));
    },

    /**
     * Количество опций
     */
    getOptionsCount(optionsStr) {
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

    <div class="flex justify-between items-center mb-4">
      <div>
        <h1 class="text-lg font-semibold">Глобальные атрибуты</h1>
        <p class="text-sm text-muted-foreground">
          Атрибуты, применяемые ко всем товарам магазина независимо от категории
        </p>
      </div>

      <Button @click="openAddAttributeDialog">
        <Plus class="size-4 mr-2" />
        Добавить атрибут
      </Button>
    </div>

    <div class="border rounded-lg h-full w-full grow mb-4 flex flex-col">
      <div v-if="isLoading" class="grow flex items-center justify-center">
        <Loader2 class="text-primary size-8 animate-spin" />
      </div>

      <div v-else class="grow flex flex-col p-4">
        <div v-if="attributes.length === 0" class="flex flex-col items-center justify-center py-12 text-center">
          <Settings2 class="size-12 mb-4 text-muted-foreground" />
          <h3 class="font-medium mb-1">Нет глобальных атрибутов</h3>
          <p class="text-sm text-muted-foreground">Добавьте атрибуты, которые будут применяться ко всем товарам</p>
        </div>

        <draggable
            v-else
            v-model="attributes"
            item-key="id"
            handle=".drag-handle"
            ghost-class="opacity-50"
            class="space-y-2"
            @end="handleReorder"
          >
            <template #item="{ element: attribute }">
              <Card class="group">
                <CardContent class="flex items-center gap-4">
                  <GripVertical class="drag-handle size-5 text-muted-foreground cursor-grab active:cursor-grabbing" />
                  <div class="grow">
                    <div class="flex items-center gap-2">
                      <span class="font-medium">{{ attribute.name }}</span>
                      <span class="text-xs text-muted-foreground bg-muted px-2 py-0.5 rounded">
                        {{ attribute.key }}
                      </span>
                      <span
                        v-if="attribute.isRequired"
                        class="text-xs text-red-500"
                      >
                        *
                      </span>
                    </div>
                    <div class="text-sm text-muted-foreground">
                      {{ getAttributeTypeLabel(attribute.type) }}
                      <span v-if="attribute.options" class="ml-2">
                        ({{ getOptionsCount(attribute.options) }} опций)
                      </span>
                    </div>
                    <div v-if="attribute.description" class="text-xs text-muted-foreground mt-1 line-clamp-1">
                      {{ attribute.description }}
                    </div>
                  </div>
                  <div class="flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      variant="ghost"
                      size="icon"
                      @click="openEditAttributeDialog(attribute)"
                    >
                      <Pencil class="size-4" />
                    </Button>
                    <AlertDialog>
                      <AlertDialogTrigger as-child>
                        <Button variant="ghost" size="icon">
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
                </CardContent>
              </Card>
            </template>
          </draggable>
      </div>
    </div>

    <!-- Attribute Dialog -->
    <Dialog v-model:open="showAttributeDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {{ editingAttribute ? 'Редактировать атрибут' : 'Новый глобальный атрибут' }}
          </DialogTitle>
          <DialogDescription>
            Настройте параметры атрибута для всех товаров магазина
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
            <Label>Варианты (через запятую)</Label>
            <Textarea
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
                  v-for="option in parseOptions(attributeForm.options)"
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
