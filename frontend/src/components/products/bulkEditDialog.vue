<template>
  <Dialog v-model:open="isOpen">
    <DialogContent class="max-w-2xl max-h-[90vh] overflow-hidden flex flex-col">
      <DialogHeader>
        <DialogTitle>Массовое редактирование</DialogTitle>
        <DialogDescription>
          Выбрано товаров: {{ selectedProducts.length }}
          <template v-if="!hasSameCategory">
            <span class="text-yellow-600 ml-2">
              (разные категории - поля категории недоступны)
            </span>
          </template>
        </DialogDescription>
      </DialogHeader>

      <Tabs v-model="activeTab" class="flex-1 overflow-hidden flex flex-col">
        <TabsList
          class="grid w-full"
          :class="hasSameCategory ? 'grid-cols-3' : 'grid-cols-2'"
        >
          <TabsTrigger value="product">Поля товара</TabsTrigger>
          <TabsTrigger v-if="hasSameCategory" value="category"
            >Поля категории</TabsTrigger
          >
          <TabsTrigger value="global">Глобальные поля</TabsTrigger>
        </TabsList>

        <div class="flex-1 overflow-y-auto mt-4">
          <!-- Product Fields Tab -->
          <TabsContent value="product" class="space-y-4 mt-0">
            <div class="space-y-4">
              <!-- Name -->
              <div class="flex items-start gap-3">
                <Checkbox
                  v-model="enabledFields.name"
                  id="enable-name"
                  class="mt-2"
                />
                <div class="flex-1 space-y-1">
                  <Label
                    for="name"
                    :class="{ 'text-muted-foreground': !enabledFields.name }"
                  >
                    Название
                  </Label>
                  <Input
                    id="name"
                    v-model="formData.name"
                    :disabled="!enabledFields.name"
                    placeholder="Новое название для всех товаров"
                  />
                </div>
              </div>

              <!-- Description -->
              <div class="flex items-start gap-3">
                <Checkbox
                  v-model="enabledFields.description"
                  id="enable-description"
                  class="mt-2"
                />
                <div class="flex-1 space-y-1">
                  <Label
                    for="description"
                    :class="{
                      'text-muted-foreground': !enabledFields.description,
                    }"
                  >
                    Описание
                  </Label>
                  <Textarea
                    id="description"
                    v-model="formData.description"
                    :disabled="!enabledFields.description"
                    placeholder="Новое описание для всех товаров"
                    rows="3"
                  />
                </div>
              </div>

              <!-- Storage Quantity -->
              <div class="flex items-start gap-3">
                <Checkbox
                  v-model="enabledFields.storageQuantity"
                  id="enable-storageQuantity"
                  class="mt-2"
                />
                <div class="flex-1 space-y-1">
                  <Label
                    for="storageQuantity"
                    :class="{
                      'text-muted-foreground': !enabledFields.storageQuantity,
                    }"
                  >
                    Количество на складе
                  </Label>
                  <Input
                    id="storageQuantity"
                    v-model.number="formData.storageQuantity"
                    type="number"
                    :disabled="!enabledFields.storageQuantity"
                    placeholder="0"
                    min="0"
                  />
                </div>
              </div>

              <!-- Price -->
              <div class="flex items-start gap-3">
                <Checkbox
                  v-model="enabledFields.price"
                  id="enable-price"
                  class="mt-2"
                />
                <div class="flex-1 space-y-1">
                  <Label
                    for="price"
                    :class="{ 'text-muted-foreground': !enabledFields.price }"
                  >
                    Цена
                  </Label>
                  <Input
                    id="price"
                    v-model.number="formData.price"
                    type="number"
                    :disabled="!enabledFields.price"
                    placeholder="0"
                    min="0"
                    step="0.01"
                  />
                </div>
              </div>

              <!-- Barcode -->
              <div class="flex items-start gap-3">
                <Checkbox
                  v-model="enabledFields.barcode"
                  id="enable-barcode"
                  class="mt-2"
                />
                <div class="flex-1 space-y-1">
                  <Label
                    for="barcode"
                    :class="{ 'text-muted-foreground': !enabledFields.barcode && !generateBarcodes }"
                  >
                    Штрихкод
                  </Label>
                  <BarcodeInput
                    v-model="formData.barcode"
                    :show-preview="false"
                    :disabled="!enabledFields.barcode"
                    :class="{ 'opacity-50 pointer-events-none': !enabledFields.barcode }"
                  />
                  <div class="flex items-center gap-2 pt-1">
                    <Checkbox
                      v-model="generateBarcodes"
                      id="generate-barcodes"
                    />
                    <Label
                      for="generate-barcodes"
                      class="text-sm font-normal cursor-pointer"
                    >
                      Сгенерировать всем у кого нет штрихкода
                    </Label>
                  </div>
                </div>
              </div>
            </div>
          </TabsContent>

          <!-- Category Fields Tab -->
          <TabsContent
            v-if="hasSameCategory"
            value="category"
            class="space-y-4 mt-0"
          >
            <template v-if="categoryAttributes.length > 0">
              <div
                v-for="attr in categoryAttributes"
                :key="attr.id"
                class="flex items-start gap-3"
              >
                <Checkbox
                  v-model="enabledCategoryAttributes[attr.id]"
                  :id="`enable-cat-${attr.id}`"
                  class="mt-2"
                />
                <div class="flex-1 space-y-1">
                  <Label
                    :for="`cat-${attr.id}`"
                    :class="{
                      'text-muted-foreground':
                        !enabledCategoryAttributes[attr.id],
                    }"
                  >
                    {{ attr.name }}
                  </Label>

                  <!-- Number -->
                  <Input
                    v-if="isType(attr.type, 'number')"
                    :id="`cat-${attr.id}`"
                    type="number"
                    v-model="categoryAttributeValues[attr.id]"
                    :disabled="!enabledCategoryAttributes[attr.id]"
                    :placeholder="attr.name"
                  />

                  <!-- Boolean -->
                  <div
                    v-else-if="isType(attr.type, 'boolean')"
                    class="flex items-center gap-2"
                  >
                    <Switch
                      :model-value="categoryAttributeValues[attr.id] === 'true'"
                      @update:model-value="categoryAttributeValues[attr.id] = $event ? 'true' : 'false'"
                      :disabled="!enabledCategoryAttributes[attr.id]"
                    />
                    <span class="text-sm text-muted-foreground">
                      {{ categoryAttributeValues[attr.id] === 'true' ? 'Да' : 'Нет' }}
                    </span>
                  </div>

                  <!-- Select -->
                  <SearchableSelect
                    v-else-if="isType(attr.type, 'select')"
                    v-model="categoryAttributeValues[attr.id]"
                    :options="getSelectOptions(attr)"
                    :placeholder="attr.name"
                    :disabled="!enabledCategoryAttributes[attr.id]"
                  />

                  <!-- MultiSelect -->
                  <MultiSelectDropdown
                    v-else-if="isType(attr.type, 'multiselect')"
                    :options="getSelectOptions(attr).map(o => o.label)"
                    :model-value="parseMultiSelect(categoryAttributeValues[attr.id])"
                    @update:model-value="categoryAttributeValues[attr.id] = JSON.stringify($event)"
                    :placeholder="attr.name"
                    :disabled="!enabledCategoryAttributes[attr.id]"
                  />

                  <!-- Text (default) -->
                  <Input
                    v-else
                    :id="`cat-${attr.id}`"
                    v-model="categoryAttributeValues[attr.id]"
                    :disabled="!enabledCategoryAttributes[attr.id]"
                    :placeholder="attr.name"
                  />
                </div>
              </div>
            </template>
            <div v-else class="text-center text-muted-foreground py-8">
              У категории нет настроенных атрибутов
            </div>
          </TabsContent>

          <!-- Global Fields Tab -->
          <TabsContent value="global" class="space-y-4 mt-0">
            <template v-if="storeAttributes.length > 0">
              <div
                v-for="attr in storeAttributes"
                :key="attr.id"
                class="flex items-start gap-3"
              >
                <Checkbox
                  v-model="enabledStoreAttributes[attr.id]"
                  :id="`enable-store-${attr.id}`"
                  class="mt-2"
                />
                <div class="flex-1 space-y-1">
                  <Label
                    :for="`store-${attr.id}`"
                    :class="{
                      'text-muted-foreground': !enabledStoreAttributes[attr.id],
                    }"
                  >
                    {{ attr.name }}
                  </Label>

                  <!-- Number -->
                  <Input
                    v-if="isType(attr.type, 'number')"
                    :id="`store-${attr.id}`"
                    type="number"
                    v-model="storeAttributeValues[attr.id]"
                    :disabled="!enabledStoreAttributes[attr.id]"
                    :placeholder="attr.name"
                  />

                  <!-- Boolean -->
                  <div
                    v-else-if="isType(attr.type, 'boolean')"
                    class="flex items-center gap-2"
                  >
                    <Switch
                      :model-value="storeAttributeValues[attr.id] === 'true'"
                      @update:model-value="storeAttributeValues[attr.id] = $event ? 'true' : 'false'"
                      :disabled="!enabledStoreAttributes[attr.id]"
                    />
                    <span class="text-sm text-muted-foreground">
                      {{ storeAttributeValues[attr.id] === 'true' ? 'Да' : 'Нет' }}
                    </span>
                  </div>

                  <!-- Select -->
                  <SearchableSelect
                    v-else-if="isType(attr.type, 'select')"
                    v-model="storeAttributeValues[attr.id]"
                    :options="getSelectOptions(attr)"
                    :placeholder="attr.name"
                    :disabled="!enabledStoreAttributes[attr.id]"
                  />

                  <!-- MultiSelect -->
                  <MultiSelectDropdown
                    v-else-if="isType(attr.type, 'multiselect')"
                    :options="getSelectOptions(attr).map(o => o.label)"
                    :model-value="parseMultiSelect(storeAttributeValues[attr.id])"
                    @update:model-value="storeAttributeValues[attr.id] = JSON.stringify($event)"
                    :placeholder="attr.name"
                    :disabled="!enabledStoreAttributes[attr.id]"
                  />

                  <!-- Text (default) -->
                  <Input
                    v-else
                    :id="`store-${attr.id}`"
                    v-model="storeAttributeValues[attr.id]"
                    :disabled="!enabledStoreAttributes[attr.id]"
                    :placeholder="attr.name"
                  />
                </div>
              </div>
            </template>
            <div v-else class="text-center text-muted-foreground py-8">
              Нет настроенных глобальных атрибутов
            </div>
          </TabsContent>
        </div>
      </Tabs>

      <DialogFooter class="mt-4">
        <Button variant="outline" @click="isOpen = false">Отмена</Button>
        <Button
          @click="handleSubmit"
          :disabled="!hasAnyEnabled || isSubmitting"
        >
          <Loader2 v-if="isSubmitting" class="mr-2 h-4 w-4 animate-spin" />
          Сохранить
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup>
// BulkEditDialog - Modal dialog for bulk-editing product fields, category attributes, and global attributes.
import { ref, computed, watch, reactive } from "vue";
import { useStore } from "vuex";
import { Loader2 } from "lucide-vue-next";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Switch } from "@/components/ui/switch";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import { toast } from "vue-sonner";

