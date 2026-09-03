<script setup>
import { computed, ref, watch } from "vue";
import { http } from "@/main";
import { getLocale, t, toBcp47 } from "@/i18n";
import {
  SHEET_WIDTH_DEFAULTS,
  SHEET_WIDTH_STORAGE,
} from "@/composables/useSheetResize";
import { isConfirmOverlayActive } from "@/composables/useConfirm";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import {
  canCancelOrder,
  decodeOrderLabelBytes,
  formatOrderMoney,
  orderChannelLabelKey,
  orderDisplayId,
  orderItemImageUrl,
  orderLabelContentType,
  orderLabelFileName,
  orderStatusClass,
  orderStatusLabelKey,
  orderStatusOf,
  primaryActionsForStatus,
} from "@/lib/ordersUi";
import { triggerBrowserDownload } from "@/lib/artifactDownload";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import Input from "@/components/ui/input/Input.vue";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import ProductImageCell from "@/components/products/ProductImageCell.vue";
import {
  Barcode,
  Check,
  CheckCircle,
  MapPin,
  Package,
  Truck,
  XCircle,
} from "lucide-vue-next";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetHeaderClose,
  SheetTitle,
} from "@/components/ui/sheet";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";

const ACTION_ICON = {
  confirm: Check,
  pack: Package,
  ship: Truck,
  deliver: MapPin,
  complete: CheckCircle,
  cancel: XCircle,
};

const ACTION_LABEL = {
  confirm: "pages.ordersPage.actionConfirm",
  pack: "pages.ordersPage.actionPack",
  ship: "pages.ordersPage.actionShip",
  deliver: "pages.ordersPage.actionDeliver",
  complete: "pages.ordersPage.actionComplete",
  cancel: "pages.ordersPage.actionCancel",
};

const ACTION_CONFIRM = {
  confirm: {
    title: "pages.ordersPage.confirmConfirmTitle",
    description: "pages.ordersPage.confirmConfirmDescription",
    label: "pages.ordersPage.actionConfirm",
    destructive: false,
  },
  pack: {
    title: "pages.ordersPage.confirmPackTitle",
    description: "pages.ordersPage.confirmPackDescription",
    label: "pages.ordersPage.actionPack",
    destructive: false,
  },
  ship: {
    title: "pages.ordersPage.confirmShipTitle",
    description: "pages.ordersPage.confirmShipDescription",
    label: "pages.ordersPage.actionShip",
    destructive: false,
  },
  deliver: {
    title: "pages.ordersPage.confirmDeliverTitle",
    description: "pages.ordersPage.confirmDeliverDescription",
    label: "pages.ordersPage.actionDeliver",
    destructive: false,
  },
  complete: {
    title: "pages.ordersPage.confirmCompleteTitle",
    description: "pages.ordersPage.confirmCompleteDescription",
    label: "pages.ordersPage.actionComplete",
    destructive: false,
  },
  cancel: {
    title: "pages.ordersPage.cancelTitle",
    description: "pages.ordersPage.cancelDescription",
    label: "pages.ordersPage.cancelConfirm",
    destructive: true,
  },
};

const props = defineProps({
  open: { type: Boolean, default: false },
  connectionId: { type: Number, default: null },
  orderId: { type: String, default: "" },
  preview: { type: Object, default: null },
  canMutate: { type: Boolean, default: false },
  canDownloadLabel: { type: Boolean, default: false },
});

const emit = defineEmits(["update:open", "updated"]);

const { formatDateTime: formatStoreDateTime, loadMemberships } =
  useStoreDateTime();
loadMemberships();

const detail = ref(null);
const loading = ref(false);
const acting = ref("");
const pendingAction = ref("");
const cancelReason = ref("");
const trackingNumber = ref("");
const deliveryService = ref("");
const confirmSpec = computed(() => ACTION_CONFIRM[pendingAction.value] ?? null);
const confirmOpen = computed({
  get: () => Boolean(pendingAction.value),
  set: (open) => {
    if (!open) pendingAction.value = "";
  },
});

