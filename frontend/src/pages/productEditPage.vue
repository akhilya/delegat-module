<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Label from "@/components/ui/label/Label.vue";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
  AlertDialogDescription,
} from "@/components/ui/alert-dialog";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import ProductFilesTable from "@/components/productFilesTable.vue";
import ProductImagesEditor from "@/components/productImagesEditor.vue";
import ProductAttributeForm from "@/components/products/productAttributeForm.vue";
import StoreAttributeForm from "@/components/products/storeAttributeForm.vue";
import ProductMarketplaceIntegrations from "@/components/products/productMarketplaceIntegrations.vue";
import { BarcodeInput } from "@/components/ui/barcode";
import { Progress } from "@/components/ui/progress";
import Separator from "@/components/ui/separator/Separator.vue";
import Note from "@/components/note.vue";

import {
  Loader2,
  Upload,
  Save,
  Trash2,
  NotebookPen,
  FolderOpen,
  Globe,
  Layers,
  ExternalLink,
  Check,
  Pencil,
} from "lucide-vue-next";
import { PAGE_PRODUCTS } from "@/router";
import { useStore } from "vuex";
import { useRoute } from "vue-router";
import { ref, computed, watch } from "vue";
import { toTypedSchema } from "@vee-validate/zod";
import { useForm } from "vee-validate";
import { z } from "zod";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useFileDialog, useDebounceFn } from "@vueuse/core";
import { router } from "@/main";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    Button,
    FormControl,
    FormDescription,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogTrigger,
    AlertDialogDescription,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    Input,
    Textarea,
    Label,
    ProductFilesTable,
    ProductImagesEditor,
    ProductAttributeForm,
    StoreAttributeForm,
    ProductMarketplaceIntegrations,
    BarcodeInput,
    Separator,
    Loader2,
    Upload,
    Save,
    Trash2,
    NotebookPen,
    FolderOpen,
    Globe,
    Pencil,
    Note,
    Progress,
    Layers,
    ExternalLink,
    Check,
  },
  data() {
    return {
      PAGE_PRODUCTS,
      tab: this.$route.query.tab ?? "properties",
    };
  },
  computed: {
    breadcrumbs() {
      const items = [{ label: "Товары", to: PAGE_PRODUCTS }];
      if (this.productSKU) {
        items.push({ label: this.productSKU });
      } else {
        items.push({ label: "Редактирование" });
      }
      return items;
    },
  },
  watch: {
    tab(newVal) {
      this.$router.replace({ query: { tab: newVal } });
    },
  },
  setup() {
    const store = useStore();
    const route = useRoute();
    const isSubmitting = ref(false);
    const isProductLoading = ref(false);
    const isUploading = ref(false);
    const uploadProgress = ref({ loaded: 0, total: 0, percent: 0 });
    const productSKU = ref("");
    const files = ref([]);
    const productId = parseInt(String(route.params.id));
    const images = ref(undefined);
    const isSavingOrder = ref(false);
    const notes = ref([]);
    const category = ref(null);
    const attributeValues = ref([]);
    const storeAttributes = ref([]);
    const storeAttributeValues = ref([]);
    const isLoadingStoreAttributes = ref(false);
    const productGroup = ref(null);
    const isLoadingGroup = ref(false);
    const categoryAttributeErrors = ref({});
    const storeAttributeErrors = ref({});
    const lastSavedAt = ref(null);
    const hasUnsavedChanges = ref(false);
    const isInitialLoad = ref(true);
    const showSkuDialog = ref(false);
    const newSkuValue = ref("");
    const isUpdatingSku = ref(false);

    // Fetch product group
    async function fetchProductGroup() {
      isLoadingGroup.value = true;
      await store.dispatch("getProductGroup", {
        productId,
        onSuccess: (response) => {
          const data = response.data.value;
          if (data?.hasGroup === false || !data?.group) {
            productGroup.value = null;
          } else {
            productGroup.value = data.group;
          }
        },
        onError: () => {
          productGroup.value = null;
        },
      });
      isLoadingGroup.value = false;
    }

    // Fetch store attributes on mount
    async function fetchStoreAttributes() {
      isLoadingStoreAttributes.value = true;
      await store.dispatch("getAllStoreAttributes", {
        onSuccess: (response) => {
          storeAttributes.value = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isLoadingStoreAttributes.value = false;
    }

    fetchStoreAttributes();
    fetchProduct();
    fetchProductGroup();

    async function fetchProduct() {
      isProductLoading.value = true;
      await store.dispatch("getProduct", {
        productId,
        onSuccess: (response) => {
          const product = response.data.value;
          productSKU.value = product.sku;
          notes.value = product.notes || [];
          files.value = (product.files || []).sort((a, b) => {
            if (a.contentType < b.contentType) return -1;
            if (a.contentType > b.contentType) return 1;
            return 0;
          });
          images.value = product.images;
          category.value = product.category;
          attributeValues.value = product.attributeValues || [];
          storeAttributeValues.value = product.storeAttributeValues || [];

          form.setValues({
            product: {
              name: product.name,
              description: product.description,
              barcode: product.barcode || "",
              storageQuantity: product.storageQuantity,
              price: product.price,
            },
          });

          // Mark initial load as complete after a short delay
          setTimeout(() => {
            isInitialLoad.value = false;
          }, 100);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isProductLoading.value = false;
    }

    const formSchema = toTypedSchema(
      z.object({
        product: z.object({
          name: z.string().min(1, "Название обязательно").max(100),
          description: z.string().max(5000).optional(),
          barcode: z.string().max(13).optional().nullable(),
          storageQuantity: z.number().int().min(0, "Количество не может быть отрицательным").optional().nullable(),
          price: z.number().min(0, "Цена не может быть отрицательной").optional().nullable(),
        }),
      }),
    );

    const form = useForm({
      validationSchema: formSchema,
      initialValues: {
        product: {
          name: "",
          description: "",
          barcode: "",
          storageQuantity: null,
          price: null,
        },
      },
    });

    // Validate attributes and return true if valid
    function validateAttributes() {
      let isValid = true;
      const catErrors = {};
      const storeErrors = {};

      // Validate required category attributes
      if (category.value?.attributes) {
        const requiredAttrs = category.value.attributes.filter((a) => a.isRequired);
        for (const attr of requiredAttrs) {
          const attrValue = attributeValues.value.find((av) => av.attributeId === attr.id);
          if (!attrValue || !attrValue.value) {
            catErrors[attr.id] = "Обязательное поле";
            isValid = false;
          }
        }
      }

      // Validate required store attributes
      const requiredStoreAttrs = storeAttributes.value.filter((a) => a.isRequired);
      for (const attr of requiredStoreAttrs) {
        const attrValue = storeAttributeValues.value.find((av) => av.storeAttributeId === attr.id);
        if (!attrValue || !attrValue.value) {
          storeErrors[attr.id] = "Обязательное поле";
          isValid = false;
        }
      }

      categoryAttributeErrors.value = catErrors;
      storeAttributeErrors.value = storeErrors;

      return isValid;
    }

    // Save product (used by both manual and auto save)
    async function saveProduct(showSuccessToast = true) {
      // First validate the main form
      const formValid = await form.validate();
      if (!formValid.valid) {
        return false;
      }

      // Then validate attributes
      if (!validateAttributes()) {
        if (showSuccessToast) {
          displaySonnerError("Заполните все обязательные поля");
        }
        return false;
      }

      isSubmitting.value = true;

      const values = form.values;
      const input = {
        ...values.product,
        attributeValues: attributeValues.value,
        storeAttributeValues: storeAttributeValues.value,
      };

      try {
        await store.dispatch("updateProduct", {
          productId,
          input,
          onSuccess: () => {
            lastSavedAt.value = new Date();
            hasUnsavedChanges.value = false;
            if (showSuccessToast) {
              displaySonnerSuccess("Товар успешно обновлён.");
            }
          },
          onError: (error) => {
            if (error?.response?.data?.errors)
              form.setErrors(error.response.data.errors);
            displaySonnerError(error);
          },
        });
        return true;
      } catch (error) {
        displaySonnerError(error);
        return false;
      } finally {
        isSubmitting.value = false;
      }
    }

    const onSubmit = form.handleSubmit(async () => {
      await saveProduct(true);
    });

    // Debounced autosave (1.5 second delay)
    const debouncedAutoSave = useDebounceFn(async () => {
      if (isInitialLoad.value || isSubmitting.value) return;
      await saveProduct(false);
    }, 1500);

    // Watch for form changes and trigger autosave
    watch(
      () => form.values,
      () => {
        if (isInitialLoad.value) return;
        hasUnsavedChanges.value = true;
        debouncedAutoSave();
      },
      { deep: true }
    );

    // Watch for attribute changes and trigger autosave
    watch(
      [attributeValues, storeAttributeValues],
      () => {
        if (isInitialLoad.value) return;
        hasUnsavedChanges.value = true;
        // Clear errors for changed attributes
        categoryAttributeErrors.value = {};
        storeAttributeErrors.value = {};
        debouncedAutoSave();
      },
      { deep: true }
    );

    const { open: openFileDialog, onChange } = useFileDialog({
      accept: "/*",
      multiple: true,
    });

    onChange(async (newFiles) => {
      isUploading.value = true;
      uploadProgress.value = { loaded: 0, total: 0, percent: 0 };
      await store.dispatch("putProductFiles", {
        files: newFiles,
        productId,
        onProgress: (progress) => {
          uploadProgress.value = progress;
        },
        onSuccess: (response) => {
          displaySonnerSuccess("Файлы успешно загружены.");
          files.value = files.value.concat(response.data.value);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isUploading.value = false;
      uploadProgress.value = { loaded: 0, total: 0, percent: 0 };
    });

    async function deleteFile(id) {
      await store.dispatch("deleteProductFile", {
        id,
        productId,
        onSuccess: () => {
          displaySonnerSuccess("Файл успешно удалён.");
          const fileIndex = files.value.findIndex((f) => f.id == id);
          if (fileIndex > -1) {
            files.value.splice(fileIndex, 1);
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    async function saveImageOrder(orderedFiles) {
      isSavingOrder.value = true;
      await store.dispatch("postProductImages", {
        productId,
        files: orderedFiles.map((f) => f.id),
        onSuccess: () => {
          displaySonnerSuccess("Порядок изображений успешно сохранён.");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isSavingOrder.value = false;
    }

    async function handleDeleteProduct() {
      await store.dispatch("deleteProduct", {
        productId,
        onSuccess: (response) => {
          router.push(response.data.value.next);
          displaySonnerSuccess("Товар успешно удалён.");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    function formatBytes(bytes, decimals = 2) {
      if (!+bytes) return "0 Bytes";
      const k = 1024;
      const dm = decimals < 0 ? 0 : decimals;
      const sizes = ["Bytes", "KB", "MB", "GB", "TB"];
      const i = Math.floor(Math.log(bytes) / Math.log(k));
      return `${parseFloat((bytes / Math.pow(k, i)).toFixed(dm))} ${sizes[i]}`;
    }

    async function addNote() {
      await store.dispatch("postNote", {
        productId,
        input: {
          text: "",
          importance: "low",
        },
        onSuccess: (response) => {
          notes.value.push(response.data.value);
          displaySonnerSuccess("Новая заметка добавлена.");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    function updateAttributeValues(newValues) {
      attributeValues.value = newValues;
    }

    function updateStoreAttributeValues(newValues) {
      storeAttributeValues.value = newValues;
    }

    function openSkuDialog() {
      newSkuValue.value = "";
      showSkuDialog.value = true;
    }

    async function submitSkuChange() {
      const trimmed = newSkuValue.value.trim();
      if (!trimmed || trimmed === productSKU.value) return;

      isUpdatingSku.value = true;
      await store.dispatch("updateProductSku", {
        productId,
        newSku: trimmed,
        onSuccess: (response) => {
          const data = response.data.value;
          productSKU.value = data.newSku;
          showSkuDialog.value = false;

          // Show marketplace results
          const mpResults = data.marketplaceResults || [];
          const failed = mpResults.filter((r) => !r.success);

          if (failed.length > 0) {
            const details = failed.map((r) => `${r.name}: ${r.error}`).join("\n");
            displaySonnerSuccess(
              `Артикул изменён локально. Ошибки на маркетплейсах (${failed.length}):\n${details}`,
            );
          } else if (mpResults.length > 0) {
            displaySonnerSuccess(
              `Артикул изменён локально и на ${mpResults.length} маркетплейс(ах)`,
            );
          } else {
            displaySonnerSuccess("Артикул изменён");
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isUpdatingSku.value = false;
    }

    return {
      productSKU,
      form,
      onSubmit,
      isSubmitting,
      isProductLoading,
      files,
      openFileDialog,
      isUploading,
      uploadProgress,
      deleteFile,
      productId,
      saveImageOrder,
      images,
      isSavingOrder,
      handleDeleteProduct,
      notes,
      addNote,
      category,
      attributeValues,
      storeAttributes,
      storeAttributeValues,
      isLoadingStoreAttributes,
      formatBytes,
      updateAttributeValues,
      updateStoreAttributeValues,
      productGroup,
      isLoadingGroup,
      categoryAttributeErrors,
      storeAttributeErrors,
      lastSavedAt,
      hasUnsavedChanges,
      fetchProduct,
      showSkuDialog,
      newSkuValue,
      isUpdatingSku,
      openSkuDialog,
      submitSkuChange,
    };
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar>
      <template #center>
        <Tabs v-model="tab">
          <TabsList>
            <TabsTrigger value="properties">Свойства</TabsTrigger>
            <TabsTrigger value="notes">Заметки</TabsTrigger>
            <TabsTrigger value="files">Файлы</TabsTrigger>
            <TabsTrigger value="images">Изображения</TabsTrigger>
            <TabsTrigger value="marketplaces">Маркетплейсы</TabsTrigger>
          </TabsList>
        </Tabs>
      </template>
    </NavigationBar>
    <Breadcrumbs :items="breadcrumbs" />
    <div class="h-full w-full grow flex gap-4 mb-4 overflow-hidden">
      <!-- Main content -->
      <div class="border rounded-lg grow flex flex-col overflow-auto">
        <div v-if="!isProductLoading" class="grow flex flex-col">
          <form
            v-if="form"
            v-show="tab == 'properties'"
            @submit.prevent="onSubmit"
            class="space-y-4 flex flex-col m-4"
          >
            <!-- SKU with edit button -->
            <div class="space-y-2">
              <Label class="opacity-50">Артикул</Label>
              <div class="flex items-center gap-1.5">
                <Input type="text" disabled :model-value="productSKU" />
                <Button
                  variant="outline"
                  size="icon"
                  class="shrink-0 size-9"
                  title="Изменить артикул"
                  type="button"
                  @click="openSkuDialog"
                >
                  <Pencil class="size-4" />
                </Button>
              </div>
            </div>

            <!-- SKU change dialog -->
            <AlertDialog :open="showSkuDialog" @update:open="showSkuDialog = $event">
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Изменить артикул</AlertDialogTitle>
                  <AlertDialogDescription>
                    Артикул будет изменён локально и на всех привязанных маркетплейсах.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <div class="space-y-3 py-2">
                  <div class="space-y-1">
                    <Label class="text-sm opacity-60">Текущий артикул</Label>
                    <Input type="text" disabled :model-value="productSKU" />
                  </div>
                  <div class="space-y-1">
                    <Label class="text-sm">Новый артикул</Label>
                    <Input
                      type="text"
                      v-model="newSkuValue"
                      maxlength="50"
                      placeholder="Введите новый артикул"
                      @keydown.enter.prevent="submitSkuChange"
                    />
                  </div>
                </div>
                <AlertDialogFooter>
                  <AlertDialogCancel :disabled="isUpdatingSku">Отмена</AlertDialogCancel>
                  <Button
                    @click="submitSkuChange"
                    :disabled="isUpdatingSku || !newSkuValue.trim() || newSkuValue.trim() === productSKU"
                  >
                    <Loader2 v-if="isUpdatingSku" class="size-4 mr-2 animate-spin" />
                    Изменить
                  </Button>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>

            <!-- Barcode -->
            <FormField v-slot="{ value, setValue }" name="product.barcode">
              <FormItem>
                <FormLabel>Штрихкод (EAN)</FormLabel>
                <FormControl>
                  <BarcodeInput
                    :model-value="value || ''"
                    @update:model-value="setValue($event)"
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            </FormField>

            <!-- Category (read-only) -->
            <div v-if="category" class="space-y-2">
              <Label class="opacity-50">Категория</Label>
              <div class="flex items-center gap-2 h-9 px-3 border rounded-md bg-muted/50">
                <FolderOpen class="size-4" />
                <span class="text-sm">{{ category.name }}</span>
              </div>
            </div>

            <!-- Product Group -->
            <div v-if="productGroup" class="border rounded-lg p-4 bg-muted/30">
              <div class="flex items-center justify-between mb-3">
                <div class="flex items-center gap-2">
                  <Layers class="size-4 text-primary" />
                  <span class="font-medium">{{ productGroup.name }}</span>
                </div>
                <router-link
                  :to="`/product-groups/${productGroup.id}/edit`"
                  class="text-xs text-muted-foreground hover:text-foreground flex items-center gap-1"
                >
                  Редактировать
                  <ExternalLink class="size-3" />
                </router-link>
              </div>
              <div v-if="productGroup.otherMembers && productGroup.otherMembers.length > 0" class="flex gap-2 overflow-x-auto pb-1 thin-scrollbar">
                <router-link
                  v-for="member in productGroup.otherMembers"
                  :key="member.productId"
                  :to="`/products/${member.productId}/edit`"
                  class="flex flex-col items-center gap-1.5 p-2 rounded-lg hover:bg-accent/50 transition-colors shrink-0 w-24"
                >
                  <img
                    v-if="member.imageUrl"
                    :src="member.imageUrl"
                    class="size-16 rounded-lg object-cover"
                    :alt="member.productName"
                  />
                  <div v-else class="size-16 rounded-lg bg-muted flex items-center justify-center">
                    <Layers class="size-6 text-muted-foreground" />
                  </div>
                  <span class="text-xs text-center text-muted-foreground truncate w-full">{{ member.productSku }}</span>
                </router-link>
              </div>
              <p v-else class="text-sm text-muted-foreground">Нет других товаров в группе</p>
            </div>

            <FormField v-slot="{ componentField }" name="product.name">
            <FormItem>
              <FormLabel>Название *</FormLabel>
              <FormControl>
                <Input type="text" v-bind="componentField" />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

          <FormField v-slot="{ componentField }" name="product.description">
            <FormItem>
              <FormLabel>Описание</FormLabel>
              <FormControl>
                <Textarea class="max-h-96 h-32" v-bind="componentField" />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

          <!-- Price and Quantity in a row -->
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <FormField v-slot="{ value, setValue }" name="product.storageQuantity">
              <FormItem>
                <FormLabel>Количество на складе</FormLabel>
                <FormControl>
                  <Input
                    type="number"
                    :model-value="value"
                    @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)"
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            </FormField>

            <FormField v-slot="{ value, setValue }" name="product.price">
              <FormItem>
                <FormLabel>Цена (₽)</FormLabel>
                <FormControl>
                  <Input
                    type="number"
                    step="0.01"
                    min="0"
                    placeholder="0.00"
                    :model-value="value"
                    @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)"
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            </FormField>
          </div>

          <!-- Category Attributes (collapsible) -->
          <details v-if="category" class="border rounded-lg" open>
            <summary class="px-4 py-3 cursor-pointer hover:bg-muted/50 transition-colors font-medium flex items-center gap-2">
              <Layers class="size-4" />
              Атрибуты категории
              <span v-if="category.attributes?.length" class="text-muted-foreground text-sm font-normal">
                ({{ category.attributes.length }})
              </span>
            </summary>
            <div class="px-4 pb-4">
              <p v-if="!category.attributes || category.attributes.length === 0" class="text-sm text-muted-foreground py-2">
                У этой категории нет атрибутов
              </p>
              <ProductAttributeForm
                v-else
                :attributes="category.attributes"
                :model-value="attributeValues"
                :errors="categoryAttributeErrors"
                @update:model-value="updateAttributeValues"
              />
            </div>
          </details>

          <!-- Store Attributes (collapsible) -->
          <details v-if="storeAttributes.length > 0" class="border rounded-lg" open>
            <summary class="px-4 py-3 cursor-pointer hover:bg-muted/50 transition-colors font-medium flex items-center gap-2">
              <Globe class="size-4" />
              Глобальные атрибуты
              <span class="text-muted-foreground text-sm font-normal">
                ({{ storeAttributes.length }})
              </span>
            </summary>
            <div class="px-4 pb-4">
              <StoreAttributeForm
                :attributes="storeAttributes"
                :model-value="storeAttributeValues"
                :errors="storeAttributeErrors"
                @update:model-value="updateStoreAttributeValues"
              />
            </div>
          </details>

          <!-- Actions row: Delete on left, Save on right -->
          <div class="flex items-center justify-between mt-6 pt-4 border-t">
            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button variant="destructive" type="button">
                  <Trash2 class="size-4 mr-2" />
                  Удалить
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>
                    Вы уверены что хотите удалить товар {{ productSKU }}?
                  </AlertDialogTitle>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Назад</AlertDialogCancel>
                  <AlertDialogAction @click="handleDeleteProduct">
                    Подтвердить
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>

            <div class="flex items-center gap-4">
              <!-- Save status indicator -->
              <div v-if="lastSavedAt" class="text-sm text-muted-foreground flex items-center gap-2">
                <Check class="size-4 text-green-500" />
                <span>Сохранено</span>
              </div>
              <div v-else-if="hasUnsavedChanges && !isSubmitting" class="text-sm text-muted-foreground">
                Несохранённые изменения...
              </div>

              <Button
                type="submit"
                size="lg"
                :disabled="isSubmitting"
              >
                <template v-if="!isSubmitting">
                  <Save class="size-4 mr-2" />
                  Сохранить
                </template>
                <Loader2 v-else class="animate-spin" />
              </Button>
            </div>
          </div>
        </form>

        <!-- Files tab -->
        <div v-show="tab == 'files'" class="flex flex-col">
          <ProductFilesTable
            :files="files"
            :productId="productId"
            @onDelete="deleteFile"
          />
          <div class="flex flex-col gap-3 p-4">
            <!-- Upload progress -->
            <div v-if="isUploading" class="space-y-2">
              <div class="flex justify-between text-sm text-muted-foreground">
                <span>Загрузка файлов...</span>
                <span>{{ uploadProgress.percent }}%</span>
              </div>
              <Progress :model-value="uploadProgress.percent" />
              <div class="text-xs text-muted-foreground text-right">
                {{ formatBytes(uploadProgress.loaded) }} / {{ formatBytes(uploadProgress.total) }}
              </div>
            </div>
            <div class="flex">
              <Button
                class="ml-auto"
                @click="openFileDialog"
                :disabled="isUploading"
              >
                <template v-if="!isUploading">
                  <Upload class="size-4 mr-2" />
                  Загрузить
                </template>
                <Loader2 v-else class="animate-spin" />
              </Button>
            </div>
          </div>
        </div>

        <!-- Images tab -->
        <div v-show="tab == 'images'" class="flex flex-col grow">
          <ProductImagesEditor
            :isSaving="isSavingOrder"
            :files="files"
            :images="images"
            :productId="productId"
            @onOrderSave="saveImageOrder"
          />
        </div>

        <!-- Marketplaces tab -->
        <div v-show="tab == 'marketplaces'" class="flex flex-col grow">
          <ProductMarketplaceIntegrations :productId="productId" @product-updated="fetchProduct" />
        </div>

        <!-- Notes tab -->
        <div v-show="tab == 'notes'" class="flex flex-col grow">
          <div class="p-4 flex flex-col gap-4">
            <div v-if="notes.length === 0" class="text-center py-8 text-muted-foreground">
              <NotebookPen class="size-12 mx-auto mb-3 opacity-50" />
              <p>Нет заметок</p>
              <p class="text-sm">Добавьте заметку для этого товара</p>
            </div>
            <div v-for="(note, index) in notes" :key="note.id || index">
              <Note
                v-model:note="notes[index]"
                :productId="productId"
                @onDelete="notes.splice(index, 1)"
              />
            </div>
          </div>
          <div class="mt-auto p-4 border-t">
            <Button size="lg" variant="outline" class="w-full" @click="addNote">
              <NotebookPen class="size-4 mr-2" />
              Добавить заметку
            </Button>
          </div>
        </div>
      </div>

        <!-- Loading state -->
        <div
          v-else-if="isProductLoading"
          class="grow flex items-center justify-center"
        >
          <Loader2 class="text-primary size-8 animate-spin" />
        </div>
      </div>

    </div>
  </Page>
</template>
