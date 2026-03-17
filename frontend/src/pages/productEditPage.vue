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
    ProductFileManager,
    ProductImagesEditor,
    ProductAttributeForm,
    StoreAttributeForm,
    ProductMarketplaceIntegrations,
    ProductVariantsEditor,
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
    const isDeletingFile = ref(false);
    const showSkuDialog = ref(false);
    const newSkuValue = ref("");
    const isUpdatingSku = ref(false);
    const showGroupDialog = ref(false);
    const allGroups = ref([]);
    const selectedGroupId = ref(null);
    const isChangingGroup = ref(false);
    const descriptionRef = ref(null);
    const sidebarScrollRef = ref(null);
    const mainScrollRef = ref(null);

    // Reload product data when switching between group members (component is reused)
    // Preserve both sidebar and page scroll positions across navigation
    watch(productId, () => {
      const savedSidebarScroll = sidebarScrollRef.value?.scrollTop ?? 0;
      const savedPageScroll = window.scrollY;

      // Lock scroll position while data loads to prevent jumps
      const lockScroll = () => window.scrollTo(0, savedPageScroll);
      window.addEventListener('scroll', lockScroll);

      fetchProduct().then(() => {
        nextTick(() => {
          window.removeEventListener('scroll', lockScroll);
          if (sidebarScrollRef.value) {
            sidebarScrollRef.value.scrollTop = savedSidebarScroll;
          }
          window.scrollTo(0, savedPageScroll);
        });
      });
      fetchProductGroup();
    });

    function autoResizeDescription() {
      const ref = descriptionRef.value;
      const el = ref?.$el ?? ref;
      if (!el || el.tagName !== 'TEXTAREA') return;
      el.style.height = 'auto';
      el.style.height = el.scrollHeight + 'px';
    }

    // Fetch product group
    async function fetchProductGroup() {
      isLoadingGroup.value = true;
      await store.dispatch("getProductGroup", {
        productId: productId.value,
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
        productId: productId.value,
        onSuccess: (response) => {
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

          // Mark initial load as complete after a short delay,
          // and auto-resize description once value is rendered
          setTimeout(() => {
            autoResizeDescription();
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
      debouncedSaveImages();
    }

    function removeGalleryImage(index) {
      sortedImages.value.splice(index, 1);
      sortedImages.value.forEach((img, idx) => { img.index = idx; });
      images.value = sortedImages.value.map((img) => ({ ...img }));
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
      try {
        // Upload each file
        const newFileIds = [];
        for (const file of fileList) {
          const formData = new FormData();
          formData.append('files', file);
          const response = await http({
            url: `/products/${productId.value}/files`,
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

        // Attach new files as images at end of current list
        const existingFileIds = sortedImages.value.map((img) => img.fileId);
        const allFileIds = [...existingFileIds, ...newFileIds];
        await store.dispatch("postProductImages", {
          productId: productId.value,
          files: allFileIds,
          onSuccess: () => {},
          onError: (error) => { displaySonnerError(error); },
        });

        // Refresh product to sync everything
        await fetchProduct();
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
    onUnmounted(() => window.removeEventListener('keydown', handleLightboxKeydown));

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
      sidebarScrollRef,
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
            <TabsTrigger value="images">Изображения</TabsTrigger>
            <TabsTrigger v-if="category?.hasSizes" value="sizes">Размеры</TabsTrigger>
            <TabsTrigger value="marketplaces">Маркетплейсы</TabsTrigger>
          </TabsList>
        </Tabs>
      </template>
    </NavigationBar>
    <Breadcrumbs :items="breadcrumbs" />
    <!-- Mobile tabs (visible below sm where center slot is hidden) -->
    <div class="sm:hidden mb-4 overflow-x-auto">
      <Tabs v-model="tab">
        <TabsList>
          <TabsTrigger value="properties">Свойства</TabsTrigger>
          <TabsTrigger value="notes">Заметки</TabsTrigger>
          <TabsTrigger value="files">Файлы</TabsTrigger>
          <TabsTrigger value="images">Фото</TabsTrigger>
          <TabsTrigger v-if="category?.hasSizes" value="sizes">Размеры</TabsTrigger>
          <TabsTrigger value="marketplaces">МП</TabsTrigger>
        </TabsList>
      </Tabs>
    </div>
    <!-- Mobile group link (visible when sidebar is hidden) -->
    <div v-if="productGroup" class="lg:hidden flex items-center gap-2 mb-3 text-sm">
      <Layers class="size-4 text-muted-foreground" />
      <router-link :to="`/product-groups/${productGroup.id}/edit`" class="text-muted-foreground hover:text-foreground transition-colors">
        {{ productGroup.name }}
      </router-link>
      <span class="text-muted-foreground/50">&middot;</span>
      <span class="text-muted-foreground">{{ productGroup.members?.length || 0 }} товаров</span>
    </div>
    <div class="w-full flex gap-6 mb-4" style="min-height: calc(100vh - 8.5rem)">
      <!-- Left: Product Group sidebar -->
      <div v-if="productGroup" class="w-80 shrink-0 hidden lg:flex flex-col border rounded-xl overflow-hidden sticky top-[5.5rem] self-start" style="height: calc(100vh - 8.5rem)">
        <!-- Header -->
        <div class="flex items-center justify-between px-4 py-3 border-b">
          <div class="flex items-center gap-2 min-w-0">
            <Layers class="size-4 text-muted-foreground shrink-0" />
            <span class="text-sm font-medium truncate">{{ productGroup.name }}</span>
          </div>
          <div class="flex items-center gap-1 shrink-0">
            <Button
              variant="ghost"
              size="icon"
              class="size-7"
              title="Сменить группу"
              type="button"
              @click="openGroupDialog"
            >
              <ArrowRightLeft class="size-3.5 text-muted-foreground" />
            </Button>
            <router-link
              :to="`/product-groups/${productGroup.id}/edit`"
              class="inline-flex items-center justify-center size-7 rounded-md hover:bg-accent transition-colors"
              title="Редактировать группу"
            >
              <ExternalLink class="size-3.5 text-muted-foreground" />
            </router-link>
          </div>
        </div>
        <!-- Scrollable product list -->
        <div ref="sidebarScrollRef" class="flex-1 min-h-0 overflow-y-auto thin-scrollbar p-3 space-y-1.5" :class="{ 'pointer-events-none opacity-50': isProductLoading }">
          <!-- Add product button -->
          <router-link
            :to="duplicateProductUrl"
            class="flex items-center justify-center p-2 h-16 rounded-lg border border-dashed hover:bg-accent/50 transition-colors"
          >
            <Plus class="size-4 text-muted-foreground" />
          </router-link>
          <ContextMenu v-for="member in productGroup.members" :key="member.productId">
            <ContextMenuTrigger as-child>
              <router-link
                :to="{ path: `/products/${member.productId}/edit`, query: { tab } }"
                class="flex items-center gap-3 p-2 rounded-lg transition-colors"
                :class="member.isCurrent ? 'bg-accent' : 'hover:bg-accent/50'"
              >
                <img
                  v-if="member.imageThumbnailUrl || member.imageUrl"
                  :src="member.imageThumbnailUrl ?? member.imageUrl"
                  class="size-12 rounded-md object-cover shrink-0"
                  :alt="member.productName"
                />
                <div v-else class="size-12 rounded-md bg-muted flex items-center justify-center shrink-0">
                  <Layers class="size-5 text-muted-foreground" />
                </div>
                <div class="min-w-0">
                  <p class="text-sm truncate" :class="member.isCurrent ? 'font-medium' : ''">{{ member.productName || member.productSku }}</p>
                  <p class="text-xs text-muted-foreground">{{ member.productSku }}</p>
                </div>
              </router-link>
            </ContextMenuTrigger>
            <ContextMenuContent>
              <ContextMenuItem @click="$router.push(getDuplicateUrl(member.productId))">
                <Copy class="size-4 mr-2" />
                Дублировать
              </ContextMenuItem>
            </ContextMenuContent>
          </ContextMenu>
        </div>
      </div>
      <!-- Main content -->
      <div ref="mainScrollRef" class="grow flex flex-col min-w-0">
        <div v-show="!isProductLoading" class="grow flex flex-col">
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
                  <button
                    class="absolute top-1 right-1 size-5 rounded-full bg-black/60 hover:bg-destructive flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity"
                    @click.stop="removeGalleryImage(index)"
                  >
                    <X class="size-3 text-white" />
                  </button>
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
            <AlertDialog :open="showGroupDialog" @update:open="showGroupDialog = $event">
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Сменить группу товаров</AlertDialogTitle>
                  <AlertDialogDescription>
                    Товар будет перемещён в выбранную группу.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <div class="py-2">
                  <SearchableSelect
                    :model-value="selectedGroupId"
                    @update:model-value="selectedGroupId = $event"
                    :options="groupOptions"
                    placeholder="Выберите группу..."
                  />
                </div>
                <AlertDialogFooter>
                  <AlertDialogCancel :disabled="isChangingGroup">Отмена</AlertDialogCancel>
                  <Button
                    @click="submitGroupChange"
                    :disabled="isChangingGroup"
                    type="button"
                  >
                    <Loader2 v-if="isChangingGroup" class="size-4 mr-2 animate-spin" />
                    Применить
                  </Button>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>

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

          <!-- Actions row: Delete + Duplicate on left, Save on right -->
          <div class="flex items-center justify-between sticky bottom-0 bg-background py-3 -mx-px border-t mt-2">
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
                  <AlertDialogAction @click="handleDeleteProduct">
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
        <div v-if="category?.hasSizes" v-show="tab == 'sizes'" class="flex flex-col border rounded-xl" style="min-height: calc(100vh - 8.5rem)">
          <ProductVariantsEditor :productId="productId" />
        </div>

        <!-- Marketplaces tab -->
        <div v-show="tab == 'marketplaces'" class="flex flex-col border rounded-xl" style="min-height: calc(100vh - 8.5rem)">
          <ProductMarketplaceIntegrations :productId="productId" @product-updated="fetchProduct" />
        </div>

        <!-- Notes tab -->
        <div v-show="tab == 'notes'" class="flex flex-col border rounded-xl" style="min-height: calc(100vh - 8.5rem)">
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
          <div class="mt-auto p-4">
            <Button variant="outline" @click="addNote">
              <NotebookPen class="size-4 mr-2" />
              Добавить заметку
            </Button>
          </div>
        </div>
      </div>

        <!-- Loading state -->
        <div
          v-show="isProductLoading"
          class="grow flex items-center justify-center"
        >
          <Loader2 class="text-primary size-8 animate-spin" />
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
        <button class="absolute top-4 right-4 text-white/70 hover:text-white" aria-label="Закрыть" @click="closeLightbox">
          <X class="size-8" />
        </button>
        <!-- Prev -->
        <button v-if="lightboxIndex > 0" class="absolute left-4 text-white/70 hover:text-white" aria-label="Предыдущее фото" @click="prevImage">
          <ChevronLeft class="size-10" />
        </button>
        <!-- Image -->
        <img
          :src="`/api/files/${lightboxImages[lightboxIndex]}/download`"
          alt="Фото товара"
          class="max-h-[90vh] max-w-[90vw] object-contain"
        />
        <!-- Next -->
        <button v-if="lightboxIndex < lightboxImages.length - 1" class="absolute right-4 text-white/70 hover:text-white" aria-label="Следующее фото" @click="nextImage">
          <ChevronRight class="size-10" />
        </button>
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
