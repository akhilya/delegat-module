<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import {
  FormControl,
  FormField,
  FormItem,
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
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuTrigger,
} from "@/components/ui/context-menu";
import ProductFileManager from "@/components/products/ProductFileManager.vue";
import ProductImagesEditor from "@/components/productImagesEditor.vue";
import ProductAttributeForm from "@/components/products/productAttributeForm.vue";
import StoreAttributeForm from "@/components/products/storeAttributeForm.vue";
import ProductMarketplaceIntegrations from "@/components/products/productMarketplaceIntegrations.vue";
import ProductVariantsEditor from "@/components/products/ProductVariantsEditor.vue";
import { BarcodeInput } from "@/components/ui/barcode";
import { SearchableSelect } from "@/components/ui/searchable-select";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import Note from "@/components/note.vue";

import {
  Loader2,
  Save,
  Trash2,
  NotebookPen,
  Pencil,
  Plus,
  Copy,
  X,
  ChevronLeft,
  ChevronRight,
} from "lucide-vue-next";
import { PAGE_PRODUCTS, setPageTitle } from "@/router";
import { useStore } from "vuex";
import { useRoute, onBeforeRouteLeave } from "vue-router";
import ProductGroupSidebar from "@/components/products/ProductGroupSidebar.vue";
import ProductGallerySidebar from "@/components/products/ProductGallerySidebar.vue";
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from "vue";
import { toTypedSchema } from "@vee-validate/zod";
import { useForm } from "vee-validate";
import { z } from "zod";
import {
  attributesToFormRecord,
  buildAttributesSchema,
  categoryAttributeCacheToValues,
  formRecordToAttributeValues,
  listMissingRequiredAttributes,
  mergeCategoryAttributesIntoCache,
} from "@/lib/productAttributeValidation";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { applyAttributeUniquenessError } from "@/lib/attributeUniquenessError";
import { t } from "@/i18n";
import { http, router } from "@/main";
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
    AppPageChrome,
    PageSection,
    Button,
    FormControl,
    FormField,
    FormItem,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogTrigger,
    AlertDialogDescription,
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogFooter,
    DialogClose,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    Input,
    Textarea,
    ProductFileManager,
    ProductImagesEditor,
    ProductAttributeForm,
    StoreAttributeForm,
    ProductMarketplaceIntegrations,
    ProductVariantsEditor,
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
    Save,
    Trash2,
    NotebookPen,
    Pencil,
    Note,
    Plus,
    Copy,
    X,
    ChevronLeft,
    ChevronRight,
    ContextMenu,
    ContextMenuContent,
    ContextMenuItem,
    ContextMenuTrigger,
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
      tab: this.$route.query.tab ?? "properties",
    };
  },
  computed: {
    breadcrumbs() {
      const items = [{ label: this.$t("routes.products"), to: PAGE_PRODUCTS }];
      if (this.productSKU) {
        items.push({ label: this.productSKU });
      } else {
        items.push({ label: this.$t("pages.productEditPage.breadcrumbEditing") });
      }
      return items;
    },
  },
  watch: {
    tab(newVal) {
      this.$router.replace({ query: { ...this.$route.query, tab: newVal } });
      if (newVal === 'properties') {
        this.$nextTick(() => this.autoResizeDescription());
      }
    },
  },
  setup() {
    const store = useStore();
    const route = useRoute();
    const isSubmitting = ref(false);
    const isProductLoading = ref(false);
    const productSKU = ref("");
    const files = ref([]);
    const productId = computed(() => parseInt(String(route.params.id)));
    const images = ref(undefined);
    const notes = ref([]);
    const category = ref(null);
    const categories = ref([]);
    const selectedCategoryId = ref(null);
    const savedCategoryId = ref(null);
    const allCategoryAttributeValues = ref({});
    const isLoadingCategories = ref(false);
    const isChangingCategory = ref(false);
    const storeAttributes = ref([]);
    const loadedStoreAttributeValues = ref([]);
    const isLoadingStoreAttributes = ref(false);
    const aiEnabled = ref(false);
    const productGroup = ref(null);
    const isLoadingGroup = ref(true);
    const visibleCategoryAttributes = computed(() =>
      category.value?.attributes?.filter((a) => a.showOnProductPage !== false) ?? [],
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
          name: z.string().min(1, t("pages.productEditPage.validation.nameRequired")).max(100),
          description: z.string().max(5000).optional(),
          barcode: z.string().max(13).optional().nullable(),
          storageQuantity: z.number().int().min(0, t("pages.productEditPage.validation.quantityNonNegative")).optional().nullable(),
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
          name: "",
          description: "",
          barcode: "",
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

    function pricePreview(amount) {
      return formatEquivalents(amount, storeCurrencies.value, storeBaseCurrency.value);
    }
    const categorySelectOptions = computed(() =>
      categories.value.map((item) => ({
        value: String(item.id),
        label: item.name,
      })),
    );

    const currentCategorySelectValue = computed(() =>
      selectedCategoryId.value ? String(selectedCategoryId.value) : "",
    );

    function syncCategoryAttributesCacheFromForm() {
      allCategoryAttributeValues.value = mergeCategoryAttributesIntoCache(
        allCategoryAttributeValues.value,
        form.values.categoryAttributes,
      );
    }

    function applyCategoryAttributesToForm() {
      form.setFieldValue(
        "categoryAttributes",
        attributesToFormRecord(
          category.value?.attributes,
          categoryAttributeCacheToValues(allCategoryAttributeValues.value),
          "attributeId",
        ),
      );
    }

    async function fetchCategories() {
      isLoadingCategories.value = true;
      await store.dispatch("getAllCategories", {
        onSuccess: (response) => {
          categories.value = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isLoadingCategories.value = false;
    }

    async function onCategorySelectChange(value) {
      const nextCategoryId = value ? parseInt(value, 10) : null;
      if (!nextCategoryId || nextCategoryId === selectedCategoryId.value) return;

      syncCategoryAttributesCacheFromForm();
      isChangingCategory.value = true;

      await store.dispatch("getCategory", {
        id: nextCategoryId,
        onSuccess: (response) => {
          selectedCategoryId.value = nextCategoryId;
          category.value = response.data.value;
          applyCategoryAttributesToForm();
          markDirty();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });

      isChangingCategory.value = false;
    }

    function attributeCountLabel(n) {
      const mod10 = n % 10;
      const mod100 = n % 100;
      if (mod100 >= 11 && mod100 <= 14) return t("pages.productEditPage.attributesCount.formMany");
      if (mod10 === 1) return t("pages.productEditPage.attributesCount.formOne");
      if (mod10 >= 2 && mod10 <= 4) return t("pages.productEditPage.attributesCount.formFew");
      return t("pages.productEditPage.attributesCount.formMany");
    }
    const hasUnsavedChanges = ref(false);
    const isInitialLoad = ref(true);
    const isDeletingFile = ref(false);
    const showSkuDialog = ref(false);
    const newSkuValue = ref("");
    const isUpdatingSku = ref(false);
    const allGroups = ref([]);
    const isChangingGroup = ref(false);
    const descriptionRef = ref(null);
    const groupSidebarRef = ref(null);

    const saveDisabledReasons = computed(() => {
      const reasons = [];
      if (!hasUnsavedChanges.value) {
        reasons.push(t("pages.productEditPage.saveDisabled.noChanges"));
      }
      if (!String(form.values.product?.name ?? "").trim()) {
        reasons.push(t("pages.productEditPage.saveDisabled.nameRequired"));
      }
      for (const attr of listMissingRequiredAttributes(
        visibleCategoryAttributes.value,
        form.values.categoryAttributes,
      )) {
        reasons.push(
          t("pages.productEditPage.saveDisabled.attributeRequired", {
            name: attr.name,
          }),
        );
      }
      for (const attr of listMissingRequiredAttributes(
        visibleStoreAttributes.value,
        form.values.storeAttributes,
      )) {
        reasons.push(
          t("pages.productEditPage.saveDisabled.attributeRequired", {
            name: attr.name,
          }),
        );
      }
      return reasons;
    });

    const isSaveDisabled = computed(
      () => isSubmitting.value || saveDisabledReasons.value.length > 0,
    );


    watch(
      () => store.state.aiChat.productRefreshToken,
      () => fetchProduct()
    );

    watch(productId, (newId) => {
      // If the new product is in the same group, just update isCurrent on the client.
      // This preserves the sidebar scroll position and avoids re-rendering the list.
      // If the product is from another group, reload the group entirely.
      const memberInCurrentGroup = productGroup.value?.members?.find(m => m.productId === newId);
      if (memberInCurrentGroup) {
        productGroup.value.members.forEach(m => { m.isCurrent = m.productId === newId; });
      } else {
        fetchProductGroup(false);
      }

      const scrollEl = document.querySelector('[data-slot="sidebar-inset"]');
      const savedPageScroll = scrollEl?.scrollTop ?? 0;
      const lockScroll = () => {
        if (scrollEl) scrollEl.scrollTop = savedPageScroll;
      };
      scrollEl?.addEventListener("scroll", lockScroll);
      fetchProduct().then(() => {
        nextTick(() => {
          scrollEl?.removeEventListener("scroll", lockScroll);
          if (scrollEl) scrollEl.scrollTop = savedPageScroll;
        });
      });
    });

    function autoResizeDescription() {
      const ref = descriptionRef.value;
      const el = ref?.$el ?? ref;
      if (!el || el.tagName !== 'TEXTAREA') return;
      el.style.height = 'auto';
      el.style.height = el.scrollHeight + 'px';
    }

    function onDescriptionGenerated(text) {
      form.setFieldValue('product.description', text);
      nextTick(() => autoResizeDescription());
    }

    const isDescriptionGenerating = ref(false);
    const descriptionGeneratingHint = computed(() =>
      form.values.product?.description?.trim()
        ? t("ai.descriptionImprovingHint")
        : t("ai.descriptionGeneratingHint"),
    );

    function syncDiscountFromPrices() {
      if (!extendedPriceParameters.value) return;
      const discount = computeDiscountPercent(
        form.values.product?.oldPrice,
        form.values.product?.price,
      );
      form.setFieldValue('product.discount', discount);
    }

    watch(
      () => [form.values.product?.oldPrice, form.values.product?.price],
      () => syncDiscountFromPrices(),
    );

    const aiAttributesSummary = computed(() => {
      const lines = [];
      for (const attr of visibleCategoryAttributes.value) {
        const raw = form.values.categoryAttributes?.[String(attr.id)];
        if (raw != null && raw !== '') lines.push(`${attr.name}: ${raw}`);
      }
      for (const attr of visibleStoreAttributes.value) {
        const raw = form.values.storeAttributes?.[String(attr.id)];
        if (raw != null && raw !== '') lines.push(`${attr.name}: ${raw}`);
      }
      return lines.join('\n');
    });

    function scrollCurrentMemberIntoView() {
      groupSidebarRef.value?.scrollCurrentIntoView();
    }

    // Fetch product group
    async function fetchProductGroup(shouldScroll = false) {
      isLoadingGroup.value = true;
      await store.dispatch("getProductGroup", {
        productId: productId.value,
        onSuccess: (response) => {
          const data = response.data.value;
          if (data?.hasGroup === false || !data?.group) {
            productGroup.value = null;
          } else {
            productGroup.value = data.group;
            if (shouldScroll) scrollCurrentMemberIntoView();
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
          form.setFieldValue(
            "storeAttributes",
            attributesToFormRecord(
              storeAttributes.value,
              Object.keys(form.values.storeAttributes || {}).length
                ? form.values.storeAttributes
                : loadedStoreAttributeValues.value,
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

    async function fetchAllGroups() {
      await store.dispatch("getAllGroups", {
        onSuccess: (response) => {
          allGroups.value = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    fetchCategories();
    loadMemberships();
    fetchStoreAttributes();
    fetchAllGroups();
    fetchProduct();
    fetchProductGroup(true);

    async function fetchProduct() {
      isInitialLoad.value = true;
      hasUnsavedChanges.value = false;
      isProductLoading.value = true;
      const targetProductId = productId.value;
      await store.dispatch("getProduct", {
        productId: targetProductId,
        onSuccess: (response) => {
          // Stale-response guard: the user may have navigated to a different product while the request was in flight.
          // Data for product A must not be applied to product B's UI/state.
          if (targetProductId !== productId.value) return;

          const product = response.data.value;
          productSKU.value = product.sku;
          setPageTitle(product.sku);
          notes.value = product.notes || [];
          files.value = (product.files || []).sort((a, b) => {
            if (a.contentType < b.contentType) return -1;
            if (a.contentType > b.contentType) return 1;
            return 0;
          });
          images.value = product.images;
          category.value = product.category;
          selectedCategoryId.value = product.category?.id ?? null;
          savedCategoryId.value = selectedCategoryId.value;
          allCategoryAttributeValues.value = Object.fromEntries(
            (product.attributeValues || []).map((item) => [item.attributeId, item.value]),
          );
          loadedStoreAttributeValues.value = product.storeAttributeValues || [];

          form.setValues({
            product: {
              name: product.name,
              description: product.description,
              barcode: product.barcode || "",
              storageQuantity: product.storageQuantity,
              price: product.price,
              oldPrice: product.oldPrice ?? null,
              minPrice: product.minPrice ?? null,
              netPrice: product.netPrice ?? null,
              discount: computeDiscountPercent(product.oldPrice, product.price) ?? product.discount ?? null,
              currency: product.currency || storeBaseCurrency.value || null,
            },
            categoryAttributes: attributesToFormRecord(
              product.category?.attributes,
              product.attributeValues || [],
              "attributeId",
            ),
            storeAttributes: attributesToFormRecord(
              storeAttributes.value,
              product.storeAttributeValues || [],
              "storeAttributeId",
            ),
          });

          store.commit("aiChat/setPageContext", {
            page: "product",
            productId: targetProductId,
            productName: product.name,
            categoryId: product.category?.id ?? null,
          });

          // Mark initial load as complete after a short delay,
          // and auto-resize description once value is rendered
          setTimeout(() => {
            // If productId changed within these 100ms, keep isInitialLoad=true -
            // the next fetchProduct will reset it at the right moment.
            if (targetProductId !== productId.value) return;
            autoResizeDescription();
            isInitialLoad.value = false;
          }, 100);
        },
        onError: (error) => {
          if (targetProductId !== productId.value) return;
          displaySonnerError(error);
        },
      });
      isProductLoading.value = false;
    }

    function markDirty() {
      if (isInitialLoad.value) return;
      hasUnsavedChanges.value = true;
    }

    async function persistImages() {
      const fileIds = sortedImages.value.map((img) => img.fileId);
      let ok = false;
      await store.dispatch("postProductImages", {
        productId: productId.value,
        files: fileIds,
        onSuccess: () => {
          ok = true;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      return ok;
    }

    async function syncProductAfterSave() {
      const targetProductId = productId.value;
      await store.dispatch("getProduct", {
        productId: targetProductId,
        onSuccess: (response) => {
          if (targetProductId !== productId.value) return;

          const product = response.data.value;
          category.value = product.category;
          selectedCategoryId.value = product.category?.id ?? null;
          savedCategoryId.value = selectedCategoryId.value;
          allCategoryAttributeValues.value = Object.fromEntries(
            (product.attributeValues || []).map((item) => [item.attributeId, item.value]),
          );
          applyCategoryAttributesToForm();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    async function confirmDiscardChanges() {
      if (!hasUnsavedChanges.value) return true;
      return confirm({
        title: t("pages.productEditPage.discardChangesConfirm.title"),
        description: t("pages.productEditPage.discardChangesConfirm.description"),
        confirmLabel: t("pages.productEditPage.discardChangesConfirm.confirmLabel"),
        cancelLabel: t("pages.productEditPage.discardChangesConfirm.cancelLabel"),
        destructive: false,
      });
    }

    // Save product (manual save only)
    async function saveProduct(showSuccessToast = true) {
      // First validate the main form
      const formValid = await form.validate();
      if (!formValid.valid) {
        return false;
      }

      syncCategoryAttributesCacheFromForm();

      isSubmitting.value = true;

      const values = form.values;
      const input = {
        ...values.product,
        ...(selectedCategoryId.value != null ? { categoryId: Number(selectedCategoryId.value) } : {}),
        attributeValues: categoryAttributeCacheToValues(allCategoryAttributeValues.value),
        storeAttributeValues: formRecordToAttributeValues(values.storeAttributes, "storeAttributeId"),
      };

      try {
        let updateOk = false;
        await store.dispatch("updateProduct", {
          productId: productId.value,
          input,
          onSuccess: () => {
            updateOk = true;
          },
          onError: (error) => {
            if (error?.response?.data?.errors)
              form.setErrors(error.response.data.errors);
            applyAttributeUniquenessError(form, error);
            displaySonnerError(error);
          },
        });
        if (!updateOk) return false;

        const imagesOk = await persistImages();
        if (!imagesOk) return false;

        await syncProductAfterSave();
        hasUnsavedChanges.value = false;
        if (showSuccessToast) {
          displaySonnerSuccess(t("pages.productEditPage.toast.updated"));
        }
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

    watch(
      () => form.values,
      () => {
        markDirty();
      },
      { deep: true },
    );

    async function deleteFile(id) {
      if (isDeletingFile.value) return;
      isDeletingFile.value = true;
      try {
        await store.dispatch("deleteProductFile", {
          id,
          productId: productId.value,
          onSuccess: () => {
            displaySonnerSuccess(t("pages.productEditPage.toast.fileDeleted"));
            const fileIndex = files.value.findIndex((f) => f.id == id);
            if (fileIndex > -1) {
              files.value.splice(fileIndex, 1);
            }
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } finally {
        isDeletingFile.value = false;
      }
    }

    async function handleDeleteProduct() {
      hasUnsavedChanges.value = false;
      await store.dispatch("deleteProduct", {
        productId: productId.value,
        onSuccess: (response) => {
          router.push(response.data.value.next);
          displaySonnerSuccess(t("pages.productEditPage.toast.deleted"));
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    async function addNote() {
      await store.dispatch("postNote", {
        productId: productId.value,
        input: {
          text: "",
          importance: "low",
        },
        onSuccess: (response) => {
          notes.value.push(response.data.value);
          displaySonnerSuccess(t("pages.productEditPage.toast.noteAdded"));
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
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
        productId: productId.value,
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
              t("pages.productEditPage.toast.skuChangedWithErrors", { count: failed.length, details }),
            );
          } else if (mpResults.length > 0) {
            displaySonnerSuccess(
              t("pages.productEditPage.toast.skuChangedOnAdapters", { count: mpResults.length }),
            );
          } else {
            displaySonnerSuccess(t("pages.productEditPage.toast.skuChanged"));
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isUpdatingSku.value = false;
    }

    const groupSelectOptions = computed(() => [
      { value: GROUP_NONE, label: t("pages.productEditPage.fields.group.noGroupOption") },
      ...allGroups.value.map((g) => ({
        value: String(g.id),
        label: g.name,
      })),
    ]);

    const currentGroupSelectValue = computed(() =>
      productGroup.value?.id ? String(productGroup.value.id) : GROUP_NONE,
    );

    async function onGroupSelectChange(value) {
      if (isChangingGroup.value) return;
      const newId = value && value !== GROUP_NONE ? parseInt(value) : null;
      const oldId = productGroup.value?.id || null;
      if (newId === oldId) return;

      isChangingGroup.value = true;
      try {
        if (oldId) {
          await http.delete(`/productgroups/${oldId}/products/${productId.value}`);
        }
        if (newId) {
          await http.post(`/productgroups/${newId}/products`, { productIds: [productId.value] });
        }
        displaySonnerSuccess(newId ? t("pages.productEditPage.toast.groupChanged") : t("pages.productEditPage.toast.removedFromGroup"));
        await fetchProductGroup();
      } catch (error) {
        displaySonnerError(error);
      } finally {
        isChangingGroup.value = false;
      }
    }

    const duplicateProductUrl = computed(() => getDuplicateUrl(productId.value));

    async function goToDuplicate() {
      const ok = await confirmDiscardChanges();
      if (!ok) return;
      hasUnsavedChanges.value = false;
      router.push(duplicateProductUrl.value);
    }

    function getDuplicateUrl(sourceProductId) {
      const params = new URLSearchParams();
      params.set('fromProduct', String(sourceProductId));
      if (productGroup.value?.id) params.set('groupId', String(productGroup.value.id));
      return `/products/create?${params.toString()}`;
    }

    // Image gallery lightbox & reordering
    const lightboxIndex = ref(null);
    const sortedImages = ref([]);

    watch(images, (val) => {
      sortedImages.value = val?.slice().sort((a, b) => a.index - b.index) ?? [];
    }, { immediate: true });

    watch(
      sortedImages,
      () => {
        markDirty();
      },
      { deep: true },
    );

    function onGalleryDragChange() {
      sortedImages.value.forEach((img, idx) => { img.index = idx; });
      images.value = sortedImages.value.map((img) => ({ ...img }));
    }

    async function removeGalleryImage(index) {
      const ok = await confirm({
        title: t("pages.productEditPage.removeGalleryImageConfirm.title"),
        description: t("pages.productEditPage.removeGalleryImageConfirm.description"),
        confirmLabel: t("common.delete"),
      });
      if (!ok) return;

      sortedImages.value.splice(index, 1);
      sortedImages.value.forEach((img, idx) => { img.index = idx; });
      images.value = sortedImages.value.map((img) => ({ ...img }));
    }

    // All image files for lightbox (attached first, then unattached)
    const lightboxImages = computed(() => {
      const imageFiles = files.value?.filter((f) => f.contentType?.includes("image")) ?? [];
      const attachedIds = new Set(sortedImages.value.map((img) => img.fileId));
      const attached = sortedImages.value.map((img) => img.fileId);
      const unattached = imageFiles.filter((f) => !attachedIds.has(f.id)).map((f) => f.id);
      return [...attached, ...unattached];
    });

    // Gallery image upload
    const galleryFileInput = ref(null);
    const isUploadingGallery = ref(false);

    function triggerGalleryUpload() {
      galleryFileInput.value?.click();
    }

    async function handleGalleryUpload(event) {
      const fileList = event.target.files;
      if (!fileList?.length) return;

      isUploadingGallery.value = true;
      // Freeze productId at the moment the upload starts. If the user
      // navigates to a different product during the upload, all subsequent steps
      // (PUT files / POST images / fetch) will target the original product,
      // and the final fetchProduct only runs if the user hasn't navigated away.
      const targetProductId = productId.value;
      try {
        // Upload each file
        const newFileIds = [];
        for (const file of fileList) {
          const formData = new FormData();
          formData.append('files', file);
          const response = await http({
            url: `/products/${targetProductId}/files`,
            method: 'PUT',
            data: formData,
            headers: { 'Content-Type': 'multipart/form-data' },
            timeout: 60 * 60 * 1000,
          });
          if (response.data) {
            const uploaded = Array.isArray(response.data) ? response.data : [response.data];
            newFileIds.push(...uploaded.map((f) => f.id));
          }
        }

        // Attach new files as images at end of current list.
        // sortedImages is a reactive ref, and if the user has already navigated
        // to a different product, sortedImages already refers to that other product.
        // So we recompute the list relative to targetProductId carefully:
        // only send something meaningful if the context hasn't changed.
        if (targetProductId === productId.value) {
          const existingFileIds = sortedImages.value.map((img) => img.fileId);
          const allFileIds = [...existingFileIds, ...newFileIds];
          await store.dispatch("postProductImages", {
            productId: targetProductId,
            files: allFileIds,
            onSuccess: () => {},
            onError: (error) => { displaySonnerError(error); },
          });
        } else {
          // Context has changed - attach at least the new files to their original product.
          // Without this, uploaded files would remain "orphaned" in the gallery.
          // Load the up-to-date image list of the original product and append the new ones.
          try {
            const refresh = await http.get(`/products/${targetProductId}`);
            const existing = (refresh.data?.images || [])
              .slice()
              .sort((a, b) => a.index - b.index)
              .map((img) => img.fileId);
            await store.dispatch("postProductImages", {
              productId: targetProductId,
              files: [...existing, ...newFileIds],
              onSuccess: () => {},
              onError: (error) => { displaySonnerError(error); },
            });
          } catch (e) {
            displaySonnerError(e);
          }
        }

        // Refresh product to sync everything - but only if the context hasn't changed,
        // otherwise we would overwrite the UI of a different product.
        if (targetProductId === productId.value) {
          await fetchProduct();
        }
      } catch (error) {
        displaySonnerError(error);
      } finally {
        isUploadingGallery.value = false;
        // Reset input so the same file can be selected again
        if (galleryFileInput.value) galleryFileInput.value.value = '';
      }
    }

    function openLightbox(idx) { lightboxIndex.value = idx; }
    function openLightboxByFileId(fileId) {
      const idx = lightboxImages.value.indexOf(fileId);
      if (idx !== -1) lightboxIndex.value = idx;
    }
    function closeLightbox() { lightboxIndex.value = null; }
    function nextImage() { if (lightboxIndex.value < lightboxImages.value.length - 1) lightboxIndex.value++; }
    function prevImage() { if (lightboxIndex.value > 0) lightboxIndex.value--; }

    function handleLightboxKeydown(e) {
      if (lightboxIndex.value == null) return;
      if (e.key === 'Escape') closeLightbox();
      else if (e.key === 'ArrowRight') nextImage();
      else if (e.key === 'ArrowLeft') prevImage();
    }

    function onBeforeUnload(e) {
      if (hasUnsavedChanges.value) {
        e.preventDefault();
        e.returnValue = "";
      }
    }

    onBeforeRouteLeave(async () => {
      if (isSubmitting.value) return false;
      const ok = await confirmDiscardChanges();
      if (ok) hasUnsavedChanges.value = false;
      return ok;
    });

    onMounted(async () => {
      window.addEventListener("keydown", handleLightboxKeydown);
      window.addEventListener("beforeunload", onBeforeUnload);
      try {
        const { loadMe } = await import("@/main");
        const me = await loadMe();
        aiEnabled.value = me?.aiEnabled === true;
      } catch {
        aiEnabled.value = false;
      }
    });
    onUnmounted(() => {
      window.removeEventListener("keydown", handleLightboxKeydown);
      window.removeEventListener("beforeunload", onBeforeUnload);
      store.commit("aiChat/clearPageContext");
    });

    return {
      productSKU,
      form,
      onSubmit,
      isSubmitting,
      isSaveDisabled,
      saveDisabledReasons,
      isProductLoading,
      files,
      aiEnabled,      deleteFile,
      productId,
      images,
      handleDeleteProduct,
      notes,
      addNote,
      category,
      categories,
      selectedCategoryId,
      isLoadingCategories,
      isChangingCategory,
      categorySelectOptions,
      currentCategorySelectValue,
      onCategorySelectChange,
      visibleCategoryAttributes,
      visibleStoreAttributes,
      attributeCountLabel,
      storeAttributes,
      isLoadingStoreAttributes,

      productGroup,
      isLoadingGroup,
      hasUnsavedChanges,
      goToDuplicate,
      fetchProduct,
      showSkuDialog,
      newSkuValue,
      isUpdatingSku,
      openSkuDialog,
      submitSkuChange,
      allGroups,
      isChangingGroup,
      groupSelectOptions,
      currentGroupSelectValue,
      onGroupSelectChange,
      descriptionRef,
      groupSidebarRef,
      autoResizeDescription,
      onDescriptionGenerated,
      isDescriptionGenerating,
      descriptionGeneratingHint,
      aiAttributesSummary,
      extendedPriceParameters,
      storeCurrencies,
      storeBaseCurrency,
      pricePreview,
      duplicateProductUrl,
      getDuplicateUrl,
      lightboxIndex,
      sortedImages,
      lightboxImages,
      openLightbox,
      openLightboxByFileId,
      closeLightbox,
      nextImage,
      prevImage,
      onGalleryDragChange,
      removeGalleryImage,
      galleryFileInput,
      isUploadingGallery,
      triggerGalleryUpload,
      handleGalleryUpload,
    };
  },
};
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <template #nav-center>
        <Tabs v-model="tab">
          <TabsList>
            <TabsTrigger value="properties">{{ $t("pages.productEditPage.tabs.properties") }}</TabsTrigger>
            <TabsTrigger value="notes">{{ $t("pages.productEditPage.tabs.notes") }}</TabsTrigger>
            <TabsTrigger value="files">{{ $t("pages.productEditPage.tabs.files") }}</TabsTrigger>
            <TabsTrigger value="images"><span class="sm:hidden">{{ $t("pages.productEditPage.tabs.imagesShort") }}</span><span class="hidden sm:inline">{{ $t("pages.productEditPage.tabs.images") }}</span></TabsTrigger>
            <TabsTrigger v-if="category?.hasSizes" value="sizes">{{ $t("pages.productEditPage.tabs.sizes") }}</TabsTrigger>
            <TabsTrigger value="marketplaces"><span class="sm:hidden">{{ $t("pages.productEditPage.tabs.marketplacesShort") }}</span><span class="hidden sm:inline">{{ $t("nav.adapters") }}</span></TabsTrigger>
          </TabsList>
        </Tabs>
    </template>
    <PageSection
      :loading="isProductLoading && !productSKU"
      :class="
        isProductLoading && !productSKU
          ? 'mb-0 min-h-0 flex-1'
          : '!min-h-0 !grow-0 mb-0'
      "
    >
      <input
        ref="galleryFileInput"
        type="file"
        accept="image/*"
        multiple
        class="hidden"
        @change="handleGalleryUpload"
      />
      <div class="w-full">
        <div class="flex items-start gap-4 lg:gap-5">
          <ProductGroupSidebar
            ref="groupSidebarRef"
            :group="productGroup"
            :loading="isLoadingGroup"
            :disabled="isProductLoading"
            :add-url="duplicateProductUrl"
            :tab="tab"
            :get-duplicate-url="getDuplicateUrl"
          />
          <div class="relative min-w-0 flex-1">
            <div v-if="isProductLoading && productSKU" class="absolute inset-0 z-10 flex items-start justify-center pt-24 bg-background/60 backdrop-blur-[1px]">
              <Loader2 class="size-6 animate-spin text-muted-foreground" />
            </div>
            <form
            v-if="form"
            v-show="tab == 'properties'"
            id="product-edit-form"
            novalidate
            @submit.prevent="onSubmit"
            class="flex min-w-0 flex-col"
          >
            <ProductGallerySidebar
              v-model:images="sortedImages"
              layout="inline"
              class="lg:hidden mb-4"
              :is-uploading="isUploadingGallery"
              :disabled="isProductLoading"
              @drag-change="onGalleryDragChange"
              @remove="removeGalleryImage"
              @upload="triggerGalleryUpload"
              @preview="openLightbox"
            />

            <div class="flex w-full min-w-0 flex-col gap-6">
            <!-- Main fields -->
            <Card class="border-0 shadow-none">
              <CardHeader>
                <CardTitle class="text-base">
                  {{ $t("pages.productEditPage.sections.mainData.title") }}
                </CardTitle>
                <CardDescription>
                  {{ $t("pages.productEditPage.sections.mainData.description") }}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <FieldGroup class="gap-5">
                  <SettingsFormField :label="$t('pages.productEditPage.fields.category.label')">
                    <div class="space-y-1.5 w-full">
                      <SearchableSelect
                        :model-value="currentCategorySelectValue"
                        :options="categorySelectOptions"
                        :placeholder="$t('pages.productEditPage.fields.category.placeholder')"
                        :disabled="isProductLoading || isChangingCategory || isLoadingCategories"
                        @update:model-value="onCategorySelectChange"
                      />
                      <router-link
                        v-if="category"
                        :to="`/categories/${category.id}/edit`"
                        class="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
                      >
                        {{ $t("pages.productEditPage.fields.category.editLink") }}
                      </router-link>
                    </div>
                  </SettingsFormField>

                  <SettingsFormField :label="$t('pages.productEditPage.fields.group.label')">
                    <div class="space-y-1.5">
                      <SearchableSelect
                        :model-value="currentGroupSelectValue"
                        :options="groupSelectOptions"
                        :placeholder="$t('pages.productEditPage.fields.group.placeholder')"
                        :disabled="isProductLoading || isChangingGroup"
                        @update:model-value="onGroupSelectChange"
                      />
                      <router-link
                        v-if="productGroup"
                        :to="`/product-groups/${productGroup.id}/edit`"
                        class="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
                      >
                        {{ $t("pages.productEditPage.fields.group.editLink") }}
                      </router-link>
                    </div>
                  </SettingsFormField>

                  <SettingsFormField :label="$t('pages.productEditPage.fields.sku.label')" label-for="product-sku">
                    <div class="flex items-center gap-2">
                      <Input
                        id="product-sku"
                        type="text"
                        disabled
                        :model-value="productSKU"
                        class="font-mono"
                      />
                      <Button
                        variant="outline"
                        size="icon"
                        class="shrink-0 size-9"
                        :title="$t('pages.productEditPage.fields.sku.changeAction')"
                        :aria-label="$t('pages.productEditPage.fields.sku.changeAction')"
                        type="button"
                        @click="openSkuDialog"
                      >
                        <Pencil class="size-4" />
                      </Button>
                    </div>
                  </SettingsFormField>

                  <FormField v-slot="{ value, setValue, errorMessage }" name="product.barcode">
                    <FormItem class="gap-0">
                      <SettingsFormField :label="$t('pages.productEditPage.fields.barcode.label')" label-for="product-barcode" :error="errorMessage">
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
                      <SettingsFormField :label="$t('pages.productEditPage.fields.name.label')" label-for="product-name" :error="errorMessage" required>
                        <FormControl>
                          <Input id="product-name" type="text" v-bind="componentField" />
                        </FormControl>
                      </SettingsFormField>
                    </FormItem>
                  </FormField>

                  <FormField v-slot="{ componentField, errorMessage }" name="product.description">
                    <FormItem class="gap-1.5">
                      <div class="flex items-center justify-between gap-2">
                        <label for="product-description" class="text-sm font-medium leading-none">{{ $t("pages.productEditPage.fields.description.label") }}</label>
                        <AiDescriptionGenerator
                          v-if="aiEnabled"
                          :product-name="form.values.product?.name"
                          :product-sku="productSKU"
                          :category-name="category?.name"
                          :existing-description="form.values.product?.description"
                          :attributes-summary="aiAttributesSummary"
                          @update:generating="isDescriptionGenerating = $event"
                          @generated="onDescriptionGenerated"
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

                  <template v-if="!category?.hasSizes">
                    <div class="space-y-4 border-t pt-5">
                      <div>
                        <h4 class="text-sm font-medium">{{ $t("pages.productEditPage.sections.stock.title") }}</h4>
                        <p class="text-xs text-muted-foreground mt-0.5">
                          {{ $t("pages.productEditPage.sections.stock.description") }}
                        </p>
                      </div>
                      <div class="grid grid-cols-1 gap-5">
                        <FormField v-slot="{ value, setValue, errorMessage }" name="product.storageQuantity">
                          <FormItem class="gap-0">
                            <SettingsFormField
                              :label="$t('pages.productEditPage.fields.quantity.label')"
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
                        <h4 class="text-sm font-medium">{{ $t("pages.productEditPage.sections.price.title") }}</h4>
                        <p class="text-xs text-muted-foreground mt-0.5">
                          {{ $t("pages.productEditPage.sections.price.description", { currency: storeBaseCurrency || $t("pages.productEditPage.fields.price.defaultCurrencyFallback") }) }}
                        </p>
                      </div>
                      <div class="grid grid-cols-1 gap-5">
                        <FormField v-slot="{ value, setValue, errorMessage }" name="product.price">
                          <FormItem class="gap-0">
                            <SettingsFormField :label="$t('pages.productEditPage.fields.price.label', { currency: storeBaseCurrency || $t('common.emDash') })" label-for="product-price" :error="errorMessage">
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
                        <h4 class="text-sm font-medium">{{ $t("pages.productEditPage.sections.extendedPrice.title") }}</h4>
                        <p class="text-xs text-muted-foreground mt-0.5">
                          {{ $t("pages.productEditPage.sections.extendedPrice.description") }}
                        </p>
                      </div>
                      <div class="grid grid-cols-1 sm:grid-cols-2 gap-5">
                        <FormField v-slot="{ value, setValue, errorMessage }" name="product.oldPrice">
                          <FormItem class="gap-0">
                            <SettingsFormField
                              :label="$t('pages.productEditPage.fields.oldPrice.label', { currency: storeBaseCurrency || $t('common.emDash') })"
                              :error="errorMessage"
                              :description="$t('pages.productEditPage.fields.oldPrice.description')"
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
                              :label="$t('pages.productEditPage.fields.discount.label')"
                              :error="errorMessage"
                              :description="$t('pages.productEditPage.fields.discount.description')"
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
                            <SettingsFormField :label="$t('pages.productEditPage.fields.minPrice.label', { currency: storeBaseCurrency || $t('common.emDash') })" :error="errorMessage">
                              <FormControl>
                                <Input type="number" step="0.01" min="0" :model-value="value" @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)" />
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                        <FormField v-slot="{ value, setValue, errorMessage }" name="product.netPrice">
                          <FormItem class="gap-0">
                            <SettingsFormField :label="$t('pages.productEditPage.fields.netPrice.label', { currency: storeBaseCurrency || $t('common.emDash') })" :error="errorMessage">
                              <FormControl>
                                <Input type="number" step="0.01" min="0" :model-value="value" @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)" />
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                        <FormField v-slot="{ value, setValue, errorMessage }" name="product.currency">
                          <FormItem class="gap-0 sm:col-span-2">
                            <SettingsFormField
                              :label="$t('pages.productEditPage.fields.currency.label')"
                              :error="errorMessage"
                              :description="$t('pages.productEditPage.fields.currency.description', { currency: storeBaseCurrency || $t('pages.productEditPage.fields.currency.defaultCurrencyFallback') })"
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

            <!-- SKU change dialog -->
            <Dialog v-model:open="showSkuDialog">
              <DialogContent size="md">
                <DialogHeader>
                  <DialogTitle>{{ $t("pages.productEditPage.skuDialog.title") }}</DialogTitle>
                  <DialogDescription>
                    {{ $t("pages.productEditPage.skuDialog.description") }}
                  </DialogDescription>
                </DialogHeader>
                <div class="space-y-4 py-4">
                  <SettingsFormField :label="$t('pages.productEditPage.skuDialog.currentSkuLabel')">
                    <Input type="text" disabled :model-value="productSKU" class="font-mono" />
                  </SettingsFormField>
                  <SettingsFormField :label="$t('pages.productEditPage.skuDialog.newSkuLabel')" label-for="new-sku">
                    <Input
                      id="new-sku"
                      type="text"
                      v-model="newSkuValue"
                      maxlength="50"
                      :placeholder="$t('pages.productEditPage.skuDialog.newSkuPlaceholder')"
                      class="font-mono"
                      @keydown.enter.prevent="submitSkuChange"
                    />
                  </SettingsFormField>
                </div>
                <DialogFooter>
                  <DialogClose as-child><Button variant="outline" :disabled="isUpdatingSku">{{ $t("common.cancel") }}</Button></DialogClose>
                  <Button
                    @click="submitSkuChange"
                    :disabled="isUpdatingSku || !newSkuValue.trim() || newSkuValue.trim() === productSKU"
                  >
                    <Loader2 v-if="isUpdatingSku" class="size-4 mr-2 animate-spin" />
                    {{ $t("pages.productEditPage.skuDialog.changeAction") }}
                  </Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

            <!-- Category attributes -->
            <Card
              v-if="category && visibleCategoryAttributes.length > 0"
              class="border-0 shadow-none"
            >
              <CardHeader>
                <CardTitle class="text-base">
                  {{ $t("pages.productEditPage.sections.categoryAttributes.title") }}
                </CardTitle>
                <CardDescription>
                  {{ $t("pages.productEditPage.sections.categoryAttributes.description", {
                    count: visibleCategoryAttributes.length,
                    unit: attributeCountLabel(visibleCategoryAttributes.length),
                    category: category.name,
                  }) }}
                </CardDescription>
              </CardHeader>
              <CardContent class="pt-0">
                <ProductAttributeForm
                  :attributes="category.attributes"
                />
              </CardContent>
            </Card>

            <!-- Store attributes -->
            <Card
              v-if="visibleStoreAttributes.length > 0"
              class="border-0 shadow-none"
            >
              <CardHeader>
                <CardTitle class="text-base">
                  {{ $t("nav.globalAttributes") }}
                </CardTitle>
                <CardDescription>
                  {{ $t("pages.productEditPage.sections.storeAttributes.description", {
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

            <div class="h-[4.75rem] shrink-0" aria-hidden="true" />

            </div>
            </form>

            <!-- Files tab -->
            <div v-show="tab == 'files'" class="flex flex-col border rounded-xl" style="min-height: calc(100vh - 8.5rem)">
              <ProductFileManager
                :files="files"
                :productId="productId"
                :productName="form.values?.product?.name || ''"
                :productSku="productSKU"
                @deleteFile="deleteFile"
                @filesChanged="fetchProduct"
              />
            </div>

            <!-- Images tab -->
            <div v-show="tab == 'images'" class="flex flex-col border rounded-xl overflow-hidden" style="height: calc(100vh - 8.5rem)">
              <ProductImagesEditor
                :files="files"
                v-model:images="images"
                :productId="productId"
                @preview="openLightboxByFileId"
              />
            </div>

            <!-- Sizes tab -->
            <div v-if="category?.hasSizes" v-show="tab == 'sizes'" class="flex flex-col border rounded-xl overflow-hidden" style="height: calc(100vh - 8.5rem)">
              <ProductVariantsEditor :productId="productId" />
            </div>

            <!-- Marketplaces tab -->
            <div v-show="tab == 'marketplaces'" class="flex flex-col border rounded-xl overflow-hidden" style="height: calc(100vh - 8.5rem)">
              <ProductMarketplaceIntegrations :productId="productId" @product-updated="fetchProduct" />
            </div>

            <!-- Notes tab -->
            <div v-show="tab == 'notes'" class="flex flex-col border rounded-xl overflow-hidden" style="height: calc(100vh - 8.5rem)">
              <div class="flex items-center justify-between px-4 h-11 border-b shrink-0">
                <p class="text-sm font-medium flex items-center gap-2">
                  <NotebookPen class="size-4" />
                  {{ $t("pages.productEditPage.tabs.notes") }}
                </p>
                <Button variant="ghost" size="sm" @click="addNote" class="h-7">
                  <Plus class="size-4 mr-1" />
                  {{ $t("common.add") }}
                </Button>
              </div>
              <div class="flex-1 overflow-y-auto">
                <EmptyState v-if="notes.length === 0" :title="$t('pages.productEditPage.notesEmpty.title')" :description="$t('pages.productEditPage.notesEmpty.description')">
                  <template #icon>
                    <NotebookPen />
                  </template>
                </EmptyState>
                <div v-else class="p-4 flex flex-col gap-4">
                  <div v-for="(note, index) in notes" :key="note.id || index">
                    <Note
                      v-model:note="notes[index]"
                      :productId="productId"
                      @onDelete="notes.splice(index, 1)"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>

          <ProductGallerySidebar
            v-if="tab === 'properties'"
            v-model:images="sortedImages"
            :is-uploading="isUploadingGallery"
            :disabled="isProductLoading"
            @drag-change="onGalleryDragChange"
            @remove="removeGalleryImage"
            @upload="triggerGalleryUpload"
            @preview="openLightbox"
          />
        </div>
      </div>
    </PageSection>

    <div
      v-show="tab === 'properties'"
      class="fixed bottom-0 z-20 left-0 right-0 md:left-[var(--sidebar-width)] pointer-events-none pb-3"
    >
      <div class="adaptive-padding pointer-events-none">
        <div class="flex w-full items-stretch gap-4 lg:gap-5 pointer-events-none">
          <div class="product-group-sidebar-spacer hidden shrink-0 lg:block" aria-hidden="true" />
          <Card class="flex-1 min-w-0 pointer-events-auto rounded-xl !gap-0 !p-3 shadow-sm">
            <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
              <div class="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  type="button"
                  @click="goToDuplicate"
                >
                  <Copy class="size-4 mr-2" />
                  {{ $t("pages.productEditPage.actions.duplicate") }}
                </Button>
                <AlertDialog>
                  <AlertDialogTrigger as-child>
                    <Button
                      variant="outline"
                      size="sm"
                      type="button"
                      class="text-destructive hover:text-destructive"
                    >
                      <Trash2 class="size-4 mr-2" />
                      {{ $t("common.delete") }}
                    </Button>
                  </AlertDialogTrigger>
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>{{ $t("pages.productEditPage.deleteConfirm.title", { sku: productSKU }) }}</AlertDialogTitle>
                      <AlertDialogDescription>
                        {{ $t("pages.productEditPage.deleteConfirm.description") }}
                      </AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                      <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
                      <AlertDialogAction @click="handleDeleteProduct" class="bg-destructive text-white hover:bg-destructive/90">
                        {{ $t("pages.productEditPage.deleteConfirm.confirmAction") }}
                      </AlertDialogAction>
                    </AlertDialogFooter>
                  </AlertDialogContent>
                </AlertDialog>
              </div>
              <div class="flex items-center justify-end gap-3">
                <p
                  v-if="hasUnsavedChanges"
                  class="text-sm text-muted-foreground"
                >
                  {{ $t("pages.productEditPage.unsavedChanges") }}
                </p>
                <TooltipProvider :delay-duration="200">
                  <Tooltip :disabled="!isSaveDisabled || saveDisabledReasons.length === 0">
                    <TooltipTrigger as-child>
                      <span
                        class="inline-flex"
                        :tabindex="isSaveDisabled ? 0 : undefined"
                      >
                        <Button
                          type="submit"
                          form="product-edit-form"
                          size="sm"
                          :disabled="isSaveDisabled"
                        >
                          <Loader2 v-if="isSubmitting" class="size-4 mr-2 animate-spin" />
                          <Save v-else class="size-4 mr-2" />
                          {{ $t("common.save") }}
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
          </Card>
          <div class="product-gallery-sidebar-spacer hidden shrink-0 lg:block" aria-hidden="true" />
        </div>
      </div>
    </div>

    <!-- Lightbox overlay -->
    <Teleport to="body">
      <div
        v-if="lightboxIndex != null"
        class="fixed inset-0 z-50 bg-black/90 flex items-center justify-center"
        @click.self="closeLightbox"
      >
        <!-- Close button -->
        <Button variant="ghost" size="icon" class="absolute top-4 right-4 text-white/70 hover:text-white hover:bg-transparent" :aria-label="$t('common.close')" @click="closeLightbox">
          <X class="size-8" />
        </Button>
        <!-- Prev -->
        <Button v-if="lightboxIndex > 0" variant="ghost" size="icon" class="absolute left-4 text-white/70 hover:text-white hover:bg-transparent" :aria-label="$t('pages.productEditPage.lightbox.prevPhoto')" @click="prevImage">
          <ChevronLeft class="size-10" />
        </Button>
        <!-- Image -->
        <img
          :src="`/api/files/${lightboxImages[lightboxIndex]}/download`"
          :alt="$t('pages.productEditPage.lightbox.productPhotoAlt')"
          class="max-h-[90vh] max-w-[90vw] object-contain"
        />
        <!-- Next -->
        <Button v-if="lightboxIndex < lightboxImages.length - 1" variant="ghost" size="icon" class="absolute right-4 text-white/70 hover:text-white hover:bg-transparent" :aria-label="$t('pages.productEditPage.lightbox.nextPhoto')" @click="nextImage">
          <ChevronRight class="size-10" />
        </Button>
        <!-- Counter -->
        <div class="absolute bottom-4 text-white/60 text-sm">
          {{ lightboxIndex + 1 }} / {{ lightboxImages.length }}
        </div>
      </div>
    </Teleport>
  </AppPageChrome>
</template>

<style scoped>
.product-group-sidebar-spacer {
  width: clamp(14rem, 22vw, 24rem);
}
.product-gallery-sidebar-spacer {
  width: clamp(10rem, 14vw, 14rem);
}
</style>