const order = computed(() => detail.value || props.preview);
const title = computed(() =>
  order.value
    ? orderDisplayId(order.value)
    : t("pages.ordersPage.sheetTitleFallback"),
);
const status = computed(() => orderStatusOf(order.value));
const primaryActions = computed(() => primaryActionsForStatus(status.value));
const showCancel = computed(() => canCancelOrder(status.value));
const showFooter = computed(
  () =>
    Boolean(order.value) &&
    (props.canDownloadLabel ||
      (props.canMutate && (primaryActions.value.length || showCancel.value))),
);
const items = computed(() => {
  const list = order.value?.items ?? order.value?.Items ?? [];
  return Array.isArray(list) ? list : [];
});
const locale = computed(() => toBcp47(getLocale()));
const createdLabel = computed(() => {
  const when = formatStoreDateTime(
    order.value?.createdAt ?? order.value?.CreatedAt,
  );
  return when && when !== "-" ? when : "";
});
const customerName = computed(
  () => order.value?.customerName ?? order.value?.CustomerName ?? "",
);
const customerPhone = computed(
  () => order.value?.customerPhone ?? order.value?.CustomerPhone ?? "",
);
const customerEmail = computed(
  () => order.value?.customerEmail ?? order.value?.CustomerEmail ?? "",
);
const shippingAddress = computed(
  () => order.value?.shippingAddress ?? order.value?.ShippingAddress ?? "",
);
const comment = computed(
  () => order.value?.comment ?? order.value?.Comment ?? "",
);
const channelKey = computed(() =>
  orderChannelLabelKey(order.value?.channel ?? order.value?.Channel),
);

function unwrap(response) {
  const data = extractResponseData(response, {});
  if (data?.value && typeof data.value === "object") return data.value;
  return data;
}

async function loadDetail() {
  if (!props.open || !props.connectionId || !props.orderId) {
    detail.value = null;
    return;
  }
  loading.value = true;
  try {
    const response = await http.get(
      `/marketplace/connections/${props.connectionId}/orders/${encodeURIComponent(props.orderId)}`,
      { cache: false },
    );
    detail.value = unwrap(response);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    loading.value = false;
  }
}

watch(
  () => [props.open, props.connectionId, props.orderId],
  () => {
    if (props.open) {
      loadDetail();
      return;
    }
    detail.value = null;
    pendingAction.value = "";
    cancelReason.value = "";
    trackingNumber.value = "";
    deliveryService.value = "";
  },
  { immediate: true },
);

function onOpenChange(value) {
  if (!value && (isConfirmOverlayActive() || pendingAction.value)) {
    emit("update:open", true);
    return;
  }
  emit("update:open", value);
}

function preventConfirmOutside(event) {
  event.preventDefault();
}

function actionExtra(action) {
  const extra = {};
  if (action === "ship") {
    const tracking = trackingNumber.value.trim();
    const service = deliveryService.value.trim();
    if (tracking) extra.trackingNumber = tracking;
    if (service) extra.deliveryService = service;
  }
  if (action === "cancel") {
    const reason = cancelReason.value.trim();
    if (reason) extra.reason = reason;
  }
  return extra;
}

function openActionConfirm(action) {
  if (!ACTION_CONFIRM[action]) return;
  if (action === "ship") {
    trackingNumber.value = "";
    deliveryService.value = "";
  }
  if (action === "cancel") cancelReason.value = "";
  pendingAction.value = action;
}

let armedAction = "";
let armedExtra = {};

function armConfirmSubmit() {
  armedAction = pendingAction.value;
  armedExtra = actionExtra(armedAction);
}

