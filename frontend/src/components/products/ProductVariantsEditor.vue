<script>
import { Spinner } from "@/components/ui/spinner";
import Input from "@/components/ui/input/Input.vue";
import BarcodeInput from "@/components/ui/barcode/BarcodeInput.vue";
import {
  NumberField,
  NumberFieldInput,
} from "@/components/ui/number-field";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { GripVertical, Plus, Trash2 } from "lucide-vue-next";
import { useDebounceFn } from "@vueuse/core";
import draggable from "vuedraggable";
import { confirm } from "@/composables/useConfirm";
import { t } from "@/i18n";
import { Button } from "@/components/ui/button";
import { getCachedMe } from "@/lib/meCache";
import { canAct, PERMS } from "@/lib/permissions";

export default {
  components: {
    Spinner,
    Input,
    BarcodeInput,
    NumberField,
    NumberFieldInput,
    InsetGroup,
    InsetGroupItem,
    GripVertical,
    Plus,
    Trash2,
    Button,
    draggable,
  },
  props: {
    productId: {
      type: Number,
      required: true,
    },
  },
  data() {
    return {
      variants: [],
      isLoading: false,
      isAdding: false,
      deletingId: null,
      pendingSaves: {},
    };
  },
  computed: {
    isSaving() {
      return Object.keys(this.pendingSaves).length > 0;
    },
    canEdit() {
      return canAct(getCachedMe(), PERMS.CatalogManage);
    },
  },
  methods: {
    ...mapActions([
      "getProductVariants",
      "createProductVariant",
      "updateProductVariant",
      "deleteProductVariant",
    ]),

    async fetchVariants() {
      this.isLoading = true;
      try {
        const data = await this.getProductVariants({ productId: this.productId });
        this.variants = data || [];
      } catch (error) {
        displaySonnerError(error);
      }
      this.isLoading = false;
    },

    async handleAdd() {
      this.isAdding = true;
      try {
        const newVariant = await this.createProductVariant({
          productId: this.productId,
          data: {
            size: "",
            sku: "",
            barcode: "",
            price: null,
            quantity: null,
          },
        });
        this.variants.push(newVariant);
        await this.$nextTick();
        document.getElementById(`product-variant-size-${newVariant.id}`)?.focus();
      } catch (error) {
        displaySonnerError(error);
      }
      this.isAdding = false;
    },

    async handleDelete(variant) {
      const name =
        variant.size || t("products.productVariantsEditor.unnamedFallback");
      const ok = await confirm({
        title: t("products.productVariantsEditor.deleteConfirmTitle"),
        description: t("products.productVariantsEditor.deleteConfirmDescription", {
          name,
        }),
        confirmLabel: t("common.delete"),
      });
      if (ok !== true) return;

      this.deletingId = variant.id;
      try {
        await this.deleteProductVariant({
          productId: this.productId,
          variantId: variant.id,
        });
        this.variants = this.variants.filter((v) => v.id !== variant.id);
      } catch (error) {
        displaySonnerError(error);
      }
      this.deletingId = null;
    },

    onFieldChange(variant) {
      if (!this._debouncedSaves) {
        this._debouncedSaves = {};
      }
      if (!this._debouncedSaves[variant.id]) {
        this._debouncedSaves[variant.id] = useDebounceFn(async (v) => {
          this.pendingSaves = { ...this.pendingSaves, [v.id]: true };
          try {
            await this.updateProductVariant({
              productId: this.productId,
              variantId: v.id,
              data: {
                size: v.size,
                sku: v.sku,
                barcode: v.barcode,
                price: v.price,
                quantity: v.quantity,
              },
            });
          } catch (error) {
            displaySonnerError(error);
          }
          const next = { ...this.pendingSaves };
          delete next[v.id];
          this.pendingSaves = next;
        }, 1000);
      }
      this._debouncedSaves[variant.id](variant);
    },

    setPrice(variant, value) {
      variant.price = value ?? null;
      this.onFieldChange(variant);
    },

    setQuantity(variant, value) {
      variant.quantity = value ?? null;
      this.onFieldChange(variant);
    },

    async handleReorder() {
      const updates = this.variants.map((v, index) => ({
        ...v,
        sortOrder: index,
      }));
      for (const v of updates) {
        try {
          await this.updateProductVariant({
            productId: this.productId,
            variantId: v.id,
            data: {
              size: v.size,
              sku: v.sku,
              barcode: v.barcode,
              price: v.price,
              quantity: v.quantity,
              sortOrder: v.sortOrder,
            },
          });
        } catch {
          // keep remaining rows
        }
      }
    },
  },
  mounted() {
    this.fetchVariants();
  },
};
</script>