// Attribute types: 0=Text, 1=Number, 2=Boolean, 3=Select, 4=MultiSelect
const TYPE_TEXT = [0, "Text", "text"];
const TYPE_NUMBER = [1, "Number", "number"];
const TYPE_BOOLEAN = [2, "Boolean", "boolean"];
const TYPE_SELECT = [3, "Select", "select"];
const TYPE_MULTISELECT = [4, "MultiSelect", "multiSelect", "multiselect"];

function isType(attrType, typeName) {
  const typeMap = {
    text: TYPE_TEXT,
    number: TYPE_NUMBER,
    boolean: TYPE_BOOLEAN,
    select: TYPE_SELECT,
    multiselect: TYPE_MULTISELECT,
  };
  return typeMap[typeName]?.includes(attrType) ?? false;
}

function getSelectOptions(attribute) {
  if (!attribute.options) return [];
  try {
    const parsed = JSON.parse(attribute.options);
    if (Array.isArray(parsed)) {
      return parsed.map((o) => ({
        value: String(o.value || o.label || o),
        label: o.label || o.value || String(o),
      }));
    }
  } catch {
    // Not JSON
  }
  return attribute.options
    .split(",")
    .map((opt) => opt.trim())
    .filter(Boolean)
    .map((label) => ({ value: label, label }));
}

