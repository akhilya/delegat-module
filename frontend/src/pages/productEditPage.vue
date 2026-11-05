<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import { FormControl, FormField, FormItem } from "@/components/ui/form";
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
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import ProductFileManager from "@/components/products/ProductFileManager.vue";
import ProductImagesEditor from "@/components/productImagesEditor.vue";
import ProductAttributeForm from "@/components/products/productAttributeForm.vue";
import StoreAttributeForm from "@/components/products/storeAttributeForm.vue";
import ProductMarketplaceIntegrations from "@/components/products/productMarketplaceIntegrations.vue";
import ProductVariantsEditor from "@/components/products/ProductVariantsEditor.vue";
import { BarcodeInput } from "@/components/ui/barcode";
import { SearchableSelect } from "@/components/ui/searchable-select";
import CreateProductGroupDialog from "@/components/products/CreateProductGroupDialog.vue";
import NoteDetailSheet from "@/components/notes/NoteDetailSheet.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import { FieldGroup, FieldError, FieldLabel } from "@/components/ui/field";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";

import {
  Save,
  Trash2,
  NotebookPen,
  Plus,
  Copy,
  Check,
  X,
  ChevronLeft,
  ChevronRight,
  EllipsisVertical,
} from "lucide-vue-next";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { setPageTitle } from "@/router";
import { useStore } from "vuex";
import { useRoute, onBeforeRouteLeave } from "vue-router";
import ProductGroupSidebar from "@/components/products/ProductGroupSidebar.vue";
import ProductGallerySidebar from "@/components/products/ProductGallerySidebar.vue";
import AppImage from "@/components/AppImage.vue";
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from "vue";
import { getCachedMe } from "@/lib/meCache";
import { canAct, PERMS } from "@/lib/permissions";
import { toTypedSchema } from "../lib/zodSchema";
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
import { mapPool } from "@/lib/runPool";
import { confirm, isConfirmOverlayActive } from "@/composables/useConfirm";
import AiDescriptionGenerator from "@/components/ai/AiDescriptionGenerator.vue";
import { useSelectedStore } from "@/composables/useSelectedStore";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { formatEquivalents } from "@/lib/currency";
import { computeDiscountPercent } from "@/lib/productPrice";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import {
  noteId,
  noteImportanceClass,
  noteImportanceLabel,
  noteListCaption,
  notePreviewTitle,
  sortNotesNewestFirst,
} from "@/lib/notesUi";
import { MENU_ITEM } from "@/lib/menuSurface";
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
    Spinner,
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
    AppImage,
    BarcodeInput,
    SearchableSelect,
    CreateProductGroupDialog,
    NoteDetailSheet,
    SettingsFormField,
    SettingsFormSection,
    FieldGroup,
    FieldError,
    FieldLabel,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    Save,
    Trash2,
    NotebookPen,
    Plus,
    Copy,
    Check,
    X,
    ChevronLeft,
    ChevronRight,
    EllipsisVertical,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
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
      tab: this.$route.query.tab ?? "properties",
    };
  },
  watch: {
    tab(newVal) {
      this.$router.replace({ query: { ...this.$route.query, tab: newVal } });
      if (newVal === "properties") {
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
    const noteSheetOpen = ref(false);
    const selectedNote = ref(null);
    const isAddingNote = ref(false);
    const { formatDateTime: formatStoreDateTime, loadMemberships } =
      useStoreDateTime();
    loadMemberships();
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
    const visibleCategoryAttributes = computed(
      () =>
        category.value?.attributes?.filter(
          (a) => a.showOnProductPage !== false,
        ) ?? [],
    );
    const visibleStoreAttributes = computed(() =>
      storeAttributes.value.filter((a) => a.showOnProductPage !== false),
    );
    const canManageCatalog = computed(() =>
      canAct(getCachedMe(), PERMS.CatalogManage),
    );

    const { extendedPriceParameters, storeCurrencies, storeBaseCurrency } =
      useSelectedStore();

    const priceField = z.number().min(0).optional().nullable();
    const formSchema = computed(() =>
      toTypedSchema(
        z.object({
          product: z.object({
            name: z
              .string()
              .min(1, t("pages.productEditPage.validation.nameRequired"))
              .max(FIELD_LIMITS.productName),
            description: z
              .string()
              .max(FIELD_LIMITS.productDescription)
              .optional(),
            barcode: z
              .string()
              .max(FIELD_LIMITS.productBarcode)
              .optional()
              .nullable(),
            storageQuantity: z
              .number()
              .int()
              .min(0, t("pages.productEditPage.validation.quantityNonNegative"))
              .optional()
              .nullable(),
            price: priceField,
            ...(extendedPriceParameters.value
              ? {
                  oldPrice: priceField,
                  minPrice: priceField,
                  netPrice: priceField,
                  discount: z
                    .number()
                    .int()
                    .min(0)
                    .max(99)
                    .optional()
                    .nullable(),
                  currency: z.string().max(3).optional().nullable(),
                }
              : {}),
          }),
          categoryAttributes: buildAttributesSchema(
            visibleCategoryAttributes.value,
          ),
          storeAttributes: buildAttributesSchema(visibleStoreAttributes.value),
        }),
      ),
    );

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
      return formatEquivalents(
        amount,
        storeCurrencies.value,
        storeBaseCurrency.value,
      );
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
      if (!nextCategoryId || nextCategoryId === selectedCategoryId.value)
        return;

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
      if (mod100 >= 11 && mod100 <= 14)
        return t("pages.productEditPage.attributesCount.formMany");
      if (mod10 === 1)
        return t("pages.productEditPage.attributesCount.formOne");
      if (mod10 >= 2 && mod10 <= 4)
        return t("pages.productEditPage.attributesCount.formFew");
      return t("pages.productEditPage.attributesCount.formMany");
    }
    const hasUnsavedChanges = ref(false);
    const isInitialLoad = ref(true);
    const isDeletingFile = ref(false);
    const showSkuDialog = ref(false);
    const newSkuValue = ref("");
    const isUpdatingSku = ref(false);
    const skuCopied = ref(false);
    let skuCopiedTimer = null;
    const allGroups = ref([]);
    const isChangingGroup = ref(false);
    const showCreateGroupDialog = ref(false);
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
      () => fetchProduct(),
    );

    watch(productId, (newId) => {
      // If the new product is in the same group, just update isCurrent on the client.
      // This preserves the sidebar scroll position and avoids re-rendering the list.
      // If the product is from another group, reload the group entirely.
      const memberInCurrentGroup = productGroup.value?.members?.find(
        (m) => m.productId === newId,
      );
      if (memberInCurrentGroup) {
        productGroup.value.members.forEach((m) => {
          m.isCurrent = m.productId === newId;
        });
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
      if (!el || el.tagName !== "TEXTAREA") return;
      el.style.height = "auto";
      el.style.height = el.scrollHeight + "px";
    }

    function onDescriptionGenerated(text) {
      form.setFieldValue("product.description", text);
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
      form.setFieldValue("product.discount", discount);
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
          notes.value = sortNotesNewestFirst(product.notes || []);
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
            (product.attributeValues || []).map((item) => [
              item.attributeId,
              item.value,
            ]),
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
              discount:
                computeDiscountPercent(product.oldPrice, product.price) ??
                product.discount ??
                null,
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
            (product.attributeValues || []).map((item) => [
              item.attributeId,
              item.value,
            ]),
          );
          applyCategoryAttributesToForm();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    async function confirmLeaveWithUnsaved() {
      if (!hasUnsavedChanges.value) return true;
      const result = await confirm({
        title: t("pages.productEditPage.discardChangesConfirm.title"),
        description: t(
          "pages.productEditPage.discardChangesConfirm.description",
        ),
        confirmLabel: t(
          "pages.productEditPage.discardChangesConfirm.confirmLabel",
        ),
        cancelLabel: t(
          "pages.productEditPage.discardChangesConfirm.cancelLabel",
        ),
        saveLabel: t("pages.productEditPage.discardChangesConfirm.saveLabel"),
        destructive: false,
      });
      if (result === "save") {
        return saveProduct(true);
      }
      return result === true;
    }

    // Save product (manual save only)
    async function saveProduct(showSuccessToast = true) {
      if (isSubmitting.value) return false;
      isSubmitting.value = true;
      try {
        // First validate the main form
        const formValid = await form.validate();
        if (!formValid.valid) {
          displaySonnerError(t("pages.productEditPage.toast.validationFailed"));
          return false;
        }

        syncCategoryAttributesCacheFromForm();

        const values = form.values;
        const input = {
          ...values.product,
          ...(selectedCategoryId.value != null
            ? { categoryId: Number(selectedCategoryId.value) }
            : {}),
          attributeValues: categoryAttributeCacheToValues(
            allCategoryAttributeValues.value,
          ),
          storeAttributeValues: formRecordToAttributeValues(
            values.storeAttributes,
            "storeAttributeId",
          ),
        };

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

    const sortedNotes = computed(() => sortNotesNewestFirst(notes.value));

    function noteRowTitle(row) {
      return notePreviewTitle(row, t("pages.notesPage.sheetTitleFallback"));
    }

    function noteRowImportance(row) {
      return noteImportanceLabel(row.importance, t);
    }

    function noteRowCaption(row) {
      return noteListCaption(row, {
        formatDateTime: formatStoreDateTime,
        t,
        includeProduct: false,
      });
    }

    function withProductContext(row) {
      return {
        ...row,
        id: noteId(row),
        productId: productId.value,
        productName: form.values?.product?.name || "",
        productSku: productSKU.value,
      };
    }

    function openNote(row) {
      selectedNote.value = withProductContext(row);
      noteSheetOpen.value = true;
    }

    function onNoteUpdated(patch) {
      const id = patch.id;
      notes.value = notes.value.map((n) =>
        noteId(n) === id
          ? {
              ...n,
              text: patch.text,
              importance: patch.importance,
              updatedAt: new Date().toISOString(),
            }
          : n,
      );
      if (noteId(selectedNote.value) === id) {
        selectedNote.value = {
          ...selectedNote.value,
          text: patch.text,
          importance: patch.importance,
          updatedAt: new Date().toISOString(),
        };
      }
    }

    function onNoteDeleted(id) {
      noteSheetOpen.value = false;
      selectedNote.value = null;
      notes.value = notes.value.filter((n) => noteId(n) !== id);
    }

    async function addNote() {
      if (isAddingNote.value) return;
      isAddingNote.value = true;
      await store.dispatch("postNote", {
        productId: productId.value,
        input: {
          text: "",
          importance: "Low",
        },
        onSuccess: (response) => {
          const created = response?.data?.value ?? response?.data;
          if (!created) return;
          notes.value = [...notes.value, created];
          openNote(created);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isAddingNote.value = false;
    }

    function openSkuDialog() {
      newSkuValue.value = "";
      skuCopied.value = false;
      showSkuDialog.value = true;
      nextTick(() => document.getElementById("new-sku")?.focus());
    }

    function selectSkuField(event) {
      event?.target?.select?.();
    }

    async function copySku() {
      const text = String(productSKU.value || "").trim();
      if (!text) return;
      try {
        await navigator.clipboard.writeText(text);
        skuCopied.value = true;
        clearTimeout(skuCopiedTimer);
        skuCopiedTimer = setTimeout(() => {
          skuCopied.value = false;
        }, 1500);
      } catch {
        displaySonnerError(t("common.copyFailed"));
      }
    }

    async function submitSkuChange() {
      const trimmed = newSkuValue.value.trim();
      if (!trimmed || trimmed === productSKU.value) return;
      if (fieldLimitIssue(trimmed, FIELD_LIMITS.productSku) === "tooLong") {
        displaySonnerError(
          t("common.tooLong", { max: FIELD_LIMITS.productSku }),
        );
        return;
      }

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
            const details = failed
              .map((r) => `${r.name}: ${r.error}`)
              .join("\n");
            displaySonnerSuccess(
              t("pages.productEditPage.toast.skuChangedWithErrors", {
                count: failed.length,
                details,
              }),
            );
          } else if (mpResults.length > 0) {
            displaySonnerSuccess(
              t("pages.productEditPage.toast.skuChangedOnAdapters", {
                count: mpResults.length,
              }),
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
      {
        value: GROUP_NONE,
        label: t("pages.productEditPage.fields.group.noGroupOption"),
      },
      ...allGroups.value.map((g) => ({
        value: String(g.id),
        label: g.name,
      })),
    ]);

    const currentGroupSelectValue = computed(() =>
      productGroup.value?.id ? String(productGroup.value.id) : GROUP_NONE,
    );

    async function onGroupSelectChange(value, options = {}) {
      if (isChangingGroup.value) return;
      const newId = value && value !== GROUP_NONE ? parseInt(value) : null;
      const oldId = productGroup.value?.id || null;
      if (newId === oldId) return;

      isChangingGroup.value = true;
      try {
        if (oldId) {
          await http.delete(
            `/productgroups/${oldId}/products/${productId.value}`,
          );
        }
        if (newId) {
          await http.post(`/productgroups/${newId}/products`, {
            productIds: [productId.value],
          });
        }
        if (options.notify !== false) {
          displaySonnerSuccess(
            newId
              ? t("pages.productEditPage.toast.groupChanged")
              : t("pages.productEditPage.toast.removedFromGroup"),
          );
        }
        await fetchProductGroup();
      } catch (error) {
        displaySonnerError(error);
      } finally {
        isChangingGroup.value = false;
      }
    }

    function openCreateGroupDialogFromSelect(close) {
      close?.();
      showCreateGroupDialog.value = true;
    }

    async function onGroupCreated(group) {
      const id = group?.id ?? group?.Id;
      if (!id) return;
      const name = group?.name ?? group?.Name;
      const next = {
        id,
        name: name || String(id),
        description: group?.description ?? group?.Description ?? null,
      };
      const exists = allGroups.value.some((g) => g.id === id);
      allGroups.value = exists
        ? allGroups.value.map((g) => (g.id === id ? { ...g, ...next } : g))
        : [...allGroups.value, next];
      await onGroupSelectChange(String(id), { notify: false });
    }

    const duplicateProductUrl = computed(() =>
      getDuplicateUrl(productId.value),
    );

    async function goToDuplicate() {
      const ok = await confirmLeaveWithUnsaved();
      if (!ok) return;
      hasUnsavedChanges.value = false;
      router.push(duplicateProductUrl.value);
    }

    function getDuplicateUrl(sourceProductId) {
      const params = new URLSearchParams();
      params.set("fromProduct", String(sourceProductId));
      if (productGroup.value?.id)
        params.set("groupId", String(productGroup.value.id));
      return `/products/create?${params.toString()}`;
    }

    // Image gallery lightbox & reordering
    const lightboxIndex = ref(null);
    const sortedImages = ref([]);

    watch(
      images,
      (val) => {
        sortedImages.value =
          val?.slice().sort((a, b) => a.index - b.index) ?? [];
      },
      { immediate: true },
    );

    watch(
      sortedImages,
      () => {
        markDirty();
      },
      { deep: true },
    );

    function onGalleryDragChange() {
      sortedImages.value.forEach((img, idx) => {
        img.index = idx;
      });
      images.value = sortedImages.value.map((img) => ({ ...img }));
    }

    async function removeGalleryImage(index) {
      const ok = await confirm({
        title: t("pages.productEditPage.removeGalleryImageConfirm.title"),
        description: t(
          "pages.productEditPage.removeGalleryImageConfirm.description",
        ),
        confirmLabel: t("common.delete"),
      });
      if (!ok) return;

      sortedImages.value.splice(index, 1);
      sortedImages.value.forEach((img, idx) => {
        img.index = idx;
      });
      images.value = sortedImages.value.map((img) => ({ ...img }));
    }

    // All image files for lightbox (attached first, then unattached)
    const lightboxImages = computed(() => {
      const imageFiles =
        files.value?.filter((f) => f.contentType?.includes("image")) ?? [];
      const attachedIds = new Set(sortedImages.value.map((img) => img.fileId));
      const attached = sortedImages.value.map((img) => img.fileId);
      const unattached = imageFiles
        .filter((f) => !attachedIds.has(f.id))
        .map((f) => f.id);
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
        const idGroups = await mapPool(Array.from(fileList), async (file) => {
          const formData = new FormData();
          formData.append("files", file);
          const response = await http({
            url: `/products/${targetProductId}/files`,
            method: "PUT",
            data: formData,
            timeout: 60 * 60 * 1000,
          });
          const uploaded = Array.isArray(response.data)
            ? response.data
            : response.data
              ? [response.data]
              : [];
          return uploaded.map((f) => f.id);
        });
        const newFileIds = idGroups.flat();

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
            onError: (error) => {
              displaySonnerError(error);
            },
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
              onError: (error) => {
                displaySonnerError(error);
              },
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
        if (galleryFileInput.value) galleryFileInput.value.value = "";
      }
    }

    function openLightbox(idx) {
      lightboxIndex.value = idx;
    }
    function openLightboxByFileId(fileId) {
      const idx = lightboxImages.value.indexOf(fileId);
      if (idx !== -1) lightboxIndex.value = idx;
    }
    function closeLightbox() {
      lightboxIndex.value = null;
    }
    function nextImage() {
      if (lightboxIndex.value < lightboxImages.value.length - 1)
        lightboxIndex.value++;
    }
    function prevImage() {
      if (lightboxIndex.value > 0) lightboxIndex.value--;
    }

    function handleLightboxKeydown(e) {
      if (lightboxIndex.value == null) return;
      if (e.key === "Escape") closeLightbox();
      else if (e.key === "ArrowRight") nextImage();
      else if (e.key === "ArrowLeft") prevImage();
    }

    async function handleSaveHotkey(event) {
      if (
        event.defaultPrevented ||
        event.isComposing ||
        event.altKey ||
        event.shiftKey ||
        !(event.ctrlKey || event.metaKey) ||
        !(event.code === "KeyS" || event.key?.toLowerCase() === "s")
      )
        return;
      event.preventDefault();
      if (
        event.repeat ||
        isSubmitting.value ||
        isProductLoading.value ||
        isInitialLoad.value ||
        !productSKU.value ||
        !canManageCatalog.value ||
        lightboxIndex.value != null ||
        isConfirmOverlayActive()
      )
        return;
      if (
        document.querySelector(
          '[data-slot="dialog-content"][data-state="open"], [data-slot="alert-dialog-content"][data-state="open"]',
        ) ||
        (event.target instanceof Element &&
          event.target.closest('[data-slot="sheet-content"]'))
      )
        return;

      // Reka number fields commit on blur; restore focus after committing the draft.
      if (
        event.target instanceof HTMLInputElement &&
        event.target.getAttribute("role") === "spinbutton"
      ) {
        event.target.blur();
        event.target.focus({ preventScroll: true });
      }
      await nextTick();
      if (isSubmitting.value) return;
      if (!hasUnsavedChanges.value) {
        displaySonnerSuccess(t("pages.productEditPage.saveDisabled.noChanges"));
        return;
      }
      await saveProduct(true);
    }

    function onBeforeUnload(e) {
      if (hasUnsavedChanges.value) {
        e.preventDefault();
        e.returnValue = "";
      }
    }

    onBeforeRouteLeave(async () => {
      if (isSubmitting.value) return false;
      const ok = await confirmLeaveWithUnsaved();
      if (ok) hasUnsavedChanges.value = false;
      return Boolean(ok);
    });

    onMounted(async () => {
      window.addEventListener("keydown", handleLightboxKeydown);
      window.addEventListener("keydown", handleSaveHotkey);
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
      window.removeEventListener("keydown", handleSaveHotkey);
      window.removeEventListener("beforeunload", onBeforeUnload);
      store.commit("aiChat/clearPageContext");
      clearTimeout(skuCopiedTimer);
    });

    return {
      productSKU,
      form,
      onSubmit,
      isSubmitting,
      isSaveDisabled,
      saveDisabledReasons,
      canManageCatalog,
      isProductLoading,
      files,
      aiEnabled,
      deleteFile,
      productId,
      images,
      handleDeleteProduct,
      notes,
      sortedNotes,
      addNote,
      isAddingNote,
      noteSheetOpen,
      selectedNote,
      openNote,
      onNoteUpdated,
      onNoteDeleted,
      noteId,
      noteRowTitle,
      noteRowCaption,
      noteRowImportance,
      noteImportanceClass,
      MENU_ITEM,
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
      skuCopied,
      openSkuDialog,
      selectSkuField,
      copySku,
      submitSkuChange,
      allGroups,
      isChangingGroup,
      groupSelectOptions,
      currentGroupSelectValue,
      onGroupSelectChange,
      showCreateGroupDialog,
      openCreateGroupDialogFromSelect,
      onGroupCreated,
      descriptionRef,
      groupSidebarRef,
      autoResizeDescription,
      onDescriptionGenerated,
      isDescriptionGenerating,
      descriptionGeneratingHint,
      aiAttributesSummary,
      FIELD_LIMITS,
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
  <AppPageChrome>
    <template #nav-center>
      <Tabs v-model="tab">
        <TabsList>
          <TabsTrigger value="properties">{{
            $t("pages.productEditPage.tabs.properties")
          }}</TabsTrigger>
          <TabsTrigger value="notes">{{
            $t("pages.productEditPage.tabs.notes")
          }}</TabsTrigger>
          <TabsTrigger value="files">{{
            $t("pages.productEditPage.tabs.files")
          }}</TabsTrigger>
          <TabsTrigger value="images"
            ><span class="sm:hidden">{{
              $t("pages.productEditPage.tabs.imagesShort")
            }}</span
            ><span class="hidden sm:inline">{{
              $t("pages.productEditPage.tabs.images")
            }}</span></TabsTrigger
          >
          <TabsTrigger v-if="category?.hasSizes" value="sizes">{{
            $t("pages.productEditPage.tabs.sizes")
          }}</TabsTrigger>
          <TabsTrigger value="marketplaces"
            ><span class="sm:hidden">{{
              $t("pages.productEditPage.tabs.marketplacesShort")
            }}</span
            ><span class="hidden sm:inline">{{
              $t("nav.adapters")
            }}</span></TabsTrigger
          >
        </TabsList>
      </Tabs>
    </template>
    <template #nav-right>
      <DropdownMenu v-if="canManageCatalog">
        <DropdownMenuTrigger as-child>
          <Button
            variant="ghost"
            size="icon"
            class="size-9"
            :aria-label="$t('common.actions')"
          >
            <EllipsisVertical class="size-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem @select="goToDuplicate">
            <Copy />
            {{ $t("pages.productEditPage.actions.duplicate") }}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
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
            <div
              v-if="isProductLoading && productSKU"
              class="absolute inset-0 z-10 flex items-start justify-center pt-24 bg-background/60 backdrop-blur-[1px]"
            >
              <Spinner size="xl" class="text-muted-foreground" />
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
                <SettingsFormSection
                  class="max-w-none"
                  :title="$t('pages.productEditPage.sections.mainData.title')"
                  :description="
                    $t('pages.productEditPage.sections.mainData.description')
                  "
                >
                  <SettingsFormField
                    :label="$t('pages.productEditPage.fields.category.label')"
                  >
                    <div class="space-y-1.5 w-full">
                      <SearchableSelect
                        :model-value="currentCategorySelectValue"
                        :options="categorySelectOptions"
                        :placeholder="
                          $t(
                            'pages.productEditPage.fields.category.placeholder',
                          )
                        "
                        :disabled="
                          isProductLoading ||
                          isChangingCategory ||
                          isLoadingCategories
                        "
                        @update:model-value="onCategorySelectChange"
                      />
                      <router-link
                        v-if="category"
                        :to="`/categories/${category.id}/edit`"
                        class="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
                      >
                        {{
                          $t("pages.productEditPage.fields.category.editLink")
                        }}
                      </router-link>
                    </div>
                  </SettingsFormField>

                  <SettingsFormField
                    :label="$t('pages.productEditPage.fields.group.label')"
                  >
                    <div class="space-y-1.5">
                      <SearchableSelect
                        :model-value="currentGroupSelectValue"
                        :options="groupSelectOptions"
                        :placeholder="
                          $t('pages.productEditPage.fields.group.placeholder')
                        "
                        :disabled="isProductLoading || isChangingGroup"
                        @update:model-value="onGroupSelectChange"
                      >
                        <template #footer="{ close }">
                          <Button
                            variant="ghost"
                            type="button"
                            class="h-auto min-h-0 w-full justify-start rounded-md p-0 text-left hover:bg-transparent"
                            :class="MENU_ITEM"
                            @click="openCreateGroupDialogFromSelect(close)"
                          >
                            <Plus class="size-4 shrink-0" />
                            <span class="flex-1 truncate text-sm">
                              {{
                                $t(
                                  "pages.productEditPage.fields.group.createLink",
                                )
                              }}
                            </span>
                          </Button>
                        </template>
                      </SearchableSelect>
                      <router-link
                        v-if="productGroup"
                        :to="`/product-groups/${productGroup.id}/edit`"
                        class="text-muted-foreground hover:text-foreground inline-flex items-center gap-1.5 text-xs transition-colors"
                      >
                        {{ $t("pages.productEditPage.fields.group.editLink") }}
                      </router-link>
                    </div>
                  </SettingsFormField>

                  <SettingsFormField
                    :label="$t('pages.productEditPage.fields.sku.label')"
                    label-for="product-sku"
                  >
                    <div class="space-y-1.5">
                      <Input
                        id="product-sku"
                        type="text"
                        readonly
                        :model-value="productSKU"
                        class="w-full font-mono tabular-nums"
                        @click="selectSkuField"
                        @focus="selectSkuField"
                      />
                      <div
                        class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1"
                      >
                        <Button
                          variant="link"
                          size="xs"
                          type="button"
                          class="h-auto p-0 text-muted-foreground hover:text-foreground"
                          :disabled="isProductLoading"
                          @click="openSkuDialog"
                        >
                          {{
                            $t("pages.productEditPage.fields.sku.changeAction")
                          }}
                        </Button>
                        <Button
                          v-if="productSKU"
                          variant="link"
                          size="xs"
                          type="button"
                          class="h-auto p-0 text-muted-foreground hover:text-foreground"
                          @click="copySku"
                        >
                          {{
                            skuCopied
                              ? $t("common.copied")
                              : $t("pages.productEditPage.skuDialog.copyAction")
                          }}
                        </Button>
                      </div>
                    </div>
                  </SettingsFormField>

                  <FormField
                    v-slot="{ value, setValue, errorMessage }"
                    name="product.barcode"
                  >
                    <FormItem class="gap-0">
                      <SettingsFormField
                        :label="
                          $t('pages.productEditPage.fields.barcode.label')
                        "
                        label-for="product-barcode"
                        :error="errorMessage"
                      >
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

                  <FormField
                    v-slot="{ componentField, errorMessage }"
                    name="product.name"
                  >
                    <FormItem class="gap-0">
                      <SettingsFormField
                        :label="$t('pages.productEditPage.fields.name.label')"
                        label-for="product-name"
                        :error="errorMessage"
                        required
                      >
                        <FormControl>
                          <Input
                            id="product-name"
                            type="text"
                            :maxlength="FIELD_LIMITS.productName"
                            v-bind="componentField"
                          />
                        </FormControl>
                      </SettingsFormField>
                    </FormItem>
                  </FormField>

                  <FormField
                    v-slot="{ componentField, errorMessage }"
                    name="product.description"
                  >
                    <FormItem class="gap-1.5">
                      <div class="flex items-center justify-between gap-2">
                        <FieldLabel for="product-description">{{
                          $t("pages.productEditPage.fields.description.label")
                        }}</FieldLabel>
                        <AiDescriptionGenerator
                          v-if="aiEnabled"
                          :product-name="form.values.product?.name"
                          :product-sku="productSKU"
                          :category-name="category?.name"
                          :existing-description="
                            form.values.product?.description
                          "
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
                            :maxlength="FIELD_LIMITS.productDescription"
                            @input="autoResizeDescription"
                            class="min-h-24 overflow-hidden resize-none transition-opacity"
                            :class="isDescriptionGenerating ? 'opacity-60' : ''"
                          />
                        </FormControl>
                        <div
                          v-if="isDescriptionGenerating"
                          class="pointer-events-none absolute inset-0 flex items-center justify-center rounded-md bg-background/55"
                        >
                          <p
                            class="flex items-center gap-2 text-xs text-muted-foreground"
                          >
                            <Spinner size="sm" />
                            {{ descriptionGeneratingHint }}
                          </p>
                        </div>
                      </div>
                      <FieldError v-if="errorMessage">{{
                        errorMessage
                      }}</FieldError>
                    </FormItem>
                  </FormField>

                  <template v-if="!category?.hasSizes">
                    <div class="space-y-4 border-t pt-5">
                      <div>
                        <InsetGroupLabel>
                          {{ $t("pages.productEditPage.sections.stock.title") }}
                        </InsetGroupLabel>
                        <p class="px-1 text-xs text-muted-foreground">
                          {{
                            $t(
                              "pages.productEditPage.sections.stock.description",
                            )
                          }}
                        </p>
                      </div>
                      <div class="grid grid-cols-1 gap-5">
                        <FormField
                          v-slot="{ value, setValue, errorMessage }"
                          name="product.storageQuantity"
                        >
                          <FormItem class="gap-0">
                            <SettingsFormField
                              :label="
                                $t(
                                  'pages.productEditPage.fields.quantity.label',
                                )
                              "
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
                        <InsetGroupLabel>
                          {{ $t("pages.productEditPage.sections.price.title") }}
                        </InsetGroupLabel>
                        <p class="px-1 text-xs text-muted-foreground">
                          {{
                            $t(
                              "pages.productEditPage.sections.price.description",
                              {
                                currency:
                                  storeBaseCurrency ||
                                  $t(
                                    "pages.productEditPage.fields.price.defaultCurrencyFallback",
                                  ),
                              },
                            )
                          }}
                        </p>
                      </div>
                      <div class="grid grid-cols-1 gap-5">
                        <FormField
                          v-slot="{ value, setValue, errorMessage }"
                          name="product.price"
                        >
                          <FormItem class="gap-0">
                            <SettingsFormField
                              :label="
                                $t('pages.productEditPage.fields.price.label', {
                                  currency:
                                    storeBaseCurrency || $t('common.emDash'),
                                })
                              "
                              label-for="product-price"
                              :error="errorMessage"
                            >
                              <FormControl>
                                <Input
                                  id="product-price"
                                  type="number"
                                  step="0.01"
                                  min="0"
                                  placeholder="0.00"
                                  :model-value="value"
                                  @update:model-value="
                                    setValue(
                                      $event !== '' && $event != null
                                        ? Number($event)
                                        : null,
                                    )
                                  "
                                />
                              </FormControl>
                              <p
                                v-if="pricePreview(value).length"
                                class="text-xs text-muted-foreground mt-1"
                              >
                                {{ pricePreview(value).join(" · ") }}
                              </p>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                      </div>
                    </div>

                    <div
                      v-if="extendedPriceParameters"
                      class="space-y-4 border-t pt-5"
                    >
                      <div>
                        <InsetGroupLabel>
                          {{
                            $t(
                              "pages.productEditPage.sections.extendedPrice.title",
                            )
                          }}
                        </InsetGroupLabel>
                        <p class="px-1 text-xs text-muted-foreground">
                          {{
                            $t(
                              "pages.productEditPage.sections.extendedPrice.description",
                            )
                          }}
                        </p>
                      </div>
                      <div class="grid grid-cols-1 sm:grid-cols-2 gap-5">
                        <FormField
                          v-slot="{ value, setValue, errorMessage }"
                          name="product.oldPrice"
                        >
                          <FormItem class="gap-0">
                            <SettingsFormField
                              :label="
                                $t(
                                  'pages.productEditPage.fields.oldPrice.label',
                                  {
                                    currency:
                                      storeBaseCurrency || $t('common.emDash'),
                                  },
                                )
                              "
                              :error="errorMessage"
                              :description="
                                $t(
                                  'pages.productEditPage.fields.oldPrice.description',
                                )
                              "
                            >
                              <FormControl>
                                <Input
                                  type="number"
                                  step="0.01"
                                  min="0"
                                  :model-value="value"
                                  @update:model-value="
                                    setValue(
                                      $event !== '' && $event != null
                                        ? Number($event)
                                        : null,
                                    )
                                  "
                                />
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                        <FormField
                          v-slot="{ value, errorMessage }"
                          name="product.discount"
                        >
                          <FormItem class="gap-0">
                            <SettingsFormField
                              :label="
                                $t(
                                  'pages.productEditPage.fields.discount.label',
                                )
                              "
                              :error="errorMessage"
                              :description="
                                $t(
                                  'pages.productEditPage.fields.discount.description',
                                )
                              "
                            >
                              <FormControl>
                                <Input
                                  type="text"
                                  readonly
                                  class="bg-muted"
                                  :model-value="
                                    value != null ? String(value) : '-'
                                  "
                                />
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                        <FormField
                          v-slot="{ value, setValue, errorMessage }"
                          name="product.minPrice"
                        >
                          <FormItem class="gap-0">
                            <SettingsFormField
                              :label="
                                $t(
                                  'pages.productEditPage.fields.minPrice.label',
                                  {
                                    currency:
                                      storeBaseCurrency || $t('common.emDash'),
                                  },
                                )
                              "
                              :error="errorMessage"
                            >
                              <FormControl>
                                <Input
                                  type="number"
                                  step="0.01"
                                  min="0"
                                  :model-value="value"
                                  @update:model-value="
                                    setValue(
                                      $event !== '' && $event != null
                                        ? Number($event)
                                        : null,
                                    )
                                  "
                                />
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                        <FormField
                          v-slot="{ value, setValue, errorMessage }"
                          name="product.netPrice"
                        >
                          <FormItem class="gap-0">
                            <SettingsFormField
                              :label="
                                $t(
                                  'pages.productEditPage.fields.netPrice.label',
                                  {
                                    currency:
                                      storeBaseCurrency || $t('common.emDash'),
                                  },
                                )
                              "
                              :error="errorMessage"
                            >
                              <FormControl>
                                <Input
                                  type="number"
                                  step="0.01"
                                  min="0"
                                  :model-value="value"
                                  @update:model-value="
                                    setValue(
                                      $event !== '' && $event != null
                                        ? Number($event)
                                        : null,
                                    )
                                  "
                                />
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                        <FormField
                          v-slot="{ value, setValue, errorMessage }"
                          name="product.currency"
                        >
                          <FormItem class="gap-0 sm:col-span-2">
                            <SettingsFormField
                              :label="
                                $t(
                                  'pages.productEditPage.fields.currency.label',
                                )
                              "
                              :error="errorMessage"
                              :description="
                                $t(
                                  'pages.productEditPage.fields.currency.description',
                                  {
                                    currency:
                                      storeBaseCurrency ||
                                      $t(
                                        'pages.productEditPage.fields.currency.defaultCurrencyFallback',
                                      ),
                                  },
                                )
                              "
                            >
                              <FormControl>
                                <Select
                                  :model-value="value || storeBaseCurrency"
                                  @update:model-value="setValue($event)"
                                  class="w-full"
                                >
                                  <SelectTrigger class="w-full"
                                    ><SelectValue
                                  /></SelectTrigger>
                                  <SelectContent>
                                    <SelectItem
                                      v-for="c in storeCurrencies"
                                      :key="c.code"
                                      :value="c.code"
                                      >{{ c.code }}</SelectItem
                                    >
                                  </SelectContent>
                                </Select>
                              </FormControl>
                            </SettingsFormField>
                          </FormItem>
                        </FormField>
                      </div>
                    </div>
                  </template>
                </SettingsFormSection>

                <CreateProductGroupDialog
                  v-model:open="showCreateGroupDialog"
                  @created="onGroupCreated"
                />

                <!-- SKU change dialog -->
                <Dialog v-model:open="showSkuDialog">
                  <DialogContent size="sm">
                    <DialogHeader>
                      <DialogTitle>{{
                        $t("pages.productEditPage.skuDialog.title")
                      }}</DialogTitle>
                      <DialogDescription>
                        {{ $t("pages.productEditPage.skuDialog.description") }}
                      </DialogDescription>
                    </DialogHeader>
                    <FieldGroup class="gap-4">
                      <SettingsFormField
                        :label="
                          $t('pages.productEditPage.skuDialog.currentSkuLabel')
                        "
                        label-for="current-sku"
                      >
                        <div class="flex min-w-0 gap-2">
                          <Input
                            id="current-sku"
                            type="text"
                            readonly
                            :model-value="productSKU"
                            class="min-w-0 flex-1 font-mono tabular-nums"
                            @click="selectSkuField"
                            @focus="selectSkuField"
                          />
                          <Button
                            type="button"
                            variant="outline"
                            size="icon"
                            class="size-9 shrink-0"
                            :aria-label="
                              $t('pages.productEditPage.skuDialog.copyAction')
                            "
                            :disabled="!productSKU"
                            @click="copySku"
                          >
                            <Check
                              v-if="skuCopied"
                              class="size-4 text-success"
                            />
                            <Copy v-else class="size-4" />
                          </Button>
                        </div>
                      </SettingsFormField>
                      <SettingsFormField
                        :label="
                          $t('pages.productEditPage.skuDialog.newSkuLabel')
                        "
                        label-for="new-sku"
                      >
                        <Input
                          id="new-sku"
                          type="text"
                          v-model="newSkuValue"
                          :maxlength="FIELD_LIMITS.productSku"
                          :placeholder="
                            $t(
                              'pages.productEditPage.skuDialog.newSkuPlaceholder',
                            )
                          "
                          class="font-mono tabular-nums"
                          @keydown.enter.prevent="submitSkuChange"
                        />
                      </SettingsFormField>
                    </FieldGroup>
                    <DialogFooter>
                      <DialogClose as-child>
                        <Button
                          type="button"
                          variant="outline"
                          :disabled="isUpdatingSku"
                        >
                          {{ $t("common.cancel") }}
                        </Button>
                      </DialogClose>
                      <Button
                        type="button"
                        :disabled="
                          isUpdatingSku ||
                          !newSkuValue.trim() ||
                          newSkuValue.trim() === productSKU
                        "
                        @click="submitSkuChange"
                      >
                        <Spinner v-if="isUpdatingSku" class="mr-2" />
                        {{ $t("pages.productEditPage.skuDialog.changeAction") }}
                      </Button>
                    </DialogFooter>
                  </DialogContent>
                </Dialog>

                <!-- Category attributes -->
                <ProductAttributeForm
                  v-if="category && visibleCategoryAttributes.length > 0"
                  :attributes="category.attributes"
                >
                  <template #title>
                    {{
                      $t(
                        "pages.productEditPage.sections.categoryAttributes.title",
                      )
                    }}
                  </template>
                  <template #description>
                    {{
                      $t(
                        "pages.productEditPage.sections.categoryAttributes.description",
                        {
                          count: visibleCategoryAttributes.length,
                          unit: attributeCountLabel(
                            visibleCategoryAttributes.length,
                          ),
                          category: category.name,
                        },
                      )
                    }}
                  </template>
                </ProductAttributeForm>

                <!-- Store attributes -->
                <div v-if="visibleStoreAttributes.length > 0">
                  <InsetGroupLabel class="mb-1">
                    {{ $t("nav.globalAttributes") }}
                  </InsetGroupLabel>
                  <p class="mb-4 px-1 text-xs text-muted-foreground">
                    {{
                      $t(
                        "pages.productEditPage.sections.storeAttributes.description",
                        {
                          count: visibleStoreAttributes.length,
                          unit: attributeCountLabel(
                            visibleStoreAttributes.length,
                          ),
                        },
                      )
                    }}
                  </p>
                  <StoreAttributeForm :attributes="storeAttributes" />
                </div>
              </div>
            </form>

            <!-- Files tab -->
            <div
              v-show="tab == 'files'"
              class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl border h-[var(--app-fill-pane-height)]"
            >
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
            <div v-show="tab == 'images'">
              <ProductImagesEditor
                :files="files"
                v-model:images="images"
                :productId="productId"
                @preview="openLightboxByFileId"
              />
            </div>

            <!-- Sizes tab -->
            <div
              v-if="category?.hasSizes"
              v-show="tab == 'sizes'"
              class="space-y-6"
            >
              <ProductVariantsEditor :productId="productId" />
            </div>

            <!-- Marketplaces tab -->
            <div v-show="tab == 'marketplaces'">
              <ProductMarketplaceIntegrations
                :productId="productId"
                @product-updated="fetchProduct"
              />
            </div>

            <!-- Notes tab -->
            <div v-show="tab == 'notes'" class="space-y-6">
              <InsetGroup v-if="sortedNotes.length">
                <InsetGroupItem
                  v-for="note in sortedNotes"
                  :key="noteId(note)"
                  show-chevron
                  :aria-label="
                    $t('pages.productEditPage.fields.notes.openAria')
                  "
                  @click="openNote(note)"
                >
                  <template #leading>
                    <NotebookPen class="size-4" />
                  </template>
                  <p class="truncate">{{ noteRowTitle(note) }}</p>
                  <p
                    v-if="noteRowCaption(note)"
                    class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
                  >
                    {{ noteRowCaption(note) }}
                  </p>
                  <template #trailing>
                    <span
                      class="max-w-28 truncate text-xs"
                      :class="noteImportanceClass(note.importance)"
                    >
                      {{ noteRowImportance(note) }}
                    </span>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
              <InsetGroup>
                <InsetGroupItem
                  :disabled="isAddingNote || isProductLoading"
                  :aria-label="$t('pages.productEditPage.fields.notes.addLink')"
                  @click="addNote"
                >
                  <template #leading>
                    <Spinner v-if="isAddingNote" />
                    <Plus v-else class="size-4" />
                  </template>
                  {{ $t("pages.productEditPage.fields.notes.addLink") }}
                </InsetGroupItem>
              </InsetGroup>
            </div>

            <!-- In-flow actions: delete + save (duplicate lives in nav ⋯ menu) -->
            <div
              v-show="tab === 'properties' && productSKU && canManageCatalog"
              class="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
            >
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button variant="destructive" type="button">
                    <Trash2 class="size-4 mr-2" />
                    {{ $t("common.delete") }}
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{{
                      $t("pages.productEditPage.deleteConfirm.title", {
                        sku: productSKU,
                      })
                    }}</AlertDialogTitle>
                    <AlertDialogDescription>
                      {{
                        $t("pages.productEditPage.deleteConfirm.description")
                      }}
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{{
                      $t("common.cancel")
                    }}</AlertDialogCancel>
                    <AlertDialogAction
                      @click="handleDeleteProduct"
                      variant="destructive"
                    >
                      {{
                        $t("pages.productEditPage.deleteConfirm.confirmAction")
                      }}
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
              <div class="flex items-center justify-end gap-3">
                <p
                  v-if="hasUnsavedChanges"
                  class="text-sm text-muted-foreground"
                >
                  {{ $t("pages.productEditPage.unsavedChanges") }}
                </p>
                <TooltipProvider :delay-duration="200">
                  <Tooltip
                    :disabled="
                      !isSaveDisabled || saveDisabledReasons.length === 0
                    "
                  >
                    <TooltipTrigger as-child>
                      <span
                        class="inline-flex"
                        :tabindex="isSaveDisabled ? 0 : undefined"
                      >
                        <Button
                          type="submit"
                          form="product-edit-form"
                          aria-keyshortcuts="Control+s Meta+s"
                          :disabled="isSaveDisabled"
                        >
                          <Spinner v-if="isSubmitting" class="mr-2" />
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

    <!-- Lightbox overlay -->
    <Teleport to="body">
      <div
        v-if="lightboxIndex != null"
        class="fixed inset-0 z-[var(--z-overlay)] flex items-center justify-center bg-black/50 backdrop-blur-xs"
        @click.self="closeLightbox"
      >
        <!-- Close button -->
        <Button
          variant="outline"
          size="icon-lg"
          class="absolute top-4 right-4 rounded-md bg-background/90 supports-backdrop-filter:backdrop-blur-xs"
          :aria-label="$t('common.close')"
          @click="closeLightbox"
        >
          <X class="size-5" />
        </Button>
        <!-- Prev -->
        <Button
          v-if="lightboxIndex > 0"
          variant="outline"
          size="icon-lg"
          class="absolute left-4 rounded-md bg-background/90 supports-backdrop-filter:backdrop-blur-xs"
          :aria-label="$t('pages.productEditPage.lightbox.prevPhoto')"
          @click="prevImage"
        >
          <ChevronLeft class="size-6" />
        </Button>
        <!-- Image -->
        <AppImage
          :src="`/api/files/${lightboxImages[lightboxIndex]}/download`"
          :alt="$t('pages.productEditPage.lightbox.productPhotoAlt')"
          spinner-size="xl"
          class="flex max-h-[90vh] max-w-[90vw] items-center justify-center"
          img-class="max-h-[90vh] max-w-[90vw] object-contain"
        />
        <!-- Next -->
        <Button
          v-if="lightboxIndex < lightboxImages.length - 1"
          variant="outline"
          size="icon-lg"
          class="absolute right-4 rounded-md bg-background/90 supports-backdrop-filter:backdrop-blur-xs"
          :aria-label="$t('pages.productEditPage.lightbox.nextPhoto')"
          @click="nextImage"
        >
          <ChevronRight class="size-6" />
        </Button>
        <!-- Counter -->
        <div
          class="absolute bottom-4 rounded-md border bg-background/90 px-3 py-1 text-sm tabular-nums text-foreground shadow-xs supports-backdrop-filter:backdrop-blur-xs"
        >
          {{ lightboxIndex + 1 }} / {{ lightboxImages.length }}
        </div>
      </div>
    </Teleport>
    <NoteDetailSheet
      v-model:open="noteSheetOpen"
      :note="selectedNote"
      :show-product-link="false"
      @updated="onNoteUpdated"
      @deleted="onNoteDeleted"
    />
  </AppPageChrome>
</template>
