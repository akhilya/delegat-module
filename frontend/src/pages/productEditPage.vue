<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
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
  mergeCategoryAttributesIntoCache,
} from "@/lib/productAttributeValidation";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { http, router } from "@/main";
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
    const productGroup = ref(null);
    const isLoadingGroup = ref(false);
    const visibleCategoryAttributes = computed(() =>
      category.value?.attributes?.filter((a) => a.showOnProductPage !== false) ?? [],
    );
    const visibleStoreAttributes = computed(() =>
      storeAttributes.value.filter((a) => a.showOnProductPage !== false),
    );

    const formSchema = computed(() => toTypedSchema(
      z.object({
        product: z.object({
          name: z.string().min(1, "Название обязательно").max(100),
          description: z.string().max(5000).optional(),
          barcode: z.string().max(13).optional().nullable(),
          storageQuantity: z.number().int().min(0, "Количество не может быть отрицательным").optional().nullable(),
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
          name: "",
          description: "",
          barcode: "",
          storageQuantity: null,
          price: null,
        },
        categoryAttributes: {},
        storeAttributes: {},
      },
    });
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
      if (mod100 >= 11 && mod100 <= 14) return "атрибутов";
      if (mod10 === 1) return "атрибут";
      if (mod10 >= 2 && mod10 <= 4) return "атрибута";
      return "атрибутов";
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


    watch(
      () => store.state.aiChat.productRefreshToken,
      () => fetchProduct()
    );

    watch(productId, (newId) => {
      // Если новый товар — в той же группе, просто обновляем isCurrent на клиенте.
      // Это сохраняет скролл сайдбара и не вызывает перерисовку списка.
      // Если товар из другой группы — перезагружаем группу полностью.
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
          // Stale-response guard: пока запрос летел, пользователь мог уйти на другой товар.
          // Применять данные A на UI/state товара B нельзя.
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
            // Если productId сменился за эти 100мс — оставляем isInitialLoad=true,
            // следующий fetchProduct сам сбросит его в нужный момент.
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
        title: "Несохранённые изменения",
        description: "Есть несохранённые изменения. Уйти без сохранения?",
        confirmLabel: "Уйти",
        cancelLabel: "Остаться",
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
            displaySonnerError(error);
          },
        });
        if (!updateOk) return false;

        const imagesOk = await persistImages();
        if (!imagesOk) return false;

        await syncProductAfterSave();
        hasUnsavedChanges.value = false;
        if (showSuccessToast) {
          displaySonnerSuccess("Товар успешно обновлён.");
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
          displaySonnerSuccess("Товар успешно удалён.");
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
          displaySonnerSuccess("Новая заметка добавлена.");
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
              `Артикул изменён локально. Ошибки на адаптерах (${failed.length}):\n${details}`,
            );
          } else if (mpResults.length > 0) {
            displaySonnerSuccess(
              `Артикул изменён локально и на ${mpResults.length} адаптер(ах)`,
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

    const groupSelectOptions = computed(() => [
      { value: GROUP_NONE, label: "Без группы" },
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
        displaySonnerSuccess(newId ? "Группа изменена" : "Товар убран из группы");
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
        title: "Удалить фото из галереи?",
        description: "Изображение будет откреплено от товара. Файл останется в хранилище.",
        confirmLabel: "Удалить",
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
      // Замораживаем productId на момент старта загрузки. Если пользователь
      // уйдёт на другой товар во время аплоада — все последующие шаги
      // (PUT files / POST images / fetch) направим на исходный товар,
      // а итоговый fetchProduct делаем только если пользователь не ушёл.
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
        // sortedImages — это reactive ref, и если пользователь успел уйти
        // на другой товар, sortedImages уже относится к другому товару.
        // Поэтому пересчитываем relative-к-targetProductId список с осторожностью:
        // отправляем только что-то осмысленное только если контекст не сменился.
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
          // Контекст ушёл — прикрепляем хотя бы новые файлы к их исходному товару.
          // Без этого загруженные файлы остаются "осиротевшими" в галерее.
          // Загружаем актуальный список картинок исходного товара и дописываем новые.
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

        // Refresh product to sync everything — но только если контекст не сменился,
        // иначе перезапишем UI чужого товара.
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

    onMounted(() => {
      window.addEventListener("keydown", handleLightboxKeydown);
      window.addEventListener("beforeunload", onBeforeUnload);
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
      isProductLoading,
      files,
      deleteFile,
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
  <Page class="!min-h-0">
    <NavigationBar>
      <template #center>
        <Tabs v-model="tab">
          <TabsList>
            <TabsTrigger value="properties">Свойства</TabsTrigger>
            <TabsTrigger value="notes">Заметки</TabsTrigger>
            <TabsTrigger value="files">Файлы</TabsTrigger>
            <TabsTrigger value="images"><span class="sm:hidden">Фото</span><span class="hidden sm:inline">Изображения</span></TabsTrigger>
            <TabsTrigger v-if="category?.hasSizes" value="sizes">Размеры</TabsTrigger>
            <TabsTrigger value="marketplaces"><span class="sm:hidden">АД</span><span class="hidden sm:inline">Адаптеры</span></TabsTrigger>
          </TabsList>
        </Tabs>
      </template>
    </NavigationBar>
    <Breadcrumbs :items="breadcrumbs" />
    <PageSection :loading="isProductLoading && !productSKU" class="!min-h-0 !grow-0 mb-0">
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
                  Основные данные
                </CardTitle>
                <CardDescription>
                  Артикул, штрихкод, название и цена. Количество и цена по размерам — на вкладке «Размеры».
                </CardDescription>
              </CardHeader>
              <CardContent>
                <FieldGroup class="gap-5">
                  <SettingsFormField label="Категория">
                    <div class="space-y-1.5 w-full">
                      <SearchableSelect
                        :model-value="currentCategorySelectValue"
                        :options="categorySelectOptions"
                        placeholder="Выберите категорию..."
                        :disabled="isProductLoading || isChangingCategory || isLoadingCategories"
                        @update:model-value="onCategorySelectChange"
                      />
                      <router-link
                        v-if="category"
                        :to="`/categories/${category.id}/edit`"
                        class="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
                      >
                        Редактировать категорию
                      </router-link>
                    </div>
                  </SettingsFormField>

                  <SettingsFormField label="Группа товаров">
                    <div class="space-y-1.5">
                      <SearchableSelect
                        :model-value="currentGroupSelectValue"
                        :options="groupSelectOptions"
                        placeholder="Выберите группу..."
                        :disabled="isProductLoading || isChangingGroup"
                        @update:model-value="onGroupSelectChange"
                      />
                      <router-link
                        v-if="productGroup"
                        :to="`/product-groups/${productGroup.id}/edit`"
                        class="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
                      >
                        Редактировать группу
                      </router-link>
                    </div>
                  </SettingsFormField>

                  <SettingsFormField label="Артикул" label-for="product-sku">
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
                        title="Изменить артикул"
                        aria-label="Изменить артикул"
                        type="button"
                        @click="openSkuDialog"
                      >
                        <Pencil class="size-4" />
                      </Button>
                    </div>
                  </SettingsFormField>

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

                  <div v-if="!category?.hasSizes" class="grid grid-cols-1 sm:grid-cols-2 gap-5">
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

            <!-- SKU change dialog -->
            <Dialog v-model:open="showSkuDialog">
              <DialogContent size="md">
                <DialogHeader>
                  <DialogTitle>Изменить артикул</DialogTitle>
                  <DialogDescription>
                    Артикул будет изменён локально и на всех привязанных адаптерах.
                  </DialogDescription>
                </DialogHeader>
                <div class="space-y-4 py-4">
                  <SettingsFormField label="Текущий артикул">
                    <Input type="text" disabled :model-value="productSKU" class="font-mono" />
                  </SettingsFormField>
                  <SettingsFormField label="Новый артикул" label-for="new-sku">
                    <Input
                      id="new-sku"
                      type="text"
                      v-model="newSkuValue"
                      maxlength="50"
                      placeholder="Введите новый артикул"
                      class="font-mono"
                      @keydown.enter.prevent="submitSkuChange"
                    />
                  </SettingsFormField>
                </div>
                <DialogFooter>
                  <DialogClose as-child><Button variant="outline" :disabled="isUpdatingSku">Отмена</Button></DialogClose>
                  <Button
                    @click="submitSkuChange"
                    :disabled="isUpdatingSku || !newSkuValue.trim() || newSkuValue.trim() === productSKU"
                  >
                    <Loader2 v-if="isUpdatingSku" class="size-4 mr-2 animate-spin" />
                    Изменить
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
                  Атрибуты категории
                </CardTitle>
                <CardDescription>
                  {{ visibleCategoryAttributes.length }} {{ attributeCountLabel(visibleCategoryAttributes.length) }}.
                  Значения из настроек категории «{{ category.name }}».
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
                  Глобальные атрибуты
                </CardTitle>
                <CardDescription>
                  {{ visibleStoreAttributes.length }} {{ attributeCountLabel(visibleStoreAttributes.length) }}.
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
                  Заметки
                </p>
                <Button variant="ghost" size="sm" @click="addNote" class="h-7">
                  <Plus class="size-4 mr-1" />
                  Добавить
                </Button>
              </div>
              <div class="flex-1 overflow-y-auto">
                <EmptyState v-if="notes.length === 0" title="Нет заметок" description="Заметки помогают фиксировать важную информацию о товаре">
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
        <div class="w-full max-w-[90rem] flex items-stretch gap-4 lg:gap-5 pointer-events-none">
          <div class="w-96 shrink-0 hidden lg:block" aria-hidden="true" />
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
                  Дублировать
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
                      Удалить
                    </Button>
                  </AlertDialogTrigger>
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>Удалить товар {{ productSKU }}?</AlertDialogTitle>
                      <AlertDialogDescription>
                        Товар будет перемещён в корзину. Его можно будет восстановить.
                      </AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                      <AlertDialogCancel>Отмена</AlertDialogCancel>
                      <AlertDialogAction @click="handleDeleteProduct" class="bg-destructive text-white hover:bg-destructive/90">
                        В корзину
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
                  Несохранённые изменения
                </p>
                <Button
                  type="submit"
                  form="product-edit-form"
                  size="sm"
                  :disabled="isSubmitting || !hasUnsavedChanges"
                >
                  <Loader2 v-if="isSubmitting" class="size-4 mr-2 animate-spin" />
                  <Save v-else class="size-4 mr-2" />
                  Сохранить
                </Button>
              </div>
            </div>
          </Card>
          <div class="w-64 shrink-0 hidden lg:block" aria-hidden="true" />
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
        <Button variant="ghost" size="icon" class="absolute top-4 right-4 text-white/70 hover:text-white hover:bg-transparent" aria-label="Закрыть" @click="closeLightbox">
          <X class="size-8" />
        </Button>
        <!-- Prev -->
        <Button v-if="lightboxIndex > 0" variant="ghost" size="icon" class="absolute left-4 text-white/70 hover:text-white hover:bg-transparent" aria-label="Предыдущее фото" @click="prevImage">
          <ChevronLeft class="size-10" />
        </Button>
        <!-- Image -->
        <img
          :src="`/api/files/${lightboxImages[lightboxIndex]}/download`"
          alt="Фото товара"
          class="max-h-[90vh] max-w-[90vw] object-contain"
        />
        <!-- Next -->
        <Button v-if="lightboxIndex < lightboxImages.length - 1" variant="ghost" size="icon" class="absolute right-4 text-white/70 hover:text-white hover:bg-transparent" aria-label="Следующее фото" @click="nextImage">
          <ChevronRight class="size-10" />
        </Button>
        <!-- Counter -->
        <div class="absolute bottom-4 text-white/60 text-sm">
          {{ lightboxIndex + 1 }} / {{ lightboxImages.length }}
        </div>
      </div>
    </Teleport>
  </Page>
</template>
