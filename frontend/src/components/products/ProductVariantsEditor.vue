<script>
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import BarcodeInput from "@/components/ui/barcode/BarcodeInput.vue";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Plus,
  Trash2,
  GripVertical,
  Layers,
} from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import { useDebounceFn } from "@vueuse/core";
import draggable from "vuedraggable";
import { t } from "@/i18n";

export default {
  components: {
    Spinner,
    Button,
    Input,
    EmptyState,
    BarcodeInput,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogTrigger,
    Plus,
    Trash2,
    GripVertical,
    Layers,
    draggable
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
      // Track pending saves per variant id
      pendingSaves: {},
    };
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
        displaySonnerSuccess(t("products.productVariantsEditor.variantAdded"));
      } catch (error) {
        displaySonnerError(error);
      }
      this.isAdding = false;
    },

    async handleDelete(variant) {
      this.deletingId = variant.id;
      try {
        await this.deleteProductVariant({
          productId: this.productId,
          variantId: variant.id,
        });
        this.variants = this.variants.filter((v) => v.id !== variant.id);
        displaySonnerSuccess(t("products.productVariantsEditor.variantDeleted"));
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

    async handleReorder() {
      // Save new sort order by updating each variant's sortOrder
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
          // silently continue
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
  <div class="flex min-h-0 flex-1 flex-col">
    <div class="flex h-11 shrink-0 items-center justify-between border-b px-4">
      <p class="flex items-center gap-2 text-sm font-medium">
        <Layers class="size-4" />
        {{ $t("products.productVariantsEditor.title") }}
      </p>
      <Button variant="ghost" size="sm" @click="handleAdd" :disabled="isAdding" class="h-7">
        <Spinner v-if="isAdding" class="mr-1" />
        <Plus v-else class="size-4 mr-1" />
        {{ $t("common.add") }}
      </Button>
    </div>
    <div class="flex min-h-0 flex-1 flex-col overflow-y-auto">
      <div v-if="isLoading" class="flex flex-1 items-center justify-center py-12">
        <Spinner size="2xl" class="text-muted-foreground" />
      </div>

      <EmptyState
        v-else-if="variants.length === 0"
        :framed="false"
        :title="$t('products.productVariantsEditor.emptyTitle')"
        :description="$t('products.productVariantsEditor.emptyDescription')"
      >
        <template #icon>
          <Layers />
        </template>
      </EmptyState>

      <Table v-else>
        <TableHeader class="sticky top-0 z-10 bg-card [&_tr]:border-b">
          <TableRow class="hover:bg-transparent">
            <TableHead class="w-10 pl-4 pr-2" />
            <TableHead class="px-3">{{ $t("products.productVariantsEditor.sizeHeader") }}</TableHead>
            <TableHead class="px-3">{{ $t("products.productVariantsEditor.skuHeader") }}</TableHead>
            <TableHead class="px-3">{{ $t("products.productVariantsEditor.barcodeHeader") }}</TableHead>
            <TableHead class="w-28 px-3">{{ $t("products.productVariantsEditor.priceHeader") }}</TableHead>
            <TableHead class="w-24 px-3">{{ $t("products.productVariantsEditor.quantityHeader") }}</TableHead>
            <TableHead class="w-12 pl-2 pr-4" />
          </TableRow>
        </TableHeader>
      <draggable
        v-model="variants"
        tag="tbody"
        item-key="id"
        handle=".drag-handle"
        ghost-class="opacity-50"
        @end="handleReorder"
      >
        <template #item="{ element: variant }">
          <TableRow>
            <TableCell class="pl-4 pr-2 py-1.5">
              <GripVertical class="drag-handle size-4 text-muted-foreground cursor-grab active:cursor-grabbing" />
            </TableCell>
            <TableCell class="px-3 py-1.5">
              <Input
                v-model="variant.size"
                placeholder="XS, S, M..."
                class="h-8"
                @input="onFieldChange(variant)"
              />
            </TableCell>
            <TableCell class="px-3 py-1.5">
              <Input
                v-model="variant.sku"
                placeholder="SKU"
                class="h-8"
                @input="onFieldChange(variant)"
              />
            </TableCell>
            <TableCell class="px-3 py-1.5">
              <BarcodeInput
                :model-value="variant.barcode || ''"
                @update:model-value="variant.barcode = $event; onFieldChange(variant)"
              />
            </TableCell>
            <TableCell class="px-3 py-1.5">
              <Input
                type="number"
                step="0.01"
                min="0"
                :model-value="variant.price"
                @update:model-value="variant.price = $event !== '' && $event != null ? Number($event) : null; onFieldChange(variant)"
                placeholder="0.00"
                class="h-8"
              />
            </TableCell>
            <TableCell class="px-3 py-1.5">
              <Input
                type="number"
                min="0"
                :model-value="variant.quantity"
                @update:model-value="variant.quantity = $event !== '' && $event != null ? Number($event) : null; onFieldChange(variant)"
                placeholder="0"
                class="h-8"
              />
            </TableCell>
            <TableCell class="pl-2 pr-4 py-1.5">
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                    :disabled="deletingId === variant.id"
                  >
                    <Spinner v-if="deletingId === variant.id" />
                    <Trash2 v-else class="size-4" />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{{ $t("products.productVariantsEditor.deleteConfirmTitle") }}</AlertDialogTitle>
                    <AlertDialogDescription>
                      {{ $t("products.productVariantsEditor.deleteConfirmDescription", { name: variant.size || $t("products.productVariantsEditor.unnamedFallback") }) }}
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
                    <AlertDialogAction variant="destructive" @click="handleDelete(variant)">
                      {{ $t("common.delete") }}
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </TableCell>
          </TableRow>
        </template>
      </draggable>
      </Table>

      <div
        v-if="Object.keys(pendingSaves).length > 0"
        class="flex items-center gap-2 border-t px-4 py-2 text-sm text-muted-foreground"
      >
        <Spinner size="xs" />
        <span>{{ $t("products.productVariantsEditor.saving") }}</span>
      </div>
    </div>
  </div>
</template>
