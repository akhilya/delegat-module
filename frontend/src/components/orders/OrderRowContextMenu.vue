<script setup>
/**
 * Right-click menu for a mailbox or packing-overview order row.
 */
import {
  Barcode,
  CheckCircle,
  ClipboardList,
  MapPin,
  Package,
  Truck,
  XCircle,
} from "lucide-vue-next";
import {
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
} from "@/components/ui/context-menu";
import ProductImageSizeContextSub from "@/components/products/ProductImageSizeContextSub.vue";
import { computed } from "vue";
import { ORDER_ACTION_LABEL, orderBulkCapabilities } from "@/lib/ordersUi";

const ACTION_ICONS = {
  confirm: CheckCircle,
  pack: Package,
  ship: Truck,
  deliver: MapPin,
  complete: CheckCircle,
  cancel: XCircle,
};

const props = defineProps({
  order: { type: Object, required: true },
  adapterKey: { type: String, default: "" },
  canMutate: { type: Boolean, default: false },
  canDownloadLabel: { type: Boolean, default: false },
});

defineEmits(["open", "action", "download-label"]);

const caps = computed(() =>
  orderBulkCapabilities([props.order], props.adapterKey, {
    canMutate: props.canMutate,
    canDownloadLabel: props.canDownloadLabel,
  }),
);

const hasRowActions = computed(
  () =>
    caps.value.actions.length > 0 ||
    caps.value.canCancel ||
    caps.value.canDownloadLabel,
);
</script>

<template>
  <ContextMenuContent class="w-56">
    <ContextMenuItem @click="$emit('open')">
      <ClipboardList />
      {{ $t("pages.ordersPage.openOrder") }}
    </ContextMenuItem>
    <template v-if="hasRowActions">
      <ContextMenuSeparator />
      <ContextMenuItem
        v-for="action in caps.actions"
        :key="action"
        @click="$emit('action', action)"
      >
        <component :is="ACTION_ICONS[action]" />
        {{ $t(ORDER_ACTION_LABEL[action]) }}
      </ContextMenuItem>
      <ContextMenuItem
        v-if="caps.canDownloadLabel"
        @click="$emit('download-label')"
      >
        <Barcode />
        {{ $t("pages.ordersPage.actionDownloadLabel") }}
      </ContextMenuItem>
      <ContextMenuItem
        v-if="caps.canCancel"
        variant="destructive"
        @click="$emit('action', 'cancel')"
      >
        <XCircle />
        {{ $t("pages.ordersPage.actionCancel") }}
      </ContextMenuItem>
    </template>
    <ContextMenuSeparator />
    <ProductImageSizeContextSub />
  </ContextMenuContent>
</template>
