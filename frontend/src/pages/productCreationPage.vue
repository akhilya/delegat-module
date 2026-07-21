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
  listMissingRequiredAttributes,
} from "@/lib/productAttributeValidation";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { t } from "@/i18n";
import { http } from "@/main";
import { confirm } from "@/composables/useConfirm";
import AiDescriptionGenerator from "@/components/ai/AiDescriptionGenerator.vue";
import { useSelectedStore } from "@/composables/useSelectedStore";
import { formatEquivalents } from "@/lib/currency";
import { computeDiscountPercent } from "@/lib/productPrice";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  NumberField,
  NumberFieldContent,
  NumberFieldDecrement,
  NumberFieldIncrement,
  NumberFieldInput,
} from "@/components/ui/number-field";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";

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
    AiDescriptionGenerator,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    NumberField,
    NumberFieldContent,
    NumberFieldDecrement,
    NumberFieldIncrement,
    NumberFieldInput,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
  },
  data() {
    return {
      PAGE_PRODUCTS,
      breadcrumbs: [
        { label: this.$t("routes.products"), to: PAGE_PRODUCTS },
        { label: this.$t("pages.productCreationPage.breadcrumbTitle") },
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

    const {
      extendedPriceParameters,
      storeCurrencies,
      storeBaseCurrency,
      loadMemberships,
    } = useSelectedStore();

    const priceField = z.number().min(0).optional().nullable();
    const formSchema = computed(() => toTypedSchema(
      z.object({
        product: z.object({
          sku: z.string().min(1, t("pages.productCreationPage.validation.skuRequired")).max(20),
          barcode: z.string().max(13).optional().nullable(),
          name: z.string().min(1, t("pages.productCreationPage.validation.nameRequired")).max(100),
          description: z.string().max(5000).default(""),
          storageQuantity: z
            .number()
            .int()
            .min(0, t("pages.productCreationPage.validation.quantityNonNegative"))
            .optional()
            .nullable(),
          price: priceField,
          ...(extendedPriceParameters.value ? {
            oldPrice: priceField,
            minPrice: priceField,
            netPrice: priceField,
            discount: z.number().int().min(0).max(99).optional().nullable(),
            currency: z.string().max(3).optional().nullable(),
          } : {}),
        }),
        categoryAttributes: buildAttributesSchema(visibleCategoryAttributes.value),
        storeAttributes: buildAttributesSchema(visibleStoreAttributes.value),
      }),
    ));

    const form = useForm({
      validationSchema: formSchema,
      keepValuesOnUnmount: true,
      initialValues: {
        product: {
          sku: "",
          barcode: "",
          name: "",
          description: "",
          storageQuantity: null,
          price: null,
          oldPrice: null,
          minPrice: null,
          netPrice: null,
          discount: null,
          currency: null,
        },
        categoryAttributes: {},
        storeAttributes: {},
      },
    });

    const saveDisabledReasons = computed(() => {
      const reasons = [];
      if (!selectedCategoryId.value) {
        reasons.push(t("pages.productCreationPage.saveDisabled.categoryRequired"));
      }
      if (!String(form.values.product?.sku ?? "").trim()) {
        reasons.push(t("pages.productCreationPage.saveDisabled.skuRequired"));
      }
      if (!String(form.values.product?.name ?? "").trim()) {
        reasons.push(t("pages.productCreationPage.saveDisabled.nameRequired"));
      }
      for (const attr of listMissingRequiredAttributes(
        visibleCategoryAttributes.value,
        form.values.categoryAttributes,
      )) {
        reasons.push(
          t("pages.productCreationPage.saveDisabled.attributeRequired", {
            name: attr.name,
          }),
        );
      }
      for (const attr of listMissingRequiredAttributes(
        visibleStoreAttributes.value,
        form.values.storeAttributes,
      )) {
        reasons.push(
          t("pages.productCreationPage.saveDisabled.attributeRequired", {
            name: attr.name,
          }),
        );
      }
      return reasons;
    });

    const isSaveDisabled = computed(
      () => isSubmitting.value || saveDisabledReasons.value.length > 0,
    );

    const productGroups = ref([]);
    const selectedGroupId = ref(null);
    const isLoadingGroups = ref(false);
    const descriptionRef = ref(null);
    const sortedImages = ref([]);
    const galleryFileInput = ref(null);
    const isUploadingGallery = ref(false);
    const groupDetail = ref(null);
    const isDescriptionGenerating = ref(false);
    const descriptionGeneratingHint = computed(() =>
      form.values.product?.description?.trim()
        ? t("ai.descriptionImprovingHint")
        : t("ai.descriptionGeneratingHint"),
    );
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
        title: t("pages.productCreationPage.removePhotoConfirm.title"),
        description: t("pages.productCreationPage.removePhotoConfirm.description"),
        confirmLabel: t("pages.productCreationPage.removePhotoConfirm.confirmLabel"),
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
      if (mod100 >= 11 && mod100 <= 14) return t("pages.productCreationPage.attributesCount.formMany");
      if (mod10 === 1) return t("pages.productCreationPage.attributesCount.formOne");
      if (mod10 >= 2 && mod10 <= 4) return t("pages.productCreationPage.attributesCount.formFew");
      return t("pages.productCreationPage.attributesCount.formMany");
    }

    function pricePreview(amount) {
      return formatEquivalents(amount, storeCurrencies.value, storeBaseCurrency.value);
    }

    function syncDiscountFromPrices() {
      if (!extendedPriceParameters.value) return;
      form.setFieldValue(
        "product.discount",
        computeDiscountPercent(
          form.values.product?.oldPrice,
          form.values.product?.price,
        ),
      );
    }

    watch(
      () => [form.values.product?.oldPrice, form.values.product?.price],
      () => syncDiscountFromPrices(),
    );

    const aiAttributesSummary = computed(() => {
      const lines = [];
      for (const attr of visibleCategoryAttributes.value) {
        const raw = form.values.categoryAttributes?.[String(attr.id)];
        if (raw != null && raw !== "") lines.push(`${attr.name}: ${raw}`);
      }
      for (const attr of visibleStoreAttributes.value) {
        const raw = form.values.storeAttributes?.[String(attr.id)];
        if (raw != null && raw !== "") lines.push(`${attr.name}: ${raw}`);
      }
      return lines.join("\n");
    });

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
      { value: GROUP_NONE, label: t("pages.productCreationPage.fields.group.noGroupOption") },
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
    loadMemberships();
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

    const onSubmit = form.handleSubmit(
      async (values) => {
        if (!selectedCategoryId.value) {
          categoryError.value = t("pages.productCreationPage.validation.categoryRequired");
          displaySonnerError(t("pages.productCreationPage.validation.categoryRequiredToast"));
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
              let imagesFailed = false;
              let successMessage = t("pages.productCreationPage.toast.createdSuccess");

              if (sortedImages.value.length > 0) {
                const imagesOk = await persistPendingImages(createdProduct.id);
                sortedImages.value.forEach(revokeImagePreview);
                sortedImages.value = [];
                if (!imagesOk) {
                  imagesFailed = true;
                  successMessage = t("pages.productCreationPage.toast.createdImagesFailed");
                }
              }

              if (selectedGroupId.value && createdProduct.id) {
                await store.dispatch("addProductsToGroup", {
                  groupId: selectedGroupId.value,
                  productIds: [createdProduct.id],
                  onSuccess: () => {
                    if (!imagesFailed) {
                      displaySonnerSuccess(t("pages.productCreationPage.toast.createdAndAddedToGroup"));
                    } else {
                      displaySonnerSuccess(t("pages.productCreationPage.toast.addedToGroupSuffix", { message: successMessage }));
                    }
                  },
                  onError: () => {
                    displaySonnerSuccess(t("pages.productCreationPage.toast.addToGroupFailedSuffix", { message: successMessage }));
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
      },
    );

    return {
      form,
      onSubmit,
      isSubmitting,
      isSaveDisabled,
      saveDisabledReasons,
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
      isDescriptionGenerating,
      descriptionGeneratingHint,
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
      extendedPriceParameters,
      storeCurrencies,
      storeBaseCurrency,
      pricePreview,
      aiAttributesSummary,
    };
  },
};
</script>

<template>
  <Page class="!min-h-0">
    <NavigationBar />
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
          <div class="relative min-w-0 flex-1">
            <form
              id="product-create-form"
              novalidate
              @submit.prevent="onSubmit"
              class="flex min-w-0 flex-col"
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

              <div class="flex w-full min-w-0 flex-col gap-6">
                <Card class="border-0 shadow-none">
                  <CardHeader>
                    <CardTitle class="text-base">{{ $t("pages.productCreationPage.sections.mainData.title") }}</CardTitle>
                    <CardDescription>
                      {{ $t("pages.productCreationPage.sections.mainData.description") }}
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <FieldGroup class="gap-5">
                      <SettingsFormField
                        :label="$t('pages.productCreationPage.fields.category.label')"
                        :error="categoryError"
                        required
                      >
                        <SearchableSelect
                          :model-value="currentCategorySelectValue"
                          :options="categorySelectOptions"
                          :placeholder="$t('pages.productCreationPage.fields.category.placeholder')"
                          :disabled="isLoadingCategories || isSubmitting"
                          :invalid="!!categoryError"
                          @update:model-value="onCategorySelectChange"
                        />
                        <p
                          v-if="categories.length === 0 && !isLoadingCategories"
                          class="text-xs text-muted-foreground mt-1.5"
                        >
                          {{ $t("pages.productCreationPage.fields.category.emptyHint") }}
                        </p>
                      </SettingsFormField>

                      <SettingsFormField
                        v-if="productGroups.length > 0"
                        :label="$t('pages.productCreationPage.fields.group.label')"
                      >
                        <div class="space-y-1.5">
                          <SearchableSelect
                            :model-value="currentGroupSelectValue"
                            :options="groupSelectOptions"
                            :placeholder="$t('pages.productCreationPage.fields.group.placeholder')"
                            :disabled="isLoadingGroups || isSubmitting"
                            @update:model-value="onGroupSelectChange"
                          />
                          <router-link
                            v-if="groupDetail"
                            :to="`/product-groups/${groupDetail.id}/edit`"
                            class="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
                          >
                            {{ $t("pages.productCreationPage.fields.group.editLink") }}
                          </router-link>
                        </div>
                      </SettingsFormField>

                      <FormField v-slot="{ componentField, errorMessage }" name="product.sku">
                        <FormItem class="gap-0">
                          <SettingsFormField :label="$t('pages.productCreationPage.fields.sku.label')" label-for="product-sku" :error="errorMessage">
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
                          <SettingsFormField :label="$t('pages.productCreationPage.fields.barcode.label')" label-for="product-barcode" :error="errorMessage">
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
                          <SettingsFormField :label="$t('pages.productCreationPage.fields.name.label')" label-for="product-name" :error="errorMessage" required>
                            <FormControl>
                              <Input id="product-name" type="text" v-bind="componentField" />
                            </FormControl>
                          </SettingsFormField>
                        </FormItem>
                      </FormField>

                      <FormField v-slot="{ componentField, errorMessage }" name="product.description">
                        <FormItem class="gap-1.5">
                          <div class="flex items-center justify-between gap-2">
                            <label for="product-description" class="text-sm font-medium leading-none">{{ $t("pages.productCreationPage.fields.description.label") }}</label>
                            <AiDescriptionGenerator
                              :product-name="form.values.product?.name"
                              :category-name="selectedCategory?.name"
                              :existing-description="form.values.product?.description"
                              :attributes-summary="aiAttributesSummary"
                              @update:generating="isDescriptionGenerating = $event"
                              @generated="(text) => { form.setFieldValue('product.description', text); nextTick(() => autoResizeDescription()); }"
                            />
                          </div>
                          <div class="relative">
                            <FormControl>
                              <Textarea
                                id="product-description"
                                ref="descriptionRef"
                                v-bind="componentField"
                                :disabled="isDescriptionGenerating"
                                :aria-busy="isDescriptionGenerating"
                                @input="autoResizeDescription"
                                class="min-h-24 overflow-hidden resize-none transition-opacity"
                                :class="isDescriptionGenerating ? 'opacity-60' : ''"
                              />
                            </FormControl>
                            <div
                              v-if="isDescriptionGenerating"
                              class="pointer-events-none absolute inset-0 flex items-center justify-center rounded-md bg-background/55"
                            >
                              <p class="flex items-center gap-2 text-xs text-muted-foreground">
                                <Loader2 class="size-3.5 animate-spin" />
                                {{ descriptionGeneratingHint }}
                              </p>
                            </div>
                          </div>
                          <p v-if="errorMessage" class="text-sm text-destructive">{{ errorMessage }}</p>
                        </FormItem>
                      </FormField>

                      <template v-if="!selectedCategory?.hasSizes">
                        <div class="space-y-4 border-t pt-5">
                          <div>
                            <h4 class="text-sm font-medium">{{ $t("pages.productCreationPage.sections.stock.title") }}</h4>
                            <p class="text-xs text-muted-foreground mt-0.5">
                              {{ $t("pages.productCreationPage.sections.stock.description") }}
                            </p>
                          </div>
                          <div class="grid grid-cols-1 gap-5">
                            <FormField v-slot="{ value, setValue, errorMessage }" name="product.storageQuantity">
                              <FormItem class="gap-0">
                                <SettingsFormField
                                  :label="$t('pages.productCreationPage.fields.quantity.label')"
                                  label-for="product-quantity"
                                  :error="errorMessage"
                                >
                                  <NumberField
                                    id="product-quantity"
                                    class="w-full"
                                    :min="0"
                                    :step="1"
                                    :model-value="value ?? undefined"
                                    @update:model-value="(v) => setValue(v ?? null)"
                                  >
                                    <NumberFieldContent class="w-full">
                                      <NumberFieldDecrement />
                                      <FormControl>
                                        <NumberFieldInput />
                                      </FormControl>
                                      <NumberFieldIncrement />
                                    </NumberFieldContent>
                                  </NumberField>
                                </SettingsFormField>
                              </FormItem>
                            </FormField>
                          </div>
                        </div>

                        <div class="space-y-4 border-t pt-5">
                          <div>
                            <h4 class="text-sm font-medium">{{ $t("pages.productCreationPage.sections.price.title") }}</h4>
                            <p class="text-xs text-muted-foreground mt-0.5">
                              {{ $t("pages.productCreationPage.sections.price.description", { currency: storeBaseCurrency || $t("pages.productCreationPage.fields.price.defaultCurrencyFallback") }) }}
                            </p>
                          </div>
                          <div class="grid grid-cols-1 gap-5">
                            <FormField v-slot="{ value, setValue, errorMessage }" name="product.price">
                              <FormItem class="gap-0">
                                <SettingsFormField :label="$t('pages.productCreationPage.fields.price.label', { currency: storeBaseCurrency || $t('common.emDash') })" label-for="product-price" :error="errorMessage">
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
                                  <p v-if="pricePreview(value).length" class="text-xs text-muted-foreground mt-1">
                                    {{ pricePreview(value).join(" · ") }}
                                  </p>
                                </SettingsFormField>
                              </FormItem>
                            </FormField>
                          </div>
                        </div>

                        <div v-if="extendedPriceParameters" class="space-y-4 border-t pt-5">
                          <div>
                            <h4 class="text-sm font-medium">{{ $t("pages.productCreationPage.sections.extendedPrice.title") }}</h4>
                            <p class="text-xs text-muted-foreground mt-0.5">
                              {{ $t("pages.productCreationPage.sections.extendedPrice.description") }}
                            </p>
                          </div>
                          <div class="grid grid-cols-1 sm:grid-cols-2 gap-5">
                            <FormField v-slot="{ value, setValue, errorMessage }" name="product.oldPrice">
                              <FormItem class="gap-0">
                                <SettingsFormField
                                  :label="$t('pages.productCreationPage.fields.oldPrice.label', { currency: storeBaseCurrency || $t('common.emDash') })"
                                  :error="errorMessage"
                                  :description="$t('pages.productCreationPage.fields.oldPrice.description')"
                                >
                                  <FormControl>
                                    <Input type="number" step="0.01" min="0" :model-value="value" @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)" />
                                  </FormControl>
                                </SettingsFormField>
                              </FormItem>
                            </FormField>
                            <FormField v-slot="{ value, errorMessage }" name="product.discount">
                              <FormItem class="gap-0">
                                <SettingsFormField
                                  :label="$t('pages.productCreationPage.fields.discount.label')"
                                  :error="errorMessage"
                                  :description="$t('pages.productCreationPage.fields.discount.description')"
                                >
                                  <FormControl>
                                    <Input
                                      type="text"
                                      readonly
                                      class="bg-muted"
                                      :model-value="value != null ? String(value) : '-'"
                                    />
                                  </FormControl>
                                </SettingsFormField>
                              </FormItem>
                            </FormField>
                            <FormField v-slot="{ value, setValue, errorMessage }" name="product.minPrice">
                              <FormItem class="gap-0">
                                <SettingsFormField :label="$t('pages.productCreationPage.fields.minPrice.label', { currency: storeBaseCurrency || $t('common.emDash') })" :error="errorMessage">
                                  <FormControl>
                                    <Input type="number" step="0.01" min="0" :model-value="value" @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)" />
                                  </FormControl>
                                </SettingsFormField>
                              </FormItem>
                            </FormField>
                            <FormField v-slot="{ value, setValue, errorMessage }" name="product.netPrice">
                              <FormItem class="gap-0">
                                <SettingsFormField :label="$t('pages.productCreationPage.fields.netPrice.label', { currency: storeBaseCurrency || $t('common.emDash') })" :error="errorMessage">
                                  <FormControl>
                                    <Input type="number" step="0.01" min="0" :model-value="value" @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)" />
                                  </FormControl>
                                </SettingsFormField>
                              </FormItem>
                            </FormField>
                            <FormField v-slot="{ value, setValue, errorMessage }" name="product.currency">
                              <FormItem class="gap-0 sm:col-span-2">
                                <SettingsFormField
                                  :label="$t('pages.productCreationPage.fields.currency.label')"
                                  :error="errorMessage"
                                  :description="$t('pages.productCreationPage.fields.currency.description', { currency: storeBaseCurrency || $t('pages.productCreationPage.fields.currency.defaultCurrencyFallback') })"
                                >
                                  <FormControl>
                                    <Select :model-value="value || storeBaseCurrency" @update:model-value="setValue($event)" class="w-full">
                                      <SelectTrigger class="w-full"><SelectValue /></SelectTrigger>
                                      <SelectContent>
                                        <SelectItem v-for="c in storeCurrencies" :key="c.code" :value="c.code">{{ c.code }}</SelectItem>
                                      </SelectContent>
                                    </Select>
                                  </FormControl>
                                </SettingsFormField>
                              </FormItem>
                            </FormField>
                          </div>
                        </div>
                      </template>
                    </FieldGroup>
                  </CardContent>
                </Card>

                <Card
                  v-if="selectedCategory && visibleCategoryAttributes.length > 0"
                  class="border-0 shadow-none"
                >
                  <CardHeader>
                    <CardTitle class="text-base">{{ $t("pages.productCreationPage.sections.categoryAttributes.title") }}</CardTitle>
                    <CardDescription>
                      {{ $t("pages.productCreationPage.sections.categoryAttributes.description", {
                        count: visibleCategoryAttributes.length,
                        unit: attributeCountLabel(visibleCategoryAttributes.length),
                        category: selectedCategory.name,
                      }) }}
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
                    <CardTitle class="text-base">{{ $t("nav.globalAttributes") }}</CardTitle>
                    <CardDescription>
                      {{ $t("pages.productCreationPage.sections.storeAttributes.description", {
                        count: visibleStoreAttributes.length,
                        unit: attributeCountLabel(visibleStoreAttributes.length),
                      }) }}
                    </CardDescription>
                  </CardHeader>
                  <CardContent class="pt-0">
                    <StoreAttributeForm
                      :attributes="storeAttributes"
                    />
                  </CardContent>
                </Card>

                <div class="flex items-center justify-end pt-2 pb-6">
                  <TooltipProvider :delay-duration="200">
                    <Tooltip :disabled="!isSaveDisabled || saveDisabledReasons.length === 0">
                      <TooltipTrigger as-child>
                        <span
                          class="inline-flex"
                          :tabindex="isSaveDisabled ? 0 : undefined"
                        >
                          <Button
                            type="submit"
                            size="sm"
                            :disabled="isSaveDisabled"
                            class="disabled:opacity-100 disabled:bg-primary/70"
                          >
                            <Loader2 v-if="isSubmitting" class="size-4 mr-2 animate-spin" />
                            <Plus v-else class="size-4 mr-2" />
                            {{ $t("pages.productCreationPage.actions.createProduct") }}
                          </Button>
                        </span>
                      </TooltipTrigger>
                      <TooltipContent side="top" class="max-w-xs">
                        <ul class="list-disc space-y-1 pl-4 text-left">
                          <li v-for="reason in saveDisabledReasons" :key="reason">
                            {{ reason }}
                          </li>
                        </ul>
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </div>
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
  </Page>
</template>