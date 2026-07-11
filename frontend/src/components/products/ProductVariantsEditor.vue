<script>
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
import { Loader2, Plus, Trash2, GripVertical, Layers } from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import { useDebounceFn } from "@vueuse/core";
import draggable from "vuedraggable";

export default {
  components: {
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
    Loader2,
    Plus,
    Trash2,
    GripVertical,
    Layers,
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
        displaySonnerSuccess("Размер добавлен");
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
        displaySonnerSuccess("Размер удалён");
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
  <div class="flex flex-col min-h-0">
    <div class="flex items-center justify-between px-4 h-11 border-b shrink-0">
      <p class="text-sm font-medium flex items-center gap-2">
        <Layers class="size-4" />
        Размеры
      </p>
      <Button variant="ghost" size="sm" @click="handleAdd" :disabled="isAdding" class="h-7">
        <Loader2 v-if="isAdding" class="size-4 mr-1 animate-spin" />
        <Plus v-else class="size-4 mr-1" />
        Добавить
      </Button>
    </div>
    <div class="flex-1 overflow-y-auto p-4 space-y-4">

    <div v-if="isLoading" class="flex justify-center py-12">
      <Loader2 class="size-8 animate-spin text-muted-foreground" />
    </div>

    <EmptyState
      v-else-if="variants.length === 0"
      title="Нет размеров"
      description="Добавьте размеры, чтобы управлять ценой, штрихкодом и остатками по каждому из них."
    >
      <template #icon>
        <Layers />
      </template>
    </EmptyState>

    <Table v-else>
      <TableHeader>
        <TableRow>
          <TableHead class="w-[40px]"></TableHead>
          <TableHead>Размер</TableHead>
          <TableHead>Артикул (SKU)</TableHead>
          <TableHead>Штрихкод</TableHead>
          <TableHead class="w-[120px]">Цена</TableHead>
          <TableHead class="w-[100px]">Остаток</TableHead>
          <TableHead class="w-[60px]"></TableHead>
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
            <TableCell class="py-1">
              <GripVertical class="drag-handle size-4 text-muted-foreground cursor-grab active:cursor-grabbing" />
            </TableCell>
            <TableCell class="py-1">
              <Input
                v-model="variant.size"
                placeholder="XS, S, M..."
                class="h-8"
                @input="onFieldChange(variant)"
              />
            </TableCell>
            <TableCell class="py-1">
              <Input
                v-model="variant.sku"
                placeholder="SKU"
                class="h-8"
                @input="onFieldChange(variant)"
              />
            </TableCell>
            <TableCell class="py-1">
              <BarcodeInput
                :model-value="variant.barcode || ''"
                @update:model-value="variant.barcode = $event; onFieldChange(variant)"
              />
            </TableCell>
            <TableCell class="py-1">
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
            <TableCell class="py-1">
              <Input
                type="number"
                min="0"
                :model-value="variant.quantity"
                @update:model-value="variant.quantity = $event !== '' && $event != null ? Number($event) : null; onFieldChange(variant)"
                placeholder="0"
                class="h-8"
              />
            </TableCell>
            <TableCell class="py-1">
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8"
                    :disabled="deletingId === variant.id"
                  >
                    <Loader2 v-if="deletingId === variant.id" class="size-4 animate-spin" />
                    <Trash2 v-else class="size-4 text-destructive" />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Удалить размер?</AlertDialogTitle>
                    <AlertDialogDescription>
                      Размер "{{ variant.size || '(без названия)' }}" будет удалён. Это действие нельзя отменить.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Отмена</AlertDialogCancel>
                    <AlertDialogAction @click="handleDelete(variant)">
                      Удалить
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </TableCell>
          </TableRow>
        </template>
      </draggable>
    </Table>

    <!-- Saving indicator -->
    <div v-if="Object.keys(pendingSaves).length > 0" class="flex items-center gap-2 text-sm text-muted-foreground">
      <Loader2 class="size-3 animate-spin" />
      <span>Сохранение...</span>
    </div>
    </div>
  </div>
</template>