function parseMultiSelect(value) {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value);
    return Array.isArray(parsed) ? parsed : [String(parsed)];
  } catch {
    return value ? [value] : [];
  }
}

const props = defineProps({
  modelValue: Boolean,
  selectedProducts: {
    type: Array,
    required: true,
  },
  categoryAttributes: {
    type: Array,
    default: () => [],
  },
  storeAttributes: {
    type: Array,
    default: () => [],
  },
});

const emit = defineEmits(["update:modelValue", "saved"]);

const store = useStore();

const isOpen = computed({
  get: () => props.modelValue,
  set: (value) => emit("update:modelValue", value),
});

const activeTab = ref("product");
const isSubmitting = ref(false);

// Form data
const formData = reactive({
  name: "",
  description: "",
  storageQuantity: null,
  price: null,
  barcode: "",
});

// Which fields are enabled for editing
const enabledFields = reactive({
  name: false,
  description: false,
  storageQuantity: false,
  price: false,
  barcode: false,
});

// Generate barcodes for products that don't have one
const generateBarcodes = ref(false);

// Category attributes
const enabledCategoryAttributes = reactive({});
const categoryAttributeValues = reactive({});

// Store (global) attributes
const enabledStoreAttributes = reactive({});
const storeAttributeValues = reactive({});

