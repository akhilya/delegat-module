<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import ProductAttributeForm from "@/components/products/productAttributeForm.vue";
import StoreAttributeForm from "@/components/products/storeAttributeForm.vue";
import ProductGroupSidebar from "@/components/products/ProductGroupSidebar.vue";
import ProductGallerySidebar from "@/components/products/ProductGallerySidebar.vue";
import { BarcodeInput } from "@/components/ui/barcode";
import { SearchableSelect } from "@/components/ui/searchable-select";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import {
  FormControl,
  FormField,
  FormItem,
} from "@/components/ui/form";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Loader2, Plus } from "lucide-vue-next";
import { PAGE_PRODUCTS } from "@/router";
import { useStore } from "vuex";
import { useRouter, useRoute } from "vue-router";
import { ref, watch, computed, nextTick, onUnmounted } from "vue";
import { toTypedSchema } from "@vee-validate/zod";
import { useForm } from "vee-validate";
import { z } from "zod";
import {
  attributesToFormRecord,
  buildAttributesSchema,
  formRecordToAttributeValues,
} from "@/lib/productAttributeValidation";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { http } from "@/main";
import { confirm } from "@/composables/useConfirm";

const GROUP_NONE = "__none__";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    FormControl,
    FormField,
    FormItem,

    Input,
    Textarea,
    ProductAttributeForm,
    StoreAttributeForm,
    ProductGroupSidebar,
    ProductGallerySidebar,
    BarcodeInput,
    SearchableSelect,
    SettingsFormField,
    FieldGroup,
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
    Loader2,
    Plus,
  },
  data() {
    return {
      PAGE_PRODUCTS,
      breadcrumbs: [
        { label: "Товары", to: PAGE_PRODUCTS },
        { label: "Создание товара" },
      ],
    };
  },
  setup() {
    const store = useStore();
    const router = useRouter();
    const route = useRoute();
    const isSubmitting = ref(false);
    const isLoadingCategories = ref(false);
    const categories = ref([]);
    const selectedCategoryId = ref(null);
    const selectedCategory = ref(null);
    const storeAttributes = ref([]);
    const isLoadingStoreAttributes = ref(false);
    const categoryError = ref("");
    const visibleCategoryAttributes = computed(
      () => selectedCategory.value?.attributes?.filter((a) => a.showOnProductPage !== false) ?? [],
    );
    const visibleStoreAttributes = computed(() =>
      storeAttributes.value.filter((a) => a.showOnProductPage !== false),
    );

    const formSchema = computed(() => toTypedSchema(
      z.object({
        product: z.object({
          sku: z.string().min(1, "Артикул обязателен").max(20),
          barcode: z.string().max(13).optional().nullable(),
          name: z.string().min(1, "Название обязательно").max(100),
          description: z.string().max(5000).default(""),
          storageQuantity: z
            .number()
            .int()
            .min(0, "Количество не может быть отрицательным")
            .optional()
            .nullable(),
          price: z.number().min(0, "Цена не может быть отрицательной").optional().nullable(),
        }),
        categoryAttributes: buildAttributesSchema(visibleCategoryAttributes.value),
        storeAttributes: buildAttributesSchema(visibleStoreAttributes.value),
      }),
    ));

    const form = useForm({
      validationSchema: formSchema,
      initialValues: {
        product: {
          sku: "",
          barcode: "",
          name: "",
          description: "",
          storageQuantity: null,
          price: null,
        },
        categoryAttributes: {},
        storeAttributes: {},
      },
    });

    const productGroups = ref([]);
    const selectedGroupId = ref(null);
    const isLoadingGroups = ref(false);
    const descriptionRef = ref(null);
    const sortedImages = ref([]);
    const galleryFileInput = ref(null);
    const isUploadingGallery = ref(false);
    const groupDetail = ref(null);
    let pendingImageId = 0;

    function revokeImagePreview(img) {
      if (img?.previewUrl) URL.revokeObjectURL(img.previewUrl);
    }

    function reindexGalleryImages() {
      sortedImages.value.forEach((img, idx) => {
        img.index = idx;
      });
    }

    function triggerGalleryUpload() {
      galleryFileInput.value?.click();
    }

    function handleGalleryUpload(event) {
      const fileList = event.target.files;
      if (!fileList?.length) return;

      for (const file of fileList) {
        pendingImageId += 1;
        sortedImages.value.push({
          fileId: `pending-${pendingImageId}`,
          localFile: file,
          previewUrl: URL.createObjectURL(file),
          index: sortedImages.value.length,
        });
      }
      reindexGalleryImages();

      if (galleryFileInput.value) galleryFileInput.value.value = "";
    }

    function onGalleryDragChange() {
      reindexGalleryImages();
    }

    async function removeGalleryImage(index) {
      const ok = await confirm({
        title: "Убрать фото из списка?",
        description: "Изображение не будет добавлено к товару.",
        confirmLabel: "Убрать",
      });
      if (!ok) return;

      const [removed] = sortedImages.value.splice(index, 1);
      revokeImagePreview(removed);
      reindexGalleryImages();
    }

    function openGalleryPreview(index) {
      const img = sortedImages.value[index];
      if (!img?.previewUrl) return;
      window.open(img.previewUrl, "_blank", "noopener,noreferrer");
    }

    async function persistPendingImages(productId) {
      if (!sortedImages.value.length) return true;

      try {
        const fileIds = [];
        for (const img of sortedImages.value) {
          if (!img.localFile) continue;

          const formData = new FormData();
          formData.append("files", img.localFile);
          const response = await http({
            url: `/products/${productId}/files`,
            method: "PUT",
            data: formData,
            headers: { "Content-Type": "multipart/form-data" },
            timeout: 60 * 60 * 1000,
          });

          const uploaded = Array.isArray(response.data) ? response.data : [response.data];
          fileIds.push(...uploaded.map((f) => f.id));
        }

        if (!fileIds.length) return true;

        let ok = false;
        await store.dispatch("postProductImages", {
          productId,
          files: fileIds,
          onSuccess: () => {
            ok = true;
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
        return ok;
      } catch (error) {
        displaySonnerError(error);
        return false;
      }
    }

    onUnmounted(() => {
      sortedImages.value.forEach(revokeImagePreview);
    });

    function attributeCountLabel(n) {
      const mod10 = n % 10;
      const mod100 = n % 100;
      if (mod100 >= 11 && mod100 <= 14) return "атрибутов";
      if (mod10 === 1) return "атрибут";
      if (mod10 >= 2 && mod10 <= 4) return "атрибута";
      return "атрибутов";
    }

    function autoResizeDescription() {
      const el = descriptionRef.value?.$el ?? descriptionRef.value;
      if (!el || el.tagName !== "TEXTAREA") return;
      el.style.height = "auto";
      el.style.height = `${el.scrollHeight}px`;
    }

    const categorySelectOptions = computed(() =>
      categories.value.map((c) => ({
        value: String(c.id),
        label: c.name,
      })),
    );

    const currentCategorySelectValue = computed(() =>
      selectedCategoryId.value ? String(selectedCategoryId.value) : "",
    );

    const groupSelectOptions = computed(() => [
      { value: GROUP_NONE, label: "Без группы" },
      ...productGroups.value.map((g) => ({
        value: String(g.id),
        label: g.name,
      })),
    ]);

    const currentGroupSelectValue = computed(() =>
      selectedGroupId.value ? String(selectedGroupId.value) : GROUP_NONE,
    );

    function onCategorySelectChange(value) {
      selectedCategoryId.value = value ? parseInt(value) : null;
      categoryError.value = "";
    }

    function onGroupSelectChange(value) {
      selectedGroupId.value = value && value !== GROUP_NONE ? parseInt(value) : null;
    }

    async function fetchStoreAttributes() {
      isLoadingStoreAttributes.value = true;
      await store.dispatch("getAllStoreAttributes", {
        onSuccess: (response) => {
          storeAttributes.value = response.data.value || [];
          form.setFieldValue(
            "storeAttributes",
            attributesToFormRecord(
              storeAttributes.value,
              form.values.storeAttributes,
              "storeAttributeId",
            ),
          );
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isLoadingStoreAttributes.value = false;
    }

    async function fetchProductGroups() {
      isLoadingGroups.value = true;
      await store.dispatch("getAllGroups", {
        onSuccess: (response) => {
          productGroups.value = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isLoadingGroups.value = false;
    }

    async function fetchGroupDetail(groupId) {
      if (!groupId) {
        groupDetail.value = null;
        return;
      }
      await store.dispatch("getGroup", {
        id: groupId,
        onSuccess: (response) => {
          const group = response.data.value;
          groupDetail.value = {
            id: group.id,
            name: group.name,
            members:
              group.members?.map((m) => ({
                productId: m.product.id,
                productName: m.product.name || "",
                productSku: m.product.sku || "",
                imageUrl: m.product.images?.[0]?.url,
                imageThumbnailUrl: m.product.images?.[0]?.thumbnailUrl,
              })) || [],
          };
        },
        onError: () => {
          groupDetail.value = null;
        },
      });
    }

    const fromProductId = route.query.fromProduct;
    const fromGroupId = route.query.groupId;

    function getDuplicateUrl(sourceProductId) {
      const params = new URLSearchParams();
      params.set("fromProduct", String(sourceProductId));
      if (fromGroupId) params.set("groupId", String(fromGroupId));
      else if (selectedGroupId.value) params.set("groupId", String(selectedGroupId.value));
      return `/products/create?${params.toString()}`;
    }

    const duplicateProductUrl = computed(() => {
      if (!fromProductId) return null;
      return getDuplicateUrl(fromProductId);
    });

    watch(selectedGroupId, (newId) => {
      fetchGroupDetail(newId);
    });

    async function prefillFromProduct() {
      if (!fromProductId) return;
      await store.dispatch("getProduct", {
        productId: parseInt(fromProductId),
        onSuccess: async (response) => {
          const product = response.data.value;

          form.setValues({
            product: {
              sku: "",
              barcode: "",
              name: product.name || "",
              description: product.description || "",
              storageQuantity: product.storageQuantity ?? null,
              price: product.price ?? null,
            },
          });

          const catId = product.category?.id || product.categoryId;
          if (catId) {
            selectedCategoryId.value = catId;

            const unwatchCategory = watch(selectedCategory, (cat) => {
              if (cat) {
                nextTick(() => {
                  form.setFieldValue(
                    "categoryAttributes",
                    attributesToFormRecord(
                      cat.attributes,
                      product.attributeValues || [],
                      "attributeId",
                    ),
                  );
                  unwatchCategory();
                });
              }
            });
          }

          if (product.storeAttributeValues?.length) {
            form.setFieldValue(
              "storeAttributes",
              attributesToFormRecord(
                storeAttributes.value,
                product.storeAttributeValues,
                "storeAttributeId",
              ),
            );
          }

          if (fromGroupId) {
            selectedGroupId.value = parseInt(fromGroupId);
          }

          nextTick(() => autoResizeDescription());
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    async function fetchCategories() {
      isLoadingCategories.value = true;
      await store.dispatch("getAllCategories", {
        onSuccess: (response) => {
          categories.value = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isLoadingCategories.value = false;
    }

    fetchStoreAttributes();
    fetchProductGroups();
    if (fromGroupId) fetchGroupDetail(parseInt(fromGroupId));
    fetchCategories().then(() => prefillFromProduct());

    watch(selectedCategoryId, async (newVal) => {
      if (newVal) {
        await store.dispatch("getCategory", {
          id: newVal,
          onSuccess: (response) => {
            selectedCategory.value = response.data.value;
            form.setFieldValue(
              "categoryAttributes",
              attributesToFormRecord(
                selectedCategory.value.attributes,
                [],
                "attributeId",
              ),
            );
          },
          onError: (error) => {
            displaySonnerError(error);
            selectedCategory.value = null;
          },
        });
      } else {
        selectedCategory.value = null;
        form.setFieldValue("categoryAttributes", {});
      }
    });

    const onSubmit = form.handleSubmit(async (values) => {
      if (!selectedCategoryId.value) {
        categoryError.value = "Выберите категорию";
        displaySonnerError("Выберите категорию товара");
        return;
      }

      isSubmitting.value = true;

      const input = {
        ...values.product,
        categoryId: selectedCategoryId.value,
        attributeValues: formRecordToAttributeValues(values.categoryAttributes, "attributeId"),
        storeAttributeValues: formRecordToAttributeValues(values.storeAttributes, "storeAttributeId"),
      };

      try {
        if (sortedImages.value.length > 0) {
          isUploadingGallery.value = true;
        }

        await store.dispatch("createProduct", {
          input,
          onSuccess: async (response) => {
            const createdProduct = response.data.value;
            let successMessage = "Товар успешно создан.";

            if (sortedImages.value.length > 0) {
              const imagesOk = await persistPendingImages(createdProduct.id);
              sortedImages.value.forEach(revokeImagePreview);
              sortedImages.value = [];
              if (!imagesOk) {
                successMessage = "Товар создан, но не удалось загрузить фотографии.";
              }
            }

            if (selectedGroupId.value && createdProduct.id) {
              await store.dispatch("addProductsToGroup", {
                groupId: selectedGroupId.value,
                productIds: [createdProduct.id],
                onSuccess: () => {
                  if (successMessage === "Товар успешно создан.") {
                    displaySonnerSuccess("Товар создан и добавлен в группу.");
                  } else {
                    displaySonnerSuccess(`${successMessage} Товар добавлен в группу.`);
                  }
                },
                onError: () => {
                  displaySonnerSuccess(`${successMessage} Не удалось добавить в группу.`);
                },
              });
            } else {
              displaySonnerSuccess(successMessage);
            }

            router.push(createdProduct.next);
          },
          onError: (error) => {
            if (error?.response?.data?.errors) form.setErrors(error.response.data.errors);
            displaySonnerError(error);
          },
        });
      } catch (error) {
        displaySonnerError(error);
      } finally {
        isSubmitting.value = false;
        isUploadingGallery.value = false;
      }
    });

    return {
      form,
      onSubmit,
      isSubmitting,
      isLoadingCategories,
      categories,
      selectedCategoryId,
      selectedCategory,
      visibleCategoryAttributes,
      visibleStoreAttributes,
      attributeCountLabel,
      storeAttributes,
      isLoadingStoreAttributes,
      categoryError,

      productGroups,
      selectedGroupId,
      isLoadingGroups,
      descriptionRef,
      autoResizeDescription,
      groupDetail,
      getDuplicateUrl,
      duplicateProductUrl,
      categorySelectOptions,
      currentCategorySelectValue,
      onCategorySelectChange,
      groupSelectOptions,
      currentGroupSelectValue,
      onGroupSelectChange,
      sortedImages,
      galleryFileInput,
      isUploadingGallery,
      triggerGalleryUpload,
      handleGalleryUpload,
      onGalleryDragChange,
      removeGalleryImage,
      openGalleryPreview,
    };
  },
};
</script>

<template>
  <Page class="!min-h-0">
    <div class="fixed top-0 z-30 bg-background/95 backdrop-blur-sm left-0 right-0 md:left-[var(--sidebar-width)]">
      <div class="adaptive-padding">
        <NavigationBar :sticky="false" />
      </div>
    </div>
    <div class="h-[7rem] sm:h-14 shrink-0" aria-hidden="true" />
    <Breadcrumbs :items="breadcrumbs" />
    <PageSection :loading="isLoadingCategories && categories.length === 0" class="!min-h-0 !grow-0 mb-0">
      <input
        ref="galleryFileInput"
        type="file"
        accept="image/*"
        multiple
        class="hidden"
        @change="handleGalleryUpload"
      />
      <div class="w-full max-w-[90rem]">
        <div class="flex items-start gap-4 lg:gap-5">
          <ProductGroupSidebar
            :group="groupDetail"
            :disabled="isSubmitting"
            :add-url="duplicateProductUrl"
            :get-duplicate-url="getDuplicateUrl"
          />
          <div class="relative min-w-0 flex-1 overflow-hidden">
            <form
              id="product-create-form"
              novalidate
              @submit.prevent="onSubmit"
              class="flex min-w-0 flex-col overflow-hidden"
            >
              <ProductGallerySidebar
                v-model:images="sortedImages"
                layout="inline"
                class="lg:hidden mb-4"
                :is-uploading="isUploadingGallery"
                :disabled="isSubmitting"
                @drag-change="onGalleryDragChange"
                @remove="removeGalleryImage"
                @upload="triggerGalleryUpload"
                @preview="openGalleryPreview"
              />

              <div class="flex w-full min-w-0 flex-col gap-6 overflow-hidden">
                <Card class="border-0 shadow-none">
                  <CardHeader>
                    <CardTitle class="text-base">Основные данные</CardTitle>
                    <CardDescription>
                      Категория, артикул, штрихкод, название и цена. Количество и цена по размерам — если у категории
                      включены размеры.
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <FieldGroup class="gap-5">
                      <SettingsFormField
                        label="Категория"
                        :error="categoryError"
                        required
                      >
                        <SearchableSelect
                          :model-value="currentCategorySelectValue"
                          :options="categorySelectOptions"
                          placeholder="Выберите категорию..."
                          :disabled="isLoadingCategories || isSubmitting"
                          :invalid="!!categoryError"
                          @update:model-value="onCategorySelectChange"
                        />
                        <p
                          v-if="categories.length === 0 && !isLoadingCategories"
                          class="text-xs text-muted-foreground mt-1.5"
                        >
                          Сначала создайте категорию товаров
                        </p>
                      </SettingsFormField>

                      <SettingsFormField
                        v-if="productGroups.length > 0"
                        label="Группа товаров"
                      >
                        <div class="space-y-1.5">
                          <SearchableSelect
                            :model-value="currentGroupSelectValue"
                            :options="groupSelectOptions"
                            placeholder="Выберите группу..."
                            :disabled="isLoadingGroups || isSubmitting"
                            @update:model-value="onGroupSelectChange"
                          />
                          <router-link
                            v-if="groupDetail"
                            :to="`/product-groups/${groupDetail.id}/edit`"
                            class="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
                          >
                            Редактировать группу
                          </router-link>
                        </div>
                      </SettingsFormField>

                      <FormField v-slot="{ componentField, errorMessage }" name="product.sku">
                        <FormItem class="gap-0">
                          <SettingsFormField label="Артикул" label-for="product-sku" :error="errorMessage">
                            <FormControl>
                              <Input
                                id="product-sku"
                                type="text"
                                class="font-mono"
                                v-bind="componentField"
                              />
                            </FormControl>
                          </SettingsFormField>
                        </FormItem>
                      </FormField>

                      <FormField v-slot="{ value, setValue, errorMessage }" name="product.barcode">
                        <FormItem class="gap-0">
                          <SettingsFormField label="Штрихкод (EAN)" label-for="product-barcode" :error="errorMessage">
                            <FormControl>
                              <BarcodeInput
                                id="product-barcode"
                                :model-value="value || ''"
                                @update:model-value="setValue($event)"
                              />
                            </FormControl>
                          </SettingsFormField>
                        </FormItem>
                      </FormField>

                      <FormField v-slot="{ componentField, errorMessage }" name="product.name">
                        <FormItem class="gap-0">
                          <SettingsFormField label="Название" label-for="product-name" :error="errorMessage" required>
                            <FormControl>
                              <Input id="product-name" type="text" v-bind="componentField" />
                            </FormControl>
                          </SettingsFormField>
                        </FormItem>
                      </FormField>

                      <FormField v-slot="{ componentField, errorMessage }" name="product.description">
                        <FormItem class="gap-0">
                          <SettingsFormField label="Описание" label-for="product-description" :error="errorMessage">
                            <FormControl>
                              <Textarea
                                id="product-description"
                                ref="descriptionRef"
                                v-bind="componentField"
                                @input="autoResizeDescription"
                                class="min-h-24 overflow-hidden resize-none"
                              />
                            </FormControl>
                          </SettingsFormField>
                        </FormItem>
                      </FormField>

                      <div
                        v-if="!selectedCategory?.hasSizes"
                        class="grid grid-cols-1 sm:grid-cols-2 gap-5"
                      >
                        <FormField v-slot="{ value, setValue, errorMessage }" name="product.storageQuantity">
                          <FormItem class="gap-0">
                            <SettingsFormField
                              label="Количество на складе"
                              label-for="product-quantity"
                              :error="errorMessage"
                            >
                              <FormControl>
                                <Input
                                  id="product-quantity"
                                  type="number"
                                  :model-value="value"
                                  @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)"
                                />
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>

                        <FormField v-slot="{ value, setValue, errorMessage }" name="product.price">
                          <FormItem class="gap-0">
                            <SettingsFormField label="Цена (₽)" label-for="product-price" :error="errorMessage">
                              <FormControl>
                                <Input
                                  id="product-price"
                                  type="number"
                                  step="0.01"
                                  min="0"
                                  placeholder="0.00"
                                  :model-value="value"
                                  @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)"
                                />
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                      </div>
                    </FieldGroup>
                  </CardContent>
                </Card>

                <Card
                  v-if="selectedCategory && visibleCategoryAttributes.length > 0"
                  class="border-0 shadow-none"
                >
                  <CardHeader>
                    <CardTitle class="text-base">Атрибуты категории</CardTitle>
                    <CardDescription>
                      {{ visibleCategoryAttributes.length }}
                      {{ attributeCountLabel(visibleCategoryAttributes.length) }}.
                      Значения из настроек категории «{{ selectedCategory.name }}».
                    </CardDescription>
                  </CardHeader>
                  <CardContent class="pt-0">
                    <ProductAttributeForm
                      :attributes="selectedCategory.attributes"
                    />
                  </CardContent>
                </Card>

                <Card
                  v-if="visibleStoreAttributes.length > 0"
                  class="border-0 shadow-none"
                >
                  <CardHeader>
                    <CardTitle class="text-base">Глобальные атрибуты</CardTitle>
                    <CardDescription>
                      {{ visibleStoreAttributes.length }}
                      {{ attributeCountLabel(visibleStoreAttributes.length) }}.
                      Общие для всех товаров магазина.
                    </CardDescription>
                  </CardHeader>
                  <CardContent class="pt-0">
                    <StoreAttributeForm
                      :attributes="storeAttributes"
                    />
                  </CardContent>
                </Card>

                <div class="h-[4.75rem] shrink-0" aria-hidden="true" />
              </div>
            </form>
          </div>

          <ProductGallerySidebar
            v-model:images="sortedImages"
            :is-uploading="isUploadingGallery"
            :disabled="isSubmitting"
            @drag-change="onGalleryDragChange"
            @remove="removeGalleryImage"
            @upload="triggerGalleryUpload"
            @preview="openGalleryPreview"
          />
        </div>
      </div>
    </PageSection>

    <div class="fixed bottom-0 z-20 left-0 right-0 md:left-[var(--sidebar-width)] pointer-events-none pb-3">
      <div class="adaptive-padding pointer-events-none">
        <div class="w-full max-w-[90rem] flex items-stretch gap-4 lg:gap-5 pointer-events-none">
          <div class="w-96 shrink-0 hidden lg:block" aria-hidden="true" />
          <div class="flex-1 min-w-0 pointer-events-auto flex items-center justify-end gap-3">
              <Button
                type="submit"
                form="product-create-form"
                size="sm"
                :disabled="isSubmitting || !selectedCategoryId"
              >
                <Loader2 v-if="isSubmitting" class="size-4 mr-2 animate-spin" />
                <Plus v-else class="size-4 mr-2" />
                Создать товар
              </Button>
          </div>
          <div class="w-64 shrink-0 hidden lg:block" aria-hidden="true" />
        </div>
      </div>
    </div>
  </Page>
</template>