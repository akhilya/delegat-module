<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Label from "@/components/ui/label/Label.vue";
import ProductAttributeForm from "@/components/products/productAttributeForm.vue";
import StoreAttributeForm from "@/components/products/storeAttributeForm.vue";
import { BarcodeInput } from "@/components/ui/barcode";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import Separator from "@/components/ui/separator/Separator.vue";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuTrigger,
} from "@/components/ui/context-menu";
import { Loader2, FolderOpen, Globe, Layers, ExternalLink, Plus, Copy } from "lucide-vue-next";
import { PAGE_PRODUCTS } from "@/router";
import { useStore } from "vuex";
import { useRouter, useRoute } from "vue-router";
import { ref, watch, computed, nextTick } from "vue";
import { toTypedSchema } from "@vee-validate/zod";
import { useForm } from "vee-validate";
import { z } from "zod";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";

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
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Input,
    Textarea,
    Label,
    ProductAttributeForm,
    StoreAttributeForm,
    BarcodeInput,
    Loader2,
    FolderOpen,
    Globe,
    Layers,
    ExternalLink,
    Plus,
    Copy,
    Separator,
    Card,
    CardContent,
    CardHeader,
    CardTitle,
    ContextMenu,
    ContextMenuContent,
    ContextMenuItem,
    ContextMenuTrigger,
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
    const attributeValues = ref([]);
    const storeAttributes = ref([]);
    const storeAttributeValues = ref([]);
    const isLoadingStoreAttributes = ref(false);
    const productGroups = ref([]);
    const selectedGroupId = ref(null);
    const isLoadingGroups = ref(false);
    const descriptionRef = ref(null);

    function autoResizeDescription() {
      const ref = descriptionRef.value;
      const el = ref?.$el ?? ref;
      if (!el || el.tagName !== 'TEXTAREA') return;
      el.style.height = 'auto';
      el.style.height = el.scrollHeight + 'px';
    }

    // Fetch store attributes on mount
    async function fetchStoreAttributes() {
      isLoadingStoreAttributes.value = true;
      await store.dispatch("getAllStoreAttributes", {
        onSuccess: (response) => {
          storeAttributes.value = response.data.value || [];
          // Initialize default values for store attributes
          storeAttributeValues.value = [];
          storeAttributes.value.forEach((attr) => {
            if (attr.defaultValue) {
              storeAttributeValues.value.push({
                storeAttributeId: attr.id,
                value: attr.defaultValue,
              });
            }
          });
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isLoadingStoreAttributes.value = false;
    }

    fetchStoreAttributes();

    // Fetch product groups on mount
    async function fetchProductGroups() {
      isLoadingGroups.value = true;
      await store.dispatch("getAllGroups", {
        onSuccess: (response) => {
          productGroups.value = response.data.value || [];
        },
        onError: (error) => {
          console.error("Failed to load product groups:", error);
        },
      });
      isLoadingGroups.value = false;
    }

    fetchProductGroups();

    // Pre-fill from existing product (via query params)
    const fromProductId = route.query.fromProduct;
    const fromGroupId = route.query.groupId;
    const isPreFilling = ref(false);
    const groupDetail = ref(null);

    // Fetch group detail for sidebar
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
            members: group.members?.map(m => ({
              productId: m.product.id,
              productName: m.product.name || "",
              productSku: m.product.sku || "",
              imageUrl: m.product.images?.[0]?.url,
              imageThumbnailUrl: m.product.images?.[0]?.thumbnailUrl,
            })) || [],
          };
        },
        onError: () => {},
      });
    }
    if (fromGroupId) fetchGroupDetail(parseInt(fromGroupId));

    // Update sidebar when selected group changes
    watch(selectedGroupId, (newId) => {
      fetchGroupDetail(newId);
    });

    function getDuplicateUrl(sourceProductId) {
      const params = new URLSearchParams();
      params.set('fromProduct', String(sourceProductId));
      if (fromGroupId) params.set('groupId', String(fromGroupId));
      return `/products/create?${params.toString()}`;
    }

    async function prefillFromProduct() {
      if (!fromProductId) return;
      isPreFilling.value = true;
      await store.dispatch("getProduct", {
        productId: parseInt(fromProductId),
        onSuccess: async (response) => {
          const product = response.data.value;

          // Set form values (skip SKU and barcode)
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

          // Set category — the watcher will load category details
          const catId = product.category?.id || product.categoryId;
          if (catId) {
            selectedCategoryId.value = catId;

            // Wait for category watcher to finish loading, then overwrite attribute values
            const unwatchCategory = watch(selectedCategory, (cat) => {
              if (cat) {
                nextTick(() => {
                  if (product.attributeValues?.length) {
                    attributeValues.value = product.attributeValues.map((av) => ({ ...av }));
                  }
                  unwatchCategory();
                });
              }
            });
          }

          // Set store attribute values
          if (product.storeAttributeValues?.length) {
            storeAttributeValues.value = product.storeAttributeValues.map((av) => ({ ...av }));
          }

          // Set group
          if (fromGroupId) {
            selectedGroupId.value = parseInt(fromGroupId);
          }

          nextTick(() => autoResizeDescription());
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      isPreFilling.value = false;
    }

    // Fetch categories on mount
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

    fetchCategories().then(() => prefillFromProduct());

    // Watch for category selection change
    watch(selectedCategoryId, async (newVal) => {
      if (newVal) {
        await store.dispatch("getCategory", {
          id: newVal,
          onSuccess: (response) => {
            selectedCategory.value = response.data.value;
            // Initialize default values for attributes
            attributeValues.value = [];
            if (selectedCategory.value.attributes) {
              selectedCategory.value.attributes.forEach((attr) => {
                if (attr.defaultValue) {
                  attributeValues.value.push({
                    attributeId: attr.id,
                    value: attr.defaultValue,
                  });
                }
              });
            }
          },
          onError: (error) => {
            displaySonnerError(error);
            selectedCategory.value = null;
          },
        });
      } else {
        selectedCategory.value = null;
        attributeValues.value = [];
      }
    });

    const formSchema = toTypedSchema(
      z.object({
        product: z.object({
          sku: z.string().min(1, "Артикул обязателен").max(20),
          barcode: z.string().max(13).optional().nullable(),
          name: z.string().min(1, "Название обязательно").max(100),
          description: z.string().max(5000).default(""),
          storageQuantity: z.number().int().min(0, "Количество не может быть отрицательным").optional().nullable(),
          price: z.number().min(0, "Цена не может быть отрицательной").optional().nullable(),
        }),
      }),
    );

    const form = useForm({
      validationSchema: formSchema,
      initialValues: {
        product: {
          sku: "",
          barcode: "",
          name: "",
          description: "",
        },
      },
    });

    const onSubmit = form.handleSubmit(async (values) => {
      if (!selectedCategoryId.value) {
        displaySonnerError("Выберите категорию товара");
        return;
      }

      // Validate required category attributes
      if (selectedCategory.value?.attributes) {
        const requiredAttrs = selectedCategory.value.attributes.filter((a) => a.isRequired);
        for (const attr of requiredAttrs) {
          const attrValue = attributeValues.value.find((av) => av.attributeId === attr.id);
          if (!attrValue || !attrValue.value) {
            displaySonnerError(`Заполните обязательное поле категории: ${attr.name}`);
            return;
          }
        }
      }

      // Validate required store attributes
      const requiredStoreAttrs = storeAttributes.value.filter((a) => a.isRequired);
      for (const attr of requiredStoreAttrs) {
        const attrValue = storeAttributeValues.value.find((av) => av.storeAttributeId === attr.id);
        if (!attrValue || !attrValue.value) {
          displaySonnerError(`Заполните обязательное глобальное поле: ${attr.name}`);
          return;
        }
      }

      isSubmitting.value = true;

      const input = {
        ...values.product,
        categoryId: selectedCategoryId.value,
        attributeValues: attributeValues.value,
        storeAttributeValues: storeAttributeValues.value,
      };

      try {
        await store.dispatch("createProduct", {
          input,
          onSuccess: async (response) => {
            const createdProduct = response.data.value;

            // Add to group if selected
            if (selectedGroupId.value && createdProduct.id) {
              await store.dispatch("addProductsToGroup", {
                groupId: selectedGroupId.value,
                productIds: [createdProduct.id],
                onSuccess: () => {
                  displaySonnerSuccess("Товар создан и добавлен в группу.");
                },
                onError: () => {
                  displaySonnerSuccess("Товар создан, но не удалось добавить в группу.");
                },
              });
            } else {
              displaySonnerSuccess("Товар успешно создан.");
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
      }
    });

    function updateAttributeValues(newValues) {
      attributeValues.value = newValues;
    }

    function updateStoreAttributeValues(newValues) {
      storeAttributeValues.value = newValues;
    }

    return {
      form,
      onSubmit,
      isSubmitting,
      isLoadingCategories,
      categories,
      selectedCategoryId,
      selectedCategory,
      attributeValues,
      storeAttributes,
      storeAttributeValues,
      isLoadingStoreAttributes,
      updateAttributeValues,
      updateStoreAttributeValues,
      productGroups,
      selectedGroupId,
      isLoadingGroups,
      descriptionRef,
      autoResizeDescription,
      groupDetail,
      fromProductId,
      getDuplicateUrl,
    };
  },
};
</script>

<template>
  <Page>
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />
    <div class="w-full flex gap-6 mb-4" style="min-height: calc(100vh - 8.5rem)">
      <!-- Left: Product Group sidebar -->
      <div v-if="groupDetail" class="w-80 shrink-0 flex flex-col border rounded-xl overflow-hidden sticky top-[5.5rem] self-start" style="height: calc(100vh - 8.5rem)">
        <!-- Header -->
        <div class="flex items-center justify-between px-4 py-3 border-b">
          <div class="flex items-center gap-2 min-w-0">
            <Layers class="size-4 text-muted-foreground shrink-0" />
            <span class="text-sm font-medium truncate">{{ groupDetail.name }}</span>
          </div>
          <div class="flex items-center gap-1 shrink-0">
            <router-link
              :to="`/product-groups/${groupDetail.id}/edit`"
              class="inline-flex items-center justify-center size-7 rounded-md hover:bg-accent transition-colors"
              title="Редактировать группу"
            >
              <ExternalLink class="size-3.5 text-muted-foreground" />
            </router-link>
          </div>
        </div>
        <!-- Scrollable product list -->
        <div class="flex-1 min-h-0 overflow-y-auto thin-scrollbar p-3 space-y-1.5" :class="{ 'pointer-events-none opacity-50': isSubmitting }">
          <!-- Add product button (current — highlighted) -->
          <div class="flex items-center justify-center p-2 h-16 rounded-lg bg-accent">
            <Plus class="size-4 text-muted-foreground" />
          </div>
          <ContextMenu v-for="member in groupDetail.members" :key="member.productId">
            <ContextMenuTrigger as-child>
              <router-link
                :to="`/products/${member.productId}/edit`"
                class="flex items-center gap-3 p-2 rounded-lg hover:bg-accent/50 transition-colors"
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
                  <p class="text-sm truncate">{{ member.productName || member.productSku }}</p>
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
      <div class="grow flex flex-col min-w-0">
      <form @submit.prevent="onSubmit" class="space-y-4 flex flex-col">
        <!-- Card 1: Main fields -->
        <Card>
          <CardHeader>
            <CardTitle class="text-base">Основные поля</CardTitle>
          </CardHeader>
          <CardContent class="space-y-4">
            <!-- Category Selection -->
            <div class="space-y-2">
              <Label>Категория <span class="text-destructive">*</span></Label>
              <Select v-model="selectedCategoryId" :disabled="isLoadingCategories">
                <SelectTrigger class="w-full">
                  <SelectValue placeholder="Выберите категорию товара..." />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="category in categories"
                    :key="category.id"
                    :value="category.id"
                  >
                    <span class="flex items-center gap-2">
                      <FolderOpen class="size-4" />
                      {{ category.name }}
                    </span>
                  </SelectItem>
                </SelectContent>
              </Select>
              <p v-if="categories.length === 0 && !isLoadingCategories" class="text-sm text-muted-foreground">
                Сначала создайте категорию товаров
              </p>
            </div>

            <FormField v-slot="{ componentField }" name="product.sku">
              <FormItem>
                <FormLabel>Артикул <span class="text-destructive">*</span></FormLabel>
                <FormControl>
                  <Input type="text" v-bind="componentField" />
                </FormControl>
                <FormMessage />
              </FormItem>
            </FormField>

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

            <!-- Quantity + Price -->
            <div class="grid grid-cols-2 gap-4">
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
                  <FormLabel>Цена</FormLabel>
                  <FormControl>
                    <Input
                      type="number"
                      step="0.01"
                      :model-value="value"
                      @update:model-value="setValue($event !== '' && $event != null ? Number($event) : null)"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              </FormField>
            </div>

            <!-- Product Group Selection -->
            <div v-if="productGroups.length > 0" class="space-y-2">
              <Label>Группа товара</Label>
              <Select v-model="selectedGroupId" :disabled="isLoadingGroups">
                <SelectTrigger class="w-full">
                  <SelectValue placeholder="Не выбрана" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem :value="null">
                    <span class="text-muted-foreground">Без группы</span>
                  </SelectItem>
                  <SelectItem
                    v-for="group in productGroups"
                    :key="group.id"
                    :value="group.id"
                  >
                    <span class="flex items-center gap-2">
                      <Layers class="size-4" />
                      {{ group.name }}
                      <span v-if="group.memberCount" class="text-muted-foreground text-xs">
                        ({{ group.memberCount }})
                      </span>
                    </span>
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
          </CardContent>
        </Card>

        <!-- Card 2: Category Attributes -->
        <Card v-if="selectedCategory">
          <CardHeader>
            <CardTitle class="text-base flex items-center gap-2">
              <Layers class="size-4" />
              Атрибуты категории
              <span v-if="selectedCategory.attributes?.length" class="text-muted-foreground text-sm font-normal">
                ({{ selectedCategory.attributes.length }})
              </span>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <p v-if="!selectedCategory.attributes || selectedCategory.attributes.length === 0" class="text-sm text-muted-foreground py-2">
              У этой категории нет атрибутов
            </p>
            <ProductAttributeForm
              v-else
              :attributes="selectedCategory.attributes"
              :model-value="attributeValues"
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
              @update:model-value="updateStoreAttributeValues"
            />
          </CardContent>
        </Card>

        <div class="flex justify-end">
          <Button
            type="submit"
            size="lg"
            :disabled="isSubmitting || !selectedCategoryId"
          >
            <Loader2 v-if="isSubmitting" class="animate-spin mr-2" />
            Создать товар
          </Button>
        </div>
      </form>
      </div>
    </div>
  </Page>
</template>
