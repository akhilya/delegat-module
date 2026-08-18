<script setup>
import { Spinner } from "@/components/ui/spinner";
// BulkEditDialog — enable-then-edit fields for a product selection.
// Shell matches FieldChangesPreviewDialog / importExcelDialog (bordered header/footer, min-h-0 scroll).
import { ref, computed, watch, reactive } from "vue";
import { useStore } from "vuex";
import {
  AlertTriangle,
} from "lucide-vue-next";
import { t } from "@/i18n";
import {
  Dialog,
  DialogClose,
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
import { Checkbox } from "@/components/ui/checkbox";
import { Switch } from "@/components/ui/switch";
import { Badge } from "@/components/ui/badge";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { MultiSelectDropdown } from "@/components/ui/multi-select";
import { BarcodeInput } from "@/components/ui/barcode";
import { toast } from "vue-sonner";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  labelsToStoredValues,
  parseAttributeOptions,
  resolveAttributeOptionLabels,
} from "@/lib/attributeOptions";

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
  return parseAttributeOptions(attribute.options);
}

function parseMultiSelectLabels(attribute, value) {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value);
    const values = Array.isArray(parsed) ? parsed : [parsed];
    return resolveAttributeOptionLabels(attribute.options, values);
  } catch {
    return value
      ? String(value)
          .split(",")
          .map((part) => part.trim())
          .filter(Boolean)
          .map((part) => resolveAttributeOptionLabels(attribute.options, [part])[0])
      : [];
  }
}

function setMultiSelectValue(attribute, labels) {
  return JSON.stringify(labelsToStoredValues(attribute.options, labels));
}

function maxSelections(attribute) {
  const n = Number(attribute?.maxValues);
  return Number.isInteger(n) && n > 0 ? n : 0;
}

