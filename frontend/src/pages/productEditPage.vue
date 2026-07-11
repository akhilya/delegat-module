<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
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
import Separator from "@/components/ui/separator/Separator.vue";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import Note from "@/components/note.vue";

import {
  Loader2,
  Save,
  Trash2,
  NotebookPen,
  FolderOpen,
  Globe,
  Layers,
  ExternalLink,
  Check,
  Pencil,
  ArrowRightLeft,
  Plus,
  Copy,
  X,
  ChevronLeft,
  ChevronRight,
} from "lucide-vue-next";
import { PAGE_PRODUCTS, setPageTitle } from "@/router";
import { useStore } from "vuex";
import { useRoute } from "vue-router";
import ProductGroupSidebar from "@/components/products/ProductGroupSidebar.vue";
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from "vue";
import { toTypedSchema } from "@vee-validate/zod";
import { useForm } from "vee-validate";
import { z } from "zod";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useDebounceFn } from "@vueuse/core";
import draggable from "vuedraggable";
import { http, router } from "@/main";

export default {
  components: {
    Page,
    PageSection,
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
    Label,
    ProductFileManager,
    ProductImagesEditor,
    ProductAttributeForm,
    StoreAttributeForm,
    ProductMarketplaceIntegrations,
    ProductVariantsEditor,
    ProductGroupSidebar,
    BarcodeInput,
    SearchableSelect,
    Separator,
    Card,
    CardContent,
    CardHeader,
    CardTitle,
    Loader2,
    Save,
    Trash2,
    NotebookPen,
    FolderOpen,
    Globe,
    Pencil,
    Note,
    Layers,
    ExternalLink,
    Check,
    ArrowRightLeft,
    Plus,
    Copy,
    X,
    ChevronLeft,
    ChevronRight,
    ContextMenu,
    ContextMenuContent,
    ContextMenuItem,
    ContextMenuTrigger,
    draggable,
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
    // Снапшот productId в момент постановки автосейва в очередь.
    // Защищает от race: пользователь правит товар A, переходит на B
    // до срабатывания debounce — autosave не отправит данные A на B.
    const pendingSaveProductId = ref(null);
    const pendingSaveImagesProductId = ref(null);
    const isDeletingFile = ref(false);
    const showSkuDialog = ref(false);
    const newSkuValue = ref("");
    const isUpdatingSku = ref(false);
    const showGroupDialog = ref(false);
    const allGroups = ref([]);
    const selectedGroupId = ref(null);
    const isChangingGroup = ref(false);
    const descriptionRef = ref(null);
    const groupSidebarRef = ref(null);
    const mainScrollRef = ref(null);

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

      const savedPageScroll = window.scrollY;
      const lockScroll = () => window.scrollTo(0, savedPageScroll);
      window.addEventListener('scroll', lockScroll);
      fetchProduct().then(() => {
        nextTick(() => {
          window.removeEventListener('scroll', lockScroll);
          window.scrollTo(0, savedPageScroll);
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
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isLoadingStoreAttributes.value = false;
    }

    fetchStoreAttributes();
    fetchProduct();
    fetchProductGroup(true);

    async function fetchProduct() {
      // Подавляем autosave-watcher'ы пока fetch не закончится и данные не запишутся.
      // Без этого мутация attributeValues внутри onSuccess триггерит autosave с неверным контекстом.
      isInitialLoad.value = true;
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
          productId: productId.value,
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
      // ProductId snapshot guard: если за время debounce пользователь ушёл
      // на другой товар, не отправляем устаревшие данные на новый productId.
      if (pendingSaveProductId.value !== productId.value) return;
      await saveProduct(false);
    }, 1500);

    // Watch for form changes and trigger autosave
    watch(
      () => form.values,
      () => {
        if (isInitialLoad.value) return;
        hasUnsavedChanges.value = true;
        pendingSaveProductId.value = productId.value;
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
        pendingSaveProductId.value = productId.value;
        debouncedAutoSave();
      },
      { deep: true }
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

    async function openGroupDialog() {
      showGroupDialog.value = true;
      selectedGroupId.value = productGroup.value?.id ? String(productGroup.value.id) : null;
      await store.dispatch("getAllGroups", {
        onSuccess: (response) => {
          allGroups.value = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    }

    const groupOptions = computed(() =>
      allGroups.value.map((g) => ({
        value: String(g.id),
        label: g.name,
      }))
    );

    const duplicateProductUrl = computed(() => getDuplicateUrl(productId.value));

    function getDuplicateUrl(sourceProductId) {
      const params = new URLSearchParams();
      params.set('fromProduct', String(sourceProductId));
      if (productGroup.value?.id) params.set('groupId', String(productGroup.value.id));
      return `/products/create?${params.toString()}`;
    }

    async function submitGroupChange() {
      const newId = selectedGroupId.value ? parseInt(selectedGroupId.value) : null;
      const oldId = productGroup.value?.id || null;
      if (newId === oldId) { showGroupDialog.value = false; return; }

      isChangingGroup.value = true;
      try {
        if (oldId) {
          await http.delete(`/productgroups/${oldId}/products/${productId.value}`);
        }
        if (newId) {
          await http.post(`/productgroups/${newId}/products`, { productIds: [productId.value] });
        }
        showGroupDialog.value = false;
        displaySonnerSuccess(newId ? "Группа изменена" : "Товар убран из группы");
        await fetchProductGroup();
      } catch (error) {
        displaySonnerError(error);
      } finally {
        isChangingGroup.value = false;
      }
    }

    // Image gallery lightbox & reordering
    const lightboxIndex = ref(null);
    const sortedImages = ref([]);

    watch(images, (val) => {
      sortedImages.value = val?.slice().sort((a, b) => a.index - b.index) ?? [];
    }, { immediate: true });

    const debouncedSaveImages = useDebounceFn(async () => {
      // Guard: если за 400мс пользователь ушёл на другой товар, не отправляем
      // картинки товара A на endpoint товара B.
      if (pendingSaveImagesProductId.value !== productId.value) return;
      const fileIds = sortedImages.value.map((img) => img.fileId);
      await store.dispatch("postProductImages", {
        productId: productId.value,
        files: fileIds,
        onSuccess: () => {},
        onError: (error) => { displaySonnerError(error); },
      });
    }, 400);

    function onGalleryDragChange() {
      // Update indices after drag
      sortedImages.value.forEach((img, idx) => { img.index = idx; });
      // Sync back to images ref
      images.value = sortedImages.value.map((img) => ({ ...img }));
      pendingSaveImagesProductId.value = productId.value;
      debouncedSaveImages();
    }

    function removeGalleryImage(index) {
      sortedImages.value.splice(index, 1);
      sortedImages.value.forEach((img, idx) => { img.index = idx; });
      images.value = sortedImages.value.map((img) => ({ ...img }));
      pendingSaveImagesProductId.value = productId.value;
      debouncedSaveImages();
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

    onMounted(() => window.addEventListener('keydown', handleLightboxKeydown));
    onUnmounted(() => {
      window.removeEventListener('keydown', handleLightboxKeydown);
      store.commit('aiChat/clearPageContext');
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
      attributeValues,
      storeAttributes,
      storeAttributeValues,
      isLoadingStoreAttributes,
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
      showGroupDialog,
      allGroups,
      selectedGroupId,
      isChangingGroup,
      openGroupDialog,
      groupOptions,
      submitGroupChange,
      descriptionRef,
      groupSidebarRef,
      mainScrollRef,
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
  <Page>
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
    <!-- Mobile group link (visible when sidebar is hidden) -->
    <div v-if="productGroup" class="lg:hidden flex items-center gap-2 mb-3 text-sm">
      <Layers class="size-4 text-muted-foreground" />
      <router-link :to="`/product-groups/${productGroup.id}/edit`" class="text-muted-foreground hover:text-foreground transition-colors">
        {{ productGroup.name }}
      </router-link>
      <span class="text-muted-foreground/50">&middot;</span>
      <span class="text-muted-foreground">{{ productGroup.members?.length || 0 }} товаров</span>
    </div>
    <div v-else class="lg:hidden flex items-center gap-2 mb-3 text-sm">
      <Layers class="size-4 text-muted-foreground/50" />
      <Button type="button" variant="link" class="h-auto p-0 text-muted-foreground" @click="openGroupDialog">
        Выбрать группу
      </Button>
    </div>
    <PageSection :loading="isProductLoading && !productSKU" class="flex gap-6" style="min-height: calc(100vh - 8.5rem)">
      <!-- Left: Product Group sidebar -->
      <ProductGroupSidebar
        ref="groupSidebarRef"
        :group="productGroup"
        :loading="isLoadingGroup"
        :disabled="isProductLoading"
        :add-url="duplicateProductUrl"
        :tab="tab"
        :show-change-group="true"
        :get-duplicate-url="getDuplicateUrl"
        @change-group="openGroupDialog"
      />
      <!-- Main content -->
      <div ref="mainScrollRef" class="grow flex flex-col min-w-0 relative">
        <div v-if="isProductLoading && productSKU" class="absolute inset-0 z-10 flex items-start justify-center pt-24 bg-background/60 backdrop-blur-[1px]">
          <Loader2 class="size-6 animate-spin text-muted-foreground" />
        </div>
        <div class="grow flex flex-col">
          <form
            v-if="form"
            v-show="tab == 'properties'"
            @submit.prevent="onSubmit"
            class="flex flex-col gap-6"
          >
            <!-- Image gallery (above main fields card) -->
            <input
              ref="galleryFileInput"
              type="file"
              accept="image/*"
              multiple
              class="hidden"
              @change="handleGalleryUpload"
            />
            <draggable
              v-if="sortedImages?.length"
              :list="sortedImages"
              item-key="fileId"
              :animation="200"
              ghost-class="drag-ghost"
              chosen-class="drag-chosen"
              drag-class="drag-active"
              class="flex flex-wrap gap-2"
              @change="onGalleryDragChange"
            >
              <template #item="{ element, index }">
                <div
                  :class="[
                    'gallery-item group size-32 sm:size-40 lg:size-48 rounded-lg overflow-hidden bg-secondary cursor-grab active:cursor-grabbing hover:ring-2 ring-primary relative',
                    index === 0 ? 'ring-2 ring-primary' : ''
                  ]"
                  @click="openLightbox(index)"
                >
                  <img
                    :src="`/api/files/${element.fileId}/thumbnail?w=256`"
                    :alt="element.displayName || 'Фото товара'"
                    class="size-full object-contain pointer-events-none"
                  />
                  <div v-if="index === 0" class="absolute top-1 left-1 bg-primary text-primary-foreground text-xs px-1.5 py-0.5 rounded font-medium">
                    Главное
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    class="absolute top-1 right-1 size-5 rounded-full bg-black/60 hover:bg-destructive text-white opacity-0 group-hover:opacity-100 transition-opacity"
                    @click.stop="removeGalleryImage(index)"
                  >
                    <X class="size-3" />
                  </Button>
                </div>
              </template>
              <template #footer>
                <div
                  class="size-32 sm:size-40 lg:size-48 rounded-lg border-2 border-dashed border-muted-foreground/30 hover:border-primary/50 flex items-center justify-center cursor-pointer transition-colors"
                  @click="triggerGalleryUpload"
                >
                  <Loader2 v-if="isUploadingGallery" class="size-6 text-muted-foreground animate-spin" />
                  <Plus v-else class="size-6 text-muted-foreground" />
                </div>
              </template>
            </draggable>
            <div v-else class="flex flex-wrap gap-2">
              <div
                class="size-32 sm:size-40 lg:size-48 rounded-lg border-2 border-dashed border-muted-foreground/30 hover:border-primary/50 flex items-center justify-center cursor-pointer transition-colors"
                @click="triggerGalleryUpload"
              >
                <Loader2 v-if="isUploadingGallery" class="size-6 text-muted-foreground animate-spin" />
                <Plus v-else class="size-6 text-muted-foreground" />
              </div>
            </div>

            <!-- Card 1: Main fields + Group sidebar -->
            <Card>
              <CardContent class="pt-0">
                <!-- Left: fields -->
                <div class="space-y-4">
                  <!-- Category (read-only) -->
                  <div v-if="category" class="space-y-2">
                    <Label class="text-muted-foreground">Категория</Label>
                    <div class="flex items-center gap-2 h-9 px-3 border rounded-md text-muted-foreground">
                      <FolderOpen class="size-4" />
                      <span class="text-sm">{{ category.name }}</span>
                    </div>
                  </div>

                  <!-- SKU with edit button -->
                  <div class="space-y-2">
                    <Label class="text-muted-foreground">Артикул</Label>
                    <div class="flex items-center gap-1.5">
                      <Input type="text" disabled :model-value="productSKU" />
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
                  </div>

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
                        <div class="space-y-1">
                          <Label class="text-sm text-muted-foreground">Текущий артикул</Label>
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

                  <FormField v-slot="{ componentField }" name="product.name">
                    <FormItem>
                      <FormLabel>Название <span class="text-destructive">*</span></FormLabel>
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
                        <Textarea ref="descriptionRef" v-bind="componentField" @input="autoResizeDescription" class="overflow-hidden resize-none" />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  </FormField>

                  <!-- Price and Quantity in a row (hidden when category has sizes — managed per variant) -->
                  <div v-if="!category?.hasSizes" class="grid grid-cols-1 md:grid-cols-2 gap-4">
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
                </div>

              </CardContent>
            </Card>

            <!-- Group change dialog -->
            <Dialog v-model:open="showGroupDialog">
              <DialogContent size="md">
                <DialogHeader>
                  <DialogTitle>Сменить группу товаров</DialogTitle>
                  <DialogDescription>
                    Товар будет перемещён в выбранную группу.
                  </DialogDescription>
                </DialogHeader>
                <div class="py-4">
                  <SearchableSelect
                    :model-value="selectedGroupId"
                    @update:model-value="selectedGroupId = $event"
                    :options="groupOptions"
                    placeholder="Выберите группу..."
                  />
                </div>
                <DialogFooter>
                  <DialogClose as-child><Button variant="outline" :disabled="isChangingGroup">Отмена</Button></DialogClose>
                  <Button
                    @click="submitGroupChange"
                    :disabled="isChangingGroup"
                    type="button"
                  >
                    <Loader2 v-if="isChangingGroup" class="size-4 mr-2 animate-spin" />
                    Применить
                  </Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

          <!-- Card 2: Category Attributes -->
          <Card v-if="category">
            <CardHeader>
              <CardTitle class="text-base flex items-center gap-2">
                <Layers class="size-4" />
                Атрибуты категории
                <span v-if="category.attributes?.length" class="text-muted-foreground text-sm font-normal">
                  ({{ category.attributes.length }})
                </span>
              </CardTitle>
            </CardHeader>
            <CardContent>
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
            </CardContent>
          </Card>

          <!-- Card 3: Store Attributes -->
          <Card v-if="storeAttributes.length > 0">
            <CardHeader>
              <CardTitle class="text-base flex items-center gap-2">
                <Globe class="size-4" />
                Глобальные атрибуты
                <span class="text-muted-foreground text-sm font-normal">
                  ({{ storeAttributes.length }})
                </span>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <StoreAttributeForm
                :attributes="storeAttributes"
                :model-value="storeAttributeValues"
                :errors="storeAttributeErrors"
                @update:model-value="updateStoreAttributeValues"
              />
            </CardContent>
          </Card>

          <!-- Actions row: fixed at bottom on mobile, sticky on desktop -->
          <div class="flex items-center justify-between fixed sm:sticky bottom-0 left-0 right-0 sm:left-auto sm:right-auto bg-background py-3 px-4 sm:px-0 sm:-mx-px border-t mt-2 z-20">
            <div class="flex items-center gap-2">
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button variant="destructive" type="button">
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
              <Button variant="outline" type="button" @click="$router.push(duplicateProductUrl)">
                <Copy class="size-4 mr-2" />
                Дублировать
              </Button>
            </div>

            <div class="flex items-center gap-4">
              <!-- Save status indicator -->
              <div v-if="lastSavedAt" class="text-sm text-muted-foreground flex items-center gap-2">
                <Check class="size-4 text-success" />
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
          <!-- Header -->
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
          <!-- Content -->
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
      </div>

    </PageSection>
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

<style>
.drag-ghost {
  opacity: 0.3;
  border: 2px dashed hsl(var(--primary));
  border-radius: 0.5rem;
}
.drag-chosen {
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  border-radius: 0.5rem;
}
.drag-active {
  opacity: 1 !important;
  transform: scale(1.05) rotate(2deg);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.2);
  border-radius: 0.5rem;
  cursor: grabbing !important;
  z-index: 50;
}
</style>