async function downloadLabel() {
  if (!props.canDownloadLabel || !props.connectionId || !props.orderId) return;
  acting.value = "label";
  try {
    const response = await http.get(
      `/marketplace/connections/${props.connectionId}/orders/${encodeURIComponent(props.orderId)}/label`,
      { cache: false },
    );
    const label = unwrap(response);
    const bytes = decodeOrderLabelBytes(label);
    if (!bytes) {
      displaySonnerError(t("pages.ordersPage.labelEmpty"));
      return;
    }
    const blob = new Blob([bytes], { type: orderLabelContentType(label) });
    const url = URL.createObjectURL(blob);
    triggerBrowserDownload(
      url,
      orderLabelFileName(label, `label-${props.orderId}`),
    );
    URL.revokeObjectURL(url);
    displaySonnerSuccess(t("pages.ordersPage.labelDownloaded"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    acting.value = "";
  }
}

async function runAction(action, extra = {}) {
  if (!props.canMutate || !props.connectionId || !props.orderId) return;
  acting.value = action;
  try {
    const response = await http.post(
      `/marketplace/connections/${props.connectionId}/orders/${encodeURIComponent(props.orderId)}/status`,
      { action, ...extra },
    );
    const result = unwrap(response);
    const nextOrder = result?.order ?? result?.Order;
    if (nextOrder) detail.value = nextOrder;
    displaySonnerSuccess(t("pages.ordersPage.statusUpdated"));
    emit("updated", nextOrder || detail.value);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    acting.value = "";
  }
}

async function onConfirmSubmit() {
  if (!armedAction) armConfirmSubmit();
  const action = armedAction;
  const extra = armedExtra;
  armedAction = "";
  armedExtra = {};
  pendingAction.value = "";
  cancelReason.value = "";
  trackingNumber.value = "";
  deliveryService.value = "";
  if (!action) return;
  await runAction(action, extra);
}

function itemName(item) {
  return (
    item?.name ||
    item?.Name ||
    item?.sku ||
    item?.Sku ||
    item?.offerId ||
    item?.OfferId ||
    t("common.emDash")
  );
}

function itemImage(item) {
  return orderItemImageUrl(item);
}

function itemMoney(item) {
  const price =
    item?.discountedPrice ??
    item?.DiscountedPrice ??
    item?.price ??
    item?.Price;
  const currency = order.value?.currency ?? order.value?.Currency;
  return formatOrderMoney(price, currency, locale.value);
}
</script>

<template>
  <Sheet :open="open" @update:open="onOpenChange">
    <SheetContent
      side="right"
      inset
      resizable
      :show-close="false"
      class="flex w-full flex-col gap-0 p-0"
      :default-width="SHEET_WIDTH_DEFAULTS.orders"
      :storage-key="SHEET_WIDTH_STORAGE.orders"
    >
      <SheetHeader variant="bar">
        <div class="flex min-h-8 items-center gap-2">
          <SheetTitle class="min-w-0 flex-1 truncate">
            {{ title }}
          </SheetTitle>
          <SheetHeaderClose />
        </div>
        <SheetDescription v-if="order">
          <span :class="orderStatusClass(status)">{{
            t(orderStatusLabelKey(status))
          }}</span>
          <span class="text-muted-foreground">
            · {{ t(channelKey) }}
            <template v-if="createdLabel"> · {{ createdLabel }}</template>
          </span>
        </SheetDescription>
      </SheetHeader>

      <div class="flex min-h-0 flex-1 flex-col overflow-y-auto px-4 py-4">
        <EmptyState
          v-if="loading && !order"
          loading
          :framed="false"
          class="min-h-[12rem]"
        />
        <template v-else-if="order">
          <section>
            <InsetGroupLabel>{{
              t("pages.ordersPage.sheetBuyer")
            }}</InsetGroupLabel>
            <InsetGroup>
              <InsetGroupItem as="div">
                <p class="truncate text-sm">
                  {{ customerName || t("common.emDash") }}
                </p>
                <p
                  v-if="customerPhone"
                  class="mt-0.5 text-xs text-muted-foreground"
                >
                  {{ t("pages.ordersPage.sheetPhone") }} · {{ customerPhone }}
                </p>
                <p
                  v-if="customerEmail"
                  class="mt-0.5 text-xs text-muted-foreground"
                >
                  {{ t("pages.ordersPage.sheetEmail") }} · {{ customerEmail }}
                </p>
              </InsetGroupItem>
            </InsetGroup>
          </section>

          <section v-if="shippingAddress" class="mt-6">
            <InsetGroupLabel>{{
              t("pages.ordersPage.sheetAddress")
            }}</InsetGroupLabel>
            <InsetGroup>
              <InsetGroupItem as="div">
                <p class="text-sm whitespace-pre-wrap">{{ shippingAddress }}</p>
              </InsetGroupItem>
            </InsetGroup>
          </section>

          <section v-if="comment" class="mt-6">
            <InsetGroupLabel>{{
              t("pages.ordersPage.sheetComment")
            }}</InsetGroupLabel>
            <InsetGroup>
              <InsetGroupItem as="div">
                <p class="text-sm whitespace-pre-wrap">{{ comment }}</p>
              </InsetGroupItem>
            </InsetGroup>
          </section>

          <section class="mt-6">
            <InsetGroupLabel>{{
              t("pages.ordersPage.sheetItems")
            }}</InsetGroupLabel>
            <InsetGroup v-if="items.length">
              <InsetGroupItem
                v-for="(item, index) in items"
                :key="item.externalProductId || item.sku || index"
                as="div"
                class="items-center"
              >
                <div class="flex min-w-0 items-center gap-3">
                  <ProductImageCell
                    :thumbnail-url="itemImage(item)"
                    :image-url="itemImage(item)"
                    image-class="size-11"
                    :stop-events="false"
                  />
                  <div class="min-w-0 flex-1">
                    <p class="truncate text-sm">{{ itemName(item) }}</p>
                    <p class="text-xs text-muted-foreground">
                      {{
                        t("pages.ordersPage.itemQty", {
                          count: item.quantity ?? item.Quantity ?? 0,
                        })
                      }}
                      <template v-if="itemMoney(item)">
                        · {{ itemMoney(item) }}
                      </template>
                    </p>
                  </div>
                </div>
              </InsetGroupItem>
            </InsetGroup>
            <p v-else class="px-1 text-sm text-muted-foreground">
              {{ t("common.emDash") }}
            </p>
          </section>
        </template>
      </div>

      <SheetFooter
        v-if="showFooter"
        variant="bar"
        class="flex-wrap gap-2 sm:justify-start"
      >
        <Button
          v-if="canDownloadLabel"
          type="button"
          variant="outline"
          size="sm"
          :disabled="Boolean(acting)"
          @click="downloadLabel"
        >
          <Spinner v-if="acting === 'label'" size="sm" />
          <Barcode v-else class="size-3.5" />
          {{ t("pages.ordersPage.actionDownloadLabel") }}
        </Button>
        <template v-if="canMutate">
          <Button
            v-for="action in primaryActions"
            :key="action"
            type="button"
            size="sm"
            :disabled="Boolean(acting)"
            @click="openActionConfirm(action)"
          >
            <Spinner v-if="acting === action" size="sm" />
            <component :is="ACTION_ICON[action]" v-else class="size-3.5" />
            {{ t(ACTION_LABEL[action]) }}
          </Button>
          <Button
            v-if="showCancel"
            type="button"
            variant="destructive"
            size="sm"
            :disabled="Boolean(acting)"
            @click="openActionConfirm('cancel')"
          >
            <Spinner v-if="acting === 'cancel'" size="sm" />
            <XCircle v-else class="size-3.5" />
            {{ t("pages.ordersPage.actionCancel") }}
          </Button>
        </template>
      </SheetFooter>

      <AlertDialog v-model:open="confirmOpen">
        <AlertDialogContent
          @pointer-down-outside="preventConfirmOutside"
          @focus-outside="preventConfirmOutside"
          @interact-outside="preventConfirmOutside"
        >
          <AlertDialogHeader>
            <AlertDialogTitle>{{
              confirmSpec ? t(confirmSpec.title) : ""
            }}</AlertDialogTitle>
            <AlertDialogDescription>
              {{ confirmSpec ? t(confirmSpec.description) : "" }}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <FieldGroup v-if="pendingAction === 'ship'" class="gap-3">
            <SettingsFormField
              :label="t('pages.ordersPage.shipTrackingLabel')"
              label-for="order-ship-tracking"
            >
              <Input id="order-ship-tracking" v-model="trackingNumber" />
            </SettingsFormField>
            <SettingsFormField
              :label="t('pages.ordersPage.shipServiceLabel')"
              label-for="order-ship-service"
            >
              <Input id="order-ship-service" v-model="deliveryService" />
            </SettingsFormField>
          </FieldGroup>
          <FieldGroup v-else-if="pendingAction === 'cancel'" class="gap-3">
            <SettingsFormField
              :label="t('pages.ordersPage.cancelReasonLabel')"
              label-for="order-cancel-reason"
            >
              <Input
                id="order-cancel-reason"
                v-model="cancelReason"
                :placeholder="t('pages.ordersPage.cancelReasonPlaceholder')"
              />
            </SettingsFormField>
          </FieldGroup>
          <AlertDialogFooter>
            <AlertDialogCancel>{{ t("common.cancel") }}</AlertDialogCancel>
            <AlertDialogAction
              :variant="confirmSpec?.destructive ? 'destructive' : 'default'"
              :disabled="Boolean(acting)"
              @pointerdown="armConfirmSubmit"
              @click="onConfirmSubmit"
            >
              {{ confirmSpec ? t(confirmSpec.label) : "" }}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </SheetContent>
  </Sheet>
</template>
