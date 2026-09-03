<script setup>
/**
 * Mailbox products: one identity row per line item. Height follows the count.
 * Four or more lines collapse behind an Apple-style Show N more disclosure.
 */
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { ChevronDown } from "lucide-vue-next";
import ProductIdentityCell from "@/components/products/ProductIdentityCell.vue";
import {
  ORDER_ITEM_PREVIEW_MAX,
  orderHiddenLineCount,
  orderItemImageUrl,
  orderItemName,
  orderItemQuantity,
  orderItemSku,
  orderItemsNeedCollapse,
  orderVisibleItems,
} from "@/lib/ordersUi";

const props = defineProps({
  row: { type: Object, required: true },
});

const { t } = useI18n();
const expanded = ref(false);

const needsCollapse = computed(() =>
  orderItemsNeedCollapse(props.row, ORDER_ITEM_PREVIEW_MAX),
);
const hiddenCount = computed(() =>
  orderHiddenLineCount(props.row, ORDER_ITEM_PREVIEW_MAX),
);
const visibleItems = computed(() =>
  orderVisibleItems(props.row, expanded.value, ORDER_ITEM_PREVIEW_MAX),
);

function lineCaption(item) {
  const sku = orderItemSku(item);
  const name = orderItemName(item);
  if (sku && sku !== name) return sku;
  return "";
}

function lineKey(item, index) {
  return `${index}-${orderItemSku(item) || orderItemName(item) || "line"}`;
}

function toggleExpanded(event) {
  event.preventDefault();
  event.stopPropagation();
  expanded.value = !expanded.value;
}
</script>

<template>
  <div v-if="visibleItems.length === 0" class="flex h-11 items-center">
    <span class="text-sm text-muted-foreground">{{ t("common.emDash") }}</span>
  </div>
  <div v-else class="flex min-w-0 flex-col gap-2.5">
    <ProductIdentityCell
      v-for="(item, index) in visibleItems"
      :key="lineKey(item, index)"
      :name="orderItemName(item)"
      :caption="lineCaption(item)"
      :thumbnail-url="orderItemImageUrl(item)"
      :image-url="orderItemImageUrl(item)"
    >
      <template v-if="orderItemQuantity(item) > 1" #meta>
        <span class="text-xs font-normal tabular-nums text-muted-foreground">
          {{ t("pages.ordersPage.itemQty", { count: orderItemQuantity(item) }) }}
        </span>
      </template>
    </ProductIdentityCell>
    <button
      v-if="needsCollapse"
      type="button"
      class="flex w-fit items-center gap-1 rounded-md text-xs font-medium text-muted-foreground transition-colors duration-[var(--duration-press)] ease-[var(--ease-out-quart)] hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
      :aria-expanded="expanded"
      @click="toggleExpanded"
    >
      <ChevronDown
        class="size-3.5 shrink-0 transition-transform duration-[var(--duration-press)] ease-[var(--ease-out-quart)] motion-reduce:transition-none"
        :class="expanded && 'rotate-180'"
        aria-hidden="true"
      />
      {{
        expanded
          ? t("pages.ordersPage.itemsShowLess")
          : t("pages.ordersPage.itemsShowMore", { n: hiddenCount })
      }}
    </button>
  </div>
</template>
