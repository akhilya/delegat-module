<script setup>
import { computed } from "vue";
import { ArrowRightLeft, Unlink } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import { mappingDisplayName } from "@/lib/mappingDisplayName";
import { t as $t } from "@/i18n";
import { handleRadioGroupKeydown } from "@/lib/radioGroup";

const props = defineProps({
  open: { type: Boolean, default: false },
  mapping: { type: Object, default: null },
  impact: { type: Object, default: null },
  replacementOptions: { type: Array, default: () => [] },
  mode: { type: String, default: "force" },
  replacementId: { type: String, default: null },
  loading: { type: Boolean, default: false },
});

const emit = defineEmits([
  "update:open",
  "update:mode",
  "update:replacementId",
  "confirm",
]);

const linkedCount = computed(
  () =>
    props.impact?.linkedProductsCount ??
    props.mapping?.linkedProductsCount ??
    0,
);
const hasLinkedProducts = computed(() => linkedCount.value > 0);

function productName(product) {
  return (
    product.name ||
    $t("marketplace.connectionPage.dialogs.productFallback", {
      id: product.id,
    })
  );
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent size="lg">
      <DialogHeader>
        <DialogTitle>
          {{
            $t("marketplace.connectionPage.dialogs.deleteMappingTitle", {
              name: mapping?.name || mapping?.localCategoryName,
            })
          }}
        </DialogTitle>
        <DialogDescription v-if="hasLinkedProducts">
          {{
            $t(
              "marketplace.connectionPage.dialogs.deleteMappingLinkedProducts",
              {
                count: linkedCount,
              },
            )
          }}
        </DialogDescription>
        <DialogDescription v-else>
          {{
            $t(
              "marketplace.connectionPage.dialogs.deleteMappingSimpleDescription",
            )
          }}
        </DialogDescription>
      </DialogHeader>

      <div v-if="hasLinkedProducts" class="space-y-4 py-2">
        <div
          class="space-y-4"
          role="radiogroup"
          :aria-label="
            $t('marketplace.connectionPage.dialogs.whatToDoWithProductsAria')
          "
        >
          <InsetGroup>
            <InsetGroupItem
              v-if="replacementOptions.length"
              as="div"
              role="radio"
              :aria-checked="mode === 'reassign'"
              :tabindex="mode === 'reassign' ? 0 : -1"
              :selected="mode === 'reassign'"
              @keydown="handleRadioGroupKeydown"
              @click="emit('update:mode', 'reassign')"
            >
              <template #leading><ArrowRightLeft class="size-4" /></template>
              <p>
                {{
                  $t(
                    "marketplace.connectionPage.dialogs.moveProductsToAnotherMapping",
                  )
                }}
              </p>
              <p class="mt-0.5 text-xs font-normal text-muted-foreground">
                {{ $t("marketplace.connectionPage.dialogs.moveProductsHint") }}
              </p>
            </InsetGroupItem>
            <InsetGroupItem
              as="div"
              role="radio"
              :aria-checked="mode === 'force'"
              :tabindex="
                mode === 'force' || !replacementOptions.length ? 0 : -1
              "
              :selected="mode === 'force'"
              @keydown="handleRadioGroupKeydown"
              @click="emit('update:mode', 'force')"
            >
              <template #leading><Unlink class="size-4" /></template>
              <p>
                {{
                  $t("marketplace.connectionPage.dialogs.deleteWithoutMoving")
                }}
              </p>
              <p class="mt-0.5 text-xs font-normal text-muted-foreground">
                {{
                  $t(
                    "marketplace.connectionPage.dialogs.deleteWithoutMovingHint",
                  )
                }}
              </p>
              <p
                v-if="!replacementOptions.length"
                class="mt-0.5 text-xs font-normal text-muted-foreground"
              >
                {{
                  $t(
                    "marketplace.connectionPage.dialogs.noReplacementMappingHint",
                  )
                }}
              </p>
            </InsetGroupItem>
          </InsetGroup>

          <Select
            v-if="mode === 'reassign' && replacementOptions.length"
            :model-value="replacementId ?? undefined"
            @update:model-value="emit('update:replacementId', $event)"
          >
            <SelectTrigger class="w-full">
              <SelectValue
                :placeholder="
                  $t(
                    'marketplace.connectionPage.dialogs.mappingToTransferPlaceholder',
                  )
                "
              />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="option in replacementOptions"
                :key="option.id"
                :value="String(option.id)"
              >
                {{
                  mappingDisplayName(
                    option,
                    $t("marketplace.connectionPage.dialogs.mappingFallback", {
                      id: option.id,
                    }),
                  )
                }}
              </SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div v-if="impact?.products?.length">
          <p class="mb-2 px-1 text-xs text-muted-foreground">
            {{ $t("marketplace.connectionPage.dialogs.linkedProducts") }}
          </p>
          <InsetGroup>
            <InsetGroupItem
              v-for="product in impact.products"
              :key="product.id"
              as="div"
            >
              <p class="truncate">{{ productName(product) }}</p>
              <template #trailing>
                <span
                  class="font-mono text-xs font-normal text-muted-foreground"
                >
                  {{ product.sku || `ID ${product.id}` }}
                </span>
              </template>
            </InsetGroupItem>
            <InsetGroupItem
              v-if="linkedCount > impact.products.length"
              as="div"
            >
              <p class="text-xs font-normal text-muted-foreground">
                {{
                  $t("marketplace.connectionPage.dialogs.andMoreCount", {
                    count: linkedCount - impact.products.length,
                  })
                }}
              </p>
            </InsetGroupItem>
          </InsetGroup>
        </div>
      </div>

      <DialogFooter>
        <DialogClose as-child>
          <Button variant="outline">{{ $t("common.cancel") }}</Button>
        </DialogClose>
        <Button
          variant="destructive"
          :disabled="
            loading ||
            (hasLinkedProducts && mode === 'reassign' && !replacementId)
          "
          @click="emit('confirm')"
        >
          <Spinner v-if="loading" class="mr-2" />
          {{
            hasLinkedProducts
              ? mode === "reassign"
                ? $t("marketplace.connectionPage.dialogs.moveAndDelete")
                : $t("marketplace.connectionPage.dialogs.deleteWithoutMoving")
              : $t("common.delete")
          }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