// Check if all selected products have the same category
const hasSameCategory = computed(() => {
  if (props.selectedProducts.length === 0) return false;
  const categoryIds = props.selectedProducts.map((p) => p.categoryId);
  const uniqueCategories = [...new Set(categoryIds)];
  return uniqueCategories.length === 1 && uniqueCategories[0] !== null;
});

// Check if any field is enabled
const hasAnyEnabled = computed(() => {
  return (
    enabledFields.name ||
    enabledFields.description ||
    enabledFields.storageQuantity ||
    enabledFields.price ||
    enabledFields.barcode ||
    generateBarcodes.value ||
    Object.values(enabledCategoryAttributes).some((v) => v) ||
    Object.values(enabledStoreAttributes).some((v) => v)
  );
});

// Initialize attribute states when props change
watch(
  () => props.categoryAttributes,
  (attrs) => {
    attrs.forEach((attr) => {
      if (!(attr.id in enabledCategoryAttributes)) {
        enabledCategoryAttributes[attr.id] = false;
        categoryAttributeValues[attr.id] = "";
      }
    });
  },
  { immediate: true },
);

watch(
  () => props.storeAttributes,
  (attrs) => {
    attrs.forEach((attr) => {
      if (!(attr.id in enabledStoreAttributes)) {
        enabledStoreAttributes[attr.id] = false;
        storeAttributeValues[attr.id] = "";
      }
    });
  },
  { immediate: true },
);

// Reset form when dialog opens
watch(isOpen, (open) => {
  if (open) {
    // Reset basic fields
    formData.name = "";
    formData.description = "";
    formData.storageQuantity = null;
    formData.price = null;
    formData.barcode = "";
    enabledFields.name = false;
    enabledFields.description = false;
    enabledFields.storageQuantity = false;
    enabledFields.price = false;
    enabledFields.barcode = false;
    generateBarcodes.value = false;

    // Reset category attributes
    Object.keys(enabledCategoryAttributes).forEach((key) => {
      enabledCategoryAttributes[key] = false;
      categoryAttributeValues[key] = "";
    });

    // Reset store attributes
    Object.keys(enabledStoreAttributes).forEach((key) => {
      enabledStoreAttributes[key] = false;
      storeAttributeValues[key] = "";
    });

    activeTab.value = "product";
  }
});

async function handleSubmit() {
  const productIds = props.selectedProducts.map((p) => p.id);

  const updates = {};

  // Basic fields
  if (enabledFields.name && formData.name) {
    updates.name = formData.name;
  }
  if (enabledFields.description) {
    updates.description = formData.description || "";
  }
  if (enabledFields.storageQuantity && formData.storageQuantity !== null) {
    updates.storageQuantity = formData.storageQuantity;
  }
  if (enabledFields.price && formData.price !== null) {
    updates.price = formData.price;
  }
  if (enabledFields.barcode) {
    updates.barcode = formData.barcode || "";
  }
  if (generateBarcodes.value) {
    updates.generateBarcodes = true;
  }

  // Category attribute values
  const enabledCatAttrs = Object.entries(enabledCategoryAttributes)
    .filter((entry) => entry[1])
    .map(([id]) => ({
      attributeId: parseInt(id),
      value: categoryAttributeValues[id] || "",
    }));
  if (enabledCatAttrs.length > 0) {
    updates.attributeValues = enabledCatAttrs;
  }

  // Store attribute values
  const enabledStoreAttrs = Object.entries(enabledStoreAttributes)
    .filter((entry) => entry[1])
    .map(([id]) => ({
      storeAttributeId: parseInt(id),
      value: storeAttributeValues[id] || "",
    }));
  if (enabledStoreAttrs.length > 0) {
    updates.storeAttributeValues = enabledStoreAttrs;
  }

  if (Object.keys(updates).length === 0) {
    toast.error("Выберите хотя бы одно поле для редактирования");
    return;
  }

  isSubmitting.value = true;

  await store.dispatch("bulkUpdateProducts", {
    productIds,
    updates,
    onSuccess: (response) => {
      const count = response.data?.value?.updatedCount || productIds.length;
      toast.success(`Обновлено товаров: ${count}`);
      isOpen.value = false;
      emit("saved");
    },
    onError: (error) => {
      toast.error(
        error.response?.data?.message || "Не удалось обновить товары",
      );
    },
  });

  isSubmitting.value = false;
}
</script>