<template>
  <div class="space-y-6">
    <div v-if="isLoading" class="flex justify-center py-16">
      <Spinner size="2xl" class="text-muted-foreground" />
    </div>

    <template v-else>
      <p
        v-if="variants.length === 0"
        class="px-1 text-xs text-muted-foreground"
      >
        {{ $t("products.productVariantsEditor.emptyDescription") }}
      </p>

      <draggable
        v-if="variants.length > 0"
        v-model="variants"
        item-key="id"
        handle=".drag-handle"
        ghost-class="opacity-50"
        class="space-y-3"
        @end="handleReorder"
      >
        <template #item="{ element: variant }">
          <InsetGroup>
            <InsetGroupItem
              as="div"
              class="items-start gap-3 px-3.5 py-3 hover:bg-transparent"
            >
              <template #leading>
                <button
                  type="button"
                  class="drag-handle text-muted-foreground mt-2 cursor-grab active:cursor-grabbing"
                  :aria-label="
                    $t('products.productVariantsEditor.reorderAria')
                  "
                >
                  <GripVertical class="size-4" />
                </button>
              </template>
              <div class="space-y-3">
                <label class="grid gap-1">
                  <span class="text-xs text-muted-foreground">{{
                    $t("products.productVariantsEditor.sizeHeader")
                  }}</span>
                  <Input
                    :id="`product-variant-size-${variant.id}`"
                    v-model="variant.size"
                    autocomplete="off"
                    :placeholder="
                      $t('products.productVariantsEditor.sizePlaceholder')
                    "
                    :readonly="!canEdit"
                    @input="onFieldChange(variant)"
                  />
                </label>
                <div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <label class="grid gap-1">
                    <span class="text-xs text-muted-foreground">{{
                      $t("products.productVariantsEditor.skuHeader")
                    }}</span>
                    <Input
                      v-model="variant.sku"
                      autocomplete="off"
                      :placeholder="
                        $t('products.productVariantsEditor.skuPlaceholder')
                      "
                      :readonly="!canEdit"
                      @input="onFieldChange(variant)"
                    />
                  </label>
                  <label class="grid gap-1">
                    <span class="text-xs text-muted-foreground">{{
                      $t("products.productVariantsEditor.priceHeader")
                    }}</span>
                    <NumberField
                      class="w-full"
                      :disabled="!canEdit"
                      :min="0"
                      :step="0.01"
                      :format-options="{
                        minimumFractionDigits: 0,
                        maximumFractionDigits: 2,
                      }"
                      :model-value="variant.price ?? undefined"
                      @update:model-value="setPrice(variant, $event)"
                    >
                      <NumberFieldInput class="h-9 text-right" />
                    </NumberField>
                  </label>
                  <label class="grid gap-1">
                    <span class="text-xs text-muted-foreground">{{
                      $t("products.productVariantsEditor.quantityHeader")
                    }}</span>
                    <NumberField
                      class="w-full"
                      :disabled="!canEdit"
                      :min="0"
                      :step="1"
                      :model-value="variant.quantity ?? undefined"
                      @update:model-value="setQuantity(variant, $event)"
                    >
                      <NumberFieldInput class="h-9 text-right" />
                    </NumberField>
                  </label>
                </div>
                <label class="grid gap-1">
                  <span class="text-xs text-muted-foreground">{{
                    $t("products.productVariantsEditor.barcodeHeader")
                  }}</span>
                  <BarcodeInput
                    :model-value="variant.barcode || ''"
                    @update:model-value="
                      variant.barcode = $event;
                      onFieldChange(variant);
                    "
                  />
                </label>
              </div>
              <template #trailing>
                <Button
                  v-if="canEdit"
                  type="button"
                  variant="ghost"
                  size="icon"
                  class="size-8 shrink-0 text-muted-foreground hover:text-destructive"
                  :disabled="deletingId === variant.id"
                  :aria-label="
                    $t('products.productVariantsEditor.deleteAction')
                  "
                  @click="handleDelete(variant)"
                >
                  <Spinner v-if="deletingId === variant.id" />
                  <Trash2 v-else class="size-4" />
                </Button>
              </template>
            </InsetGroupItem>
          </InsetGroup>
        </template>
      </draggable>

      <InsetGroup v-if="canEdit">
        <InsetGroupItem
          :disabled="isAdding"
          :aria-label="$t('products.productVariantsEditor.addLink')"
          @click="handleAdd"
        >
          <template #leading>
            <Spinner v-if="isAdding" />
            <Plus v-else class="size-4" />
          </template>
          {{ $t("products.productVariantsEditor.addLink") }}
        </InsetGroupItem>
      </InsetGroup>

      <p
        v-if="isSaving"
        class="flex items-center gap-2 px-1 text-xs text-muted-foreground"
      >
        <Spinner size="xs" />
        {{ $t("products.productVariantsEditor.saving") }}
      </p>
    </template>
  </div>
</template>