const props = defineProps({
  modelValue: Boolean,
  selectedProducts: {
    type: Array,
    required: true,
  },
  productIds: {
    type: Array,
    default: null,
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

const resolvedProductIds = computed(() => {
  if (Array.isArray(props.productIds) && props.productIds.length > 0) {
    return props.productIds.map((id) => Number(id)).filter((id) => Number.isFinite(id));
  }
  return props.selectedProducts.map((p) => p.id).filter((id) => id != null);
});

const selectedCount = computed(() => resolvedProductIds.value.length);

const visibleCategoryAttributes = computed(() =>
  props.categoryAttributes.filter((a) => a.showOnProductPage !== false),
);
const visibleStoreAttributes = computed(() =>
  props.storeAttributes.filter((a) => a.showOnProductPage !== false),
);

const activeTab = ref("product");
const isSubmitting = ref(false);

const formData = reactive({
  name: "",
  description: "",
  storageQuantity: null,
  price: null,
  barcode: "",
});

const enabledFields = reactive({
  name: false,
  description: false,
  storageQuantity: false,
  price: false,
  barcode: false,
});

const generateBarcodes = ref(false);

const enabledCategoryAttributes = reactive({});
const categoryAttributeValues = reactive({});
const enabledStoreAttributes = reactive({});
const storeAttributeValues = reactive({});

const hasSameCategory = computed(() => {
  if (props.selectedProducts.length === 0) return false;
  const categoryIds = props.selectedProducts.map((p) => p.categoryId);
  const uniqueCategories = [...new Set(categoryIds)];
  return uniqueCategories.length === 1 && uniqueCategories[0] !== null;
});

const blocksUniqueAttributes = computed(() => selectedCount.value > 1);

function isUniqueAttribute(attr) {
  return attr?.isUnique === true;
}

function isAttributeEditable(attr) {
  return !(blocksUniqueAttributes.value && isUniqueAttribute(attr));
}

const hasAnyEnabled = computed(() => {
  return (
    enabledFields.name ||
    enabledFields.description ||
    enabledFields.storageQuantity ||
    enabledFields.price ||
    enabledFields.barcode ||
    generateBarcodes.value ||
    Object.entries(enabledCategoryAttributes).some(
      ([id, on]) =>
        on &&
        isAttributeEditable(
          visibleCategoryAttributes.value.find((a) => String(a.id) === String(id)),
        ),
    ) ||
    Object.entries(enabledStoreAttributes).some(
      ([id, on]) =>
        on &&
        isAttributeEditable(
          visibleStoreAttributes.value.find((a) => String(a.id) === String(id)),
        ),
    )
  );
});

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

watch(isOpen, (open) => {
  if (!open) return;

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

  Object.keys(enabledCategoryAttributes).forEach((key) => {
    enabledCategoryAttributes[key] = false;
    categoryAttributeValues[key] = "";
  });
  Object.keys(enabledStoreAttributes).forEach((key) => {
    enabledStoreAttributes[key] = false;
    storeAttributeValues[key] = "";
  });

  activeTab.value = "product";
});

watch(generateBarcodes, (on) => {
  if (on) enabledFields.barcode = false;
});

watch(
  () => enabledFields.barcode,
  (on) => {
    if (on) generateBarcodes.value = false;
  },
);

async function handleSubmit() {
  const productIds = resolvedProductIds.value;
  if (productIds.length === 0) {
    toast.error(t("products.bulkEditDialog.noProductsSelected"));
    return;
  }

  const updates = {};

  if (enabledFields.name) {
    if (fieldLimitIssue(formData.name, FIELD_LIMITS.productName) === "tooLong") {
      toast.error(t("common.tooLong", { max: FIELD_LIMITS.productName }));
      return;
    }
    updates.name = formData.name || "";
  }
  if (enabledFields.description) {
    if (
      fieldLimitIssue(formData.description, FIELD_LIMITS.productDescription, {
        required: false,
      }) === "tooLong"
    ) {
      toast.error(t("common.tooLong", { max: FIELD_LIMITS.productDescription }));
      return;
    }
    updates.description = formData.description || "";
  }
  if (enabledFields.storageQuantity && formData.storageQuantity !== null && formData.storageQuantity !== "") {
    updates.storageQuantity = Number(formData.storageQuantity);
  }
  if (enabledFields.price && formData.price !== null && formData.price !== "") {
    updates.price = Number(formData.price);
  }
  if (enabledFields.barcode) {
    updates.barcode = formData.barcode || "";
  }
  if (generateBarcodes.value) {
    updates.generateBarcodes = true;
  }

  const enabledCatAttrs = Object.entries(enabledCategoryAttributes)
    .filter(([id, on]) => {
      if (!on) return false;
      const attr = visibleCategoryAttributes.value.find((a) => String(a.id) === String(id));
      return isAttributeEditable(attr);
    })
    .map(([id]) => ({
      attributeId: parseInt(id, 10),
      value: categoryAttributeValues[id] || "",
    }));
  if (enabledCatAttrs.length > 0) {
    updates.attributeValues = enabledCatAttrs;
  }

  const enabledStoreAttrs = Object.entries(enabledStoreAttributes)
    .filter(([id, on]) => {
      if (!on) return false;
      const attr = visibleStoreAttributes.value.find((a) => String(a.id) === String(id));
      return isAttributeEditable(attr);
    })
    .map(([id]) => ({
      storeAttributeId: parseInt(id, 10),
      value: storeAttributeValues[id] || "",
    }));
  if (enabledStoreAttrs.length > 0) {
    updates.storeAttributeValues = enabledStoreAttrs;
  }

  if (Object.keys(updates).length === 0) {
    toast.error(t("products.bulkEditDialog.selectFieldToEdit"));
    return;
  }

  isSubmitting.value = true;

  await store.dispatch("bulkUpdateProducts", {
    productIds,
    updates,
    onSuccess: (response) => {
      const payload = response?.data?.value ?? response?.data ?? {};
      if (payload.queued) {
        const count = payload.totalItems ?? productIds.length;
        displaySonnerSuccess(
          t("products.bulkEditDialog.queuedHeadline", { count }),
        );
        store.dispatch("operations/openPanel");
      } else {
        const count = payload.updatedCount ?? productIds.length;
        toast.success(t("products.bulkEditDialog.updatedCount", { count }));
      }
      isOpen.value = false;
      emit("saved");
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });

  isSubmitting.value = false;
}
</script>

<template>
  <Dialog v-model:open="isOpen">
    <DialogContent
      size="2xl"
      class="flex max-h-[90vh] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader variant="bar">
        <DialogTitle>
          {{ t("products.bulkEditDialog.title") }}
        </DialogTitle>
        <DialogDescription>
          {{ t("products.bulkEditDialog.selectedCount", { count: selectedCount }) }}
        </DialogDescription>
      </DialogHeader>

      <div
        v-if="!hasSameCategory && selectedCount > 0"
        class="shrink-0 flex items-start gap-2 border-b border-warning/30 bg-warning-muted/40 px-6 py-2.5 text-sm text-warning-foreground"
      >
        <AlertTriangle class="mt-0.5 size-4 shrink-0" />
        <span>{{ t("products.bulkEditDialog.differentCategoriesWarning") }}</span>
      </div>

      <Tabs
        v-model="activeTab"
        class="flex min-h-0 flex-1 flex-col gap-0 overflow-hidden"
      >
        <div class="shrink-0 border-b border-border px-6 pt-3">
          <TabsList
            class="grid w-full"
            :class="hasSameCategory ? 'grid-cols-3' : 'grid-cols-2'"
          >
            <TabsTrigger value="product">
              {{ t("products.bulkEditDialog.productFieldsTab") }}
            </TabsTrigger>
            <TabsTrigger v-if="hasSameCategory" value="category">
              {{ t("products.bulkEditDialog.categoryFieldsTab") }}
            </TabsTrigger>
            <TabsTrigger value="global">
              {{ t("products.bulkEditDialog.globalFieldsTab") }}
            </TabsTrigger>
          </TabsList>
          <p class="pb-3 pt-2 text-xs text-muted-foreground">
            {{ t("products.bulkEditDialog.enableHint") }}
          </p>
        </div>

        <div class="min-h-0 flex-1 overflow-y-auto px-6 py-4">
          <TabsContent value="product" class="mt-0">
            <FieldGroup class="gap-0">
              <div class="flex items-start gap-3 py-3">
                <Checkbox v-model="enabledFields.name" id="bulk-enable-name" class="mt-0.5" />
                <SettingsFormField
                  class="min-w-0 flex-1"
                  :class="{ 'opacity-60': !enabledFields.name }"
                  :label="t('common.name')"
                  label-for="bulk-name"
                >
                  <Input
                    id="bulk-name"
                    v-model="formData.name"
                    :disabled="!enabledFields.name"
                    :placeholder="t('products.bulkEditDialog.namePlaceholder')"
                    :maxlength="FIELD_LIMITS.productName"
                  />
                </SettingsFormField>
              </div>

              <div class="flex items-start gap-3 py-3">
                <Checkbox
                  v-model="enabledFields.description"
                  id="bulk-enable-description"
                  class="mt-0.5"
                />
                <SettingsFormField
                  class="min-w-0 flex-1"
                  :class="{ 'opacity-60': !enabledFields.description }"
                  :label="t('common.description')"
                  label-for="bulk-description"
                >
                  <Textarea
                    id="bulk-description"
                    v-model="formData.description"
                    :disabled="!enabledFields.description"
                    :placeholder="t('products.bulkEditDialog.descriptionPlaceholder')"
                    :maxlength="FIELD_LIMITS.productDescription"
                    rows="3"
                  />
                </SettingsFormField>
              </div>

              <div class="flex items-start gap-3 py-3">
                <Checkbox
                  v-model="enabledFields.storageQuantity"
                  id="bulk-enable-storageQuantity"
                  class="mt-0.5"
                />
                <SettingsFormField
                  class="min-w-0 flex-1"
                  :class="{ 'opacity-60': !enabledFields.storageQuantity }"
                  :label="t('products.bulkEditDialog.storageQuantityLabel')"
                  label-for="bulk-storageQuantity"
                >
                  <Input
                    id="bulk-storageQuantity"
                    v-model.number="formData.storageQuantity"
                    type="number"
                    class="tabular-nums"
                    :disabled="!enabledFields.storageQuantity"
                    placeholder="0"
                    min="0"
                  />
                </SettingsFormField>
              </div>

              <div class="flex items-start gap-3 py-3">
                <Checkbox v-model="enabledFields.price" id="bulk-enable-price" class="mt-0.5" />
                <SettingsFormField
                  class="min-w-0 flex-1"
                  :class="{ 'opacity-60': !enabledFields.price }"
                  :label="t('products.bulkEditDialog.priceLabel')"
                  label-for="bulk-price"
                >
                  <Input
                    id="bulk-price"
                    v-model.number="formData.price"
                    type="number"
                    class="tabular-nums"
                    :disabled="!enabledFields.price"
                    placeholder="0"
                    min="0"
                    step="0.01"
                  />
                </SettingsFormField>
              </div>

              <div class="flex items-start gap-3 py-3">
                <Checkbox
                  v-model="enabledFields.barcode"
                  id="bulk-enable-barcode"
                  class="mt-0.5"
                  :disabled="generateBarcodes"
                />
                <SettingsFormField
                  class="min-w-0 flex-1"
                  :class="{ 'opacity-60': !enabledFields.barcode && !generateBarcodes }"
                  :label="t('products.bulkEditDialog.barcodeLabel')"
                  label-for="bulk-barcode"
                >
                  <BarcodeInput
                    v-model="formData.barcode"
                    :show-preview="false"
                    :disabled="!enabledFields.barcode"
                  />
                  <label class="flex cursor-pointer items-center gap-2 pt-0.5">
                    <Checkbox v-model="generateBarcodes" id="bulk-generate-barcodes" />
                    <span class="text-sm font-normal">
                      {{ t("products.bulkEditDialog.generateBarcodesLabel") }}
                    </span>
                  </label>
                </SettingsFormField>
              </div>
            </FieldGroup>
          </TabsContent>

          <TabsContent v-if="hasSameCategory" value="category" class="mt-0">
            <template v-if="visibleCategoryAttributes.length > 0">
              <FieldGroup class="gap-0">
                <div
                  v-for="attr in visibleCategoryAttributes"
                  :key="attr.id"
                  class="flex items-start gap-3 py-3"
                >
                  <Checkbox
                    v-model="enabledCategoryAttributes[attr.id]"
                    :id="`bulk-enable-cat-${attr.id}`"
                    class="mt-0.5"
                    :disabled="!isAttributeEditable(attr)"
                  />
                  <SettingsFormField
                    class="min-w-0 flex-1"
                    :class="{
                      'opacity-60':
                        !enabledCategoryAttributes[attr.id] || !isAttributeEditable(attr),
                    }"
                    :label="attr.name"
                    :label-for="`bulk-cat-${attr.id}`"
                    :description="
                      !isAttributeEditable(attr)
                        ? t('products.bulkEditDialog.uniqueAttrBlocked')
                        : ''
                    "
                  >
                    <div
                      v-if="isUniqueAttribute(attr)"
                      class="flex flex-wrap items-center gap-2 pb-1.5"
                    >
                      <Badge variant="outline" class="text-[10px]">
                        {{ t("products.bulkEditDialog.uniqueBadge") }}
                      </Badge>
                    </div>
                    <template v-if="isAttributeEditable(attr)">
                      <Input
                        v-if="isType(attr.type, 'number')"
                        :id="`bulk-cat-${attr.id}`"
                        type="number"
                        class="tabular-nums"
                        v-model="categoryAttributeValues[attr.id]"
                        :disabled="!enabledCategoryAttributes[attr.id]"
                        :placeholder="attr.name"
                      />
                      <div
                        v-else-if="isType(attr.type, 'boolean')"
                        class="flex items-center gap-2"
                      >
                        <Switch
                          :model-value="categoryAttributeValues[attr.id] === 'true'"
                          @update:model-value="
                            categoryAttributeValues[attr.id] = $event ? 'true' : 'false'
                          "
                          :disabled="!enabledCategoryAttributes[attr.id]"
                        />
                        <span class="text-sm text-muted-foreground">
                          {{
                            categoryAttributeValues[attr.id] === "true"
                              ? t("common.yes")
                              : t("common.no")
                          }}
                        </span>
                      </div>
                      <SearchableSelect
                        v-else-if="isType(attr.type, 'select')"
                        v-model="categoryAttributeValues[attr.id]"
                        :options="getSelectOptions(attr)"
                        :placeholder="attr.name"
                        :disabled="!enabledCategoryAttributes[attr.id]"
                        :allow-create="!!attr.allowCustomValues"
                      />
                      <MultiSelectDropdown
                        v-else-if="isType(attr.type, 'multiselect')"
                        :options="getSelectOptions(attr).map((o) => o.label)"
                        :model-value="
                          parseMultiSelectLabels(attr, categoryAttributeValues[attr.id])
                        "
                        @update:model-value="
                          categoryAttributeValues[attr.id] = setMultiSelectValue(attr, $event)
                        "
                        :placeholder="attr.name"
                        :disabled="!enabledCategoryAttributes[attr.id]"
                        :allow-create="!!attr.allowCustomValues"
                        :max-selections="maxSelections(attr)"
                      />
                      <Textarea
                        v-else-if="isType(attr.type, 'text') && attr.isMultiline"
                        :id="`bulk-cat-${attr.id}`"
                        v-model="categoryAttributeValues[attr.id]"
                        :disabled="!enabledCategoryAttributes[attr.id]"
                        :placeholder="attr.name"
                        class="min-h-24 resize-y"
                      />
                      <Input
                        v-else
                        :id="`bulk-cat-${attr.id}`"
                        v-model="categoryAttributeValues[attr.id]"
                        :disabled="!enabledCategoryAttributes[attr.id]"
                        :placeholder="attr.name"
                      />
                    </template>
                  </SettingsFormField>
                </div>
              </FieldGroup>
            </template>
            <div v-else class="py-10 text-center text-sm text-muted-foreground">
              {{ t("products.bulkEditDialog.noCategoryAttributes") }}
            </div>
          </TabsContent>

          <TabsContent value="global" class="mt-0">
            <template v-if="visibleStoreAttributes.length > 0">
              <FieldGroup class="gap-0">
                <div
                  v-for="attr in visibleStoreAttributes"
                  :key="attr.id"
                  class="flex items-start gap-3 py-3"
                >
                  <Checkbox
                    v-model="enabledStoreAttributes[attr.id]"
                    :id="`bulk-enable-store-${attr.id}`"
                    class="mt-0.5"
                    :disabled="!isAttributeEditable(attr)"
                  />
                  <SettingsFormField
                    class="min-w-0 flex-1"
                    :class="{
                      'opacity-60':
                        !enabledStoreAttributes[attr.id] || !isAttributeEditable(attr),
                    }"
                    :label="attr.name"
                    :label-for="`bulk-store-${attr.id}`"
                    :description="
                      !isAttributeEditable(attr)
                        ? t('products.bulkEditDialog.uniqueAttrBlocked')
                        : ''
                    "
                  >
                    <div
                      v-if="isUniqueAttribute(attr)"
                      class="flex flex-wrap items-center gap-2 pb-1.5"
                    >
                      <Badge variant="outline" class="text-[10px]">
                        {{ t("products.bulkEditDialog.uniqueBadge") }}
                      </Badge>
                    </div>
                    <template v-if="isAttributeEditable(attr)">
                      <Input
                        v-if="isType(attr.type, 'number')"
                        :id="`bulk-store-${attr.id}`"
                        type="number"
                        class="tabular-nums"
                        v-model="storeAttributeValues[attr.id]"
                        :disabled="!enabledStoreAttributes[attr.id]"
                        :placeholder="attr.name"
                      />
                      <div
                        v-else-if="isType(attr.type, 'boolean')"
                        class="flex items-center gap-2"
                      >
                        <Switch
                          :model-value="storeAttributeValues[attr.id] === 'true'"
                          @update:model-value="
                            storeAttributeValues[attr.id] = $event ? 'true' : 'false'
                          "
                          :disabled="!enabledStoreAttributes[attr.id]"
                        />
                        <span class="text-sm text-muted-foreground">
                          {{
                            storeAttributeValues[attr.id] === "true"
                              ? t("common.yes")
                              : t("common.no")
                          }}
                        </span>
                      </div>
                      <SearchableSelect
                        v-else-if="isType(attr.type, 'select')"
                        v-model="storeAttributeValues[attr.id]"
                        :options="getSelectOptions(attr)"
                        :placeholder="attr.name"
                        :disabled="!enabledStoreAttributes[attr.id]"
                      />
                      <MultiSelectDropdown
                        v-else-if="isType(attr.type, 'multiselect')"
                        :options="getSelectOptions(attr).map((o) => o.label)"
                        :model-value="
                          parseMultiSelectLabels(attr, storeAttributeValues[attr.id])
                        "
                        @update:model-value="
                          storeAttributeValues[attr.id] = setMultiSelectValue(attr, $event)
                        "
                        :placeholder="attr.name"
                        :disabled="!enabledStoreAttributes[attr.id]"
                        :max-selections="maxSelections(attr)"
                      />
                      <Textarea
                        v-else-if="isType(attr.type, 'text') && attr.isMultiline"
                        :id="`bulk-store-${attr.id}`"
                        v-model="storeAttributeValues[attr.id]"
                        :disabled="!enabledStoreAttributes[attr.id]"
                        :placeholder="attr.name"
                        class="min-h-24 resize-y"
                      />
                      <Input
                        v-else
                        :id="`bulk-store-${attr.id}`"
                        v-model="storeAttributeValues[attr.id]"
                        :disabled="!enabledStoreAttributes[attr.id]"
                        :placeholder="attr.name"
                      />
                    </template>
                  </SettingsFormField>
                </div>
              </FieldGroup>
            </template>
            <div v-else class="py-10 text-center text-sm text-muted-foreground">
              {{ t("products.bulkEditDialog.noGlobalAttributes") }}
            </div>
          </TabsContent>
        </div>
      </Tabs>

      <DialogFooter variant="bar" class="sm:justify-between">
        <DialogClose as-child>
          <Button type="button" variant="outline" :disabled="isSubmitting">
            {{ t("common.cancel") }}
          </Button>
        </DialogClose>
        <Button
          type="button"
          :disabled="!hasAnyEnabled || isSubmitting || selectedCount === 0"
          @click="handleSubmit"
        >
          <Spinner v-if="isSubmitting" class="mr-2" />
          {{ t("common.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
