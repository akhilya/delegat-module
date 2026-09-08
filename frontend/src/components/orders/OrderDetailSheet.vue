<script setup>
import { computed, ref, watch } from "vue";
import { RouterLink } from "vue-router";
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
  ORDER_ACTION_CONFIRM,
  ORDER_ACTION_LABEL,
  ORDER_STATUS,
  canCancelOrder,
  canDownloadBoxLabel,
  canDownloadOrderLabel,
  canDownloadShipmentQr,
  formatOrderMoney,
  marketplaceOrderLabelUrl,
  orderActionNeedsShipmentFields,
  orderBoxId,
  orderChannelLabelKey,
  orderChannelOf,
  orderDeliveryIsMarketplaceOwned,
  orderDisplayId,
  orderItemImageUrl,
  orderItemName,
  orderItemProductTo,
  orderItemSku,
  orderStatusClass,
  orderStatusLabelKey,
  orderStatusOf,
  orderSupplyId,
  primaryActionsForStatus,
  saveOrderLabelFile,
} from "@/lib/ordersUi";
import {
  indexFulfillmentWarehouses,
  orderBuyer,
  orderDelivery,
  orderDiscountAmount,
  orderItemBarcode,
  orderSheetHeadlineFacts,
  orderSheetMetaFacts,
  orderTotalAmount,
} from "@/lib/orderSheetFacts.js";
import { ORDER_ACTION_ICONS } from "@/lib/orderActionIcons";
import { triggerBrowserDownload } from "@/lib/artifactDownload";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import Input from "@/components/ui/input/Input.vue";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import ProductImageCell from "@/components/products/ProductImageCell.vue";
import {
  Barcode,
  Check,
  Copy,
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

const ACTION_LABEL = ORDER_ACTION_LABEL;

const props = defineProps({
  open: { type: Boolean, default: false },
  connectionId: { type: Number, default: null },
  orderId: { type: String, default: "" },
  adapterKey: { type: String, default: "" },
  preview: { type: Object, default: null },
  canMutate: { type: Boolean, default: false },
  canDownloadLabel: { type: Boolean, default: false },
  canManageShipments: { type: Boolean, default: false },
  canGetFulfillmentLocations: { type: Boolean, default: false },
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
const packSupplyChoice = ref("new");
const packBoxChoice = ref("none");
const openShipments = ref([]);
const shipmentOffices = ref([]);
const shipmentBoxes = ref([]);
const packingOptionsLoading = ref(false);
const warehouseById = ref({});
const copiedFactId = ref("");
let copiedFactTimer = 0;
const confirmSpec = computed(() => {
  const spec = ORDER_ACTION_CONFIRM[pendingAction.value];
  if (!spec) return null;
  if (pendingAction.value === "ship" && props.canManageShipments) {
    return {
      ...spec,
      description: "pages.ordersPage.confirmShipSupplyDescription",
    };
  }
  return spec;
});
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
const channel = computed(() => orderChannelOf(order.value));
const primaryActions = computed(() =>
  primaryActionsForStatus(status.value, props.adapterKey, channel.value),
);
const showCancel = computed(() =>
  canCancelOrder(status.value, props.adapterKey, channel.value),
);
const showLabelButton = computed(
  () =>
    props.canDownloadLabel &&
    canDownloadOrderLabel(status.value, props.adapterKey, channel.value),
);
const supplyId = computed(() => orderSupplyId(order.value));
const boxId = computed(() => orderBoxId(order.value));
const showBoxLabelButton = computed(
  () =>
    props.canManageShipments &&
    canDownloadBoxLabel(status.value, props.adapterKey, boxId.value),
);
const showSupplyQrButton = computed(
  () =>
    props.canManageShipments &&
    canDownloadShipmentQr(status.value, props.adapterKey, supplyId.value),
);
const showShipmentFacts = computed(
  () => Boolean(supplyId.value || boxId.value),
);
const showPackShipmentFields = computed(
  () => props.canManageShipments && pendingAction.value === "pack",
);
const selectedPackShipment = computed(() => {
  const id = packSupplyChoice.value;
  if (!id || id === "new") return null;
  return (
    openShipments.value.find((row) => shipmentExternalId(row) === id) || null
  );
});
const packOfficeLabel = computed(() => {
  const shipment = selectedPackShipment.value;
  if (!shipment) return "";
  const locationId = String(
    shipment.locationId ?? shipment.LocationId ?? "",
  ).trim();
  const extra = shipment.extra ?? shipment.Extra ?? {};
  const recommended = String(
    extra.recommendedWhId ?? extra.RecommendedWhId ?? "",
  ).trim();
  const target = locationId || recommended;
  if (!target) return "";
  const office = shipmentOffices.value.find(
    (row) => locationExternalId(row) === target,
  );
  if (!office) return target;
  const name = office.name || office.Name || target;
  const address = office.address || office.Address;
  return address ? `${name} · ${address}` : name;
});
const showShipFields = computed(() =>
  orderActionNeedsShipmentFields(pendingAction.value, props.adapterKey),
);
const showMarketplaceDeliveryHint = computed(
  () =>
    orderDeliveryIsMarketplaceOwned(props.adapterKey) &&
    (status.value === ORDER_STATUS.Delivering ||
      status.value === ORDER_STATUS.Delivered),
);
const showFooter = computed(
  () =>
    Boolean(order.value) &&
    (showLabelButton.value ||
      showBoxLabelButton.value ||
      showSupplyQrButton.value ||
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
const buyer = computed(() => orderBuyer(order.value));
const delivery = computed(() => orderDelivery(order.value));
const totalLabel = computed(() => {
  const amount = orderTotalAmount(order.value);
  if (amount == null) return "";
  const currency = order.value?.currency ?? order.value?.Currency;
  return formatOrderMoney(amount, currency, locale.value);
});
const discountLabel = computed(() => {
  const amount = orderDiscountAmount(order.value);
  if (amount == null) return "";
  const currency = order.value?.currency ?? order.value?.Currency;
  return formatOrderMoney(amount, currency, locale.value);
});
const channelKey = computed(() =>
  orderChannelLabelKey(order.value?.channel ?? order.value?.Channel),
);
const headlineFacts = computed(() =>
  orderSheetHeadlineFacts({
    status: status.value ? t(orderStatusLabelKey(status.value)) : "",
    channel: channelKey.value ? t(channelKey.value) : "",
    created: createdLabel.value,
    total: totalLabel.value,
  }),
);
const metaFacts = computed(() =>
  orderSheetMetaFacts(order.value, {
    warehouseById: warehouseById.value,
    discountText: discountLabel.value,
  }),
);
const facts = computed(() => [...headlineFacts.value, ...metaFacts.value]);

function unwrap(response) {
  const data = extractResponseData(response, {});
  if (data?.value && typeof data.value === "object") return data.value;
  return data;
}

function shipmentExternalId(row) {
  return String(row?.externalId ?? row?.ExternalId ?? "").trim();
}

function shipmentName(row) {
  return (
    row?.name ||
    row?.Name ||
    shipmentExternalId(row) ||
    t("common.emDash")
  );
}

function locationExternalId(row) {
  return String(row?.externalId ?? row?.ExternalId ?? "").trim();
}

function boxExternalId(row) {
  return String(row?.externalId ?? row?.ExternalId ?? "").trim();
}

function resetPackFields() {
  packSupplyChoice.value = "new";
  packBoxChoice.value = "none";
  openShipments.value = [];
  shipmentOffices.value = [];
  shipmentBoxes.value = [];
}

async function loadPackOptions() {
  if (!props.canManageShipments || !props.connectionId) return;
  packingOptionsLoading.value = true;
  try {
    const [shipmentsResponse, officesResponse] = await Promise.all([
      http.get(`/marketplace/connections/${props.connectionId}/shipments`, {
        params: { openOnly: true, limit: 50 },
        cache: false,
      }),
      http.get(
        `/marketplace/connections/${props.connectionId}/fulfillment/locations`,
        { params: { kind: "marketplace_office" }, cache: false },
      ),
    ]);
    const shipments = unwrap(shipmentsResponse);
    const offices = unwrap(officesResponse);
    openShipments.value = (Array.isArray(shipments?.items) ? shipments.items : []).filter(
      (row) => shipmentExternalId(row),
    );
    shipmentOffices.value = (Array.isArray(offices?.items) ? offices.items : []).filter(
      (row) => locationExternalId(row),
    );
  } catch (error) {
    displaySonnerError(error);
    openShipments.value = [];
    shipmentOffices.value = [];
  } finally {
    packingOptionsLoading.value = false;
  }
}

async function loadBoxesForSupply(shipmentId) {
  if (!props.connectionId || !shipmentId || shipmentId === "new") {
    shipmentBoxes.value = [];
    return;
  }
  try {
    const response = await http.get(
      `/marketplace/connections/${props.connectionId}/shipments/${encodeURIComponent(shipmentId)}/boxes`,
      { cache: false },
    );
    const data = unwrap(response);
    shipmentBoxes.value = (Array.isArray(data?.items) ? data.items : []).filter(
      (row) => boxExternalId(row),
    );
  } catch (error) {
    displaySonnerError(error);
    shipmentBoxes.value = [];
  }
}

watch(packSupplyChoice, (value) => {
  if (pendingAction.value !== "pack") return;
  packBoxChoice.value = "none";
  loadBoxesForSupply(value);
});

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

async function loadWarehouseNames() {
  warehouseById.value = {};
  if (!props.open || !props.canGetFulfillmentLocations || !props.connectionId) {
    return;
  }
  try {
    const response = await http.get(
      `/marketplace/connections/${props.connectionId}/fulfillment/locations`,
      { cache: false },
    );
    const data = unwrap(response);
    const rows = Array.isArray(data?.items) ? data.items : [];
    warehouseById.value = indexFulfillmentWarehouses(rows);
  } catch {
    warehouseById.value = {};
  }
}

watch(
  () => [props.open, props.connectionId, props.orderId],
  async () => {
    if (props.open) {
      await loadDetail();
      await loadWarehouseNames();
      return;
    }
    detail.value = null;
    pendingAction.value = "";
    cancelReason.value = "";
    trackingNumber.value = "";
    deliveryService.value = "";
    warehouseById.value = {};
    copiedFactId.value = "";
    resetPackFields();
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
    const currentSupply = supplyId.value;
    if (props.canManageShipments && currentSupply) {
      extra.extra = { supplyId: currentSupply };
    }
  }
  if (action === "cancel") {
    const reason = cancelReason.value.trim();
    if (reason) extra.reason = reason;
  }
  return extra;
}

function openActionConfirm(action) {
  if (!ORDER_ACTION_CONFIRM[action]) return;
  if (action === "ship") {
    trackingNumber.value = "";
    deliveryService.value = "";
  }
  if (action === "cancel") cancelReason.value = "";
  if (action === "pack" && props.canManageShipments) {
    resetPackFields();
    loadPackOptions();
  }
  pendingAction.value = action;
}

let armedAction = "";
let armedExtra = {};

function armConfirmSubmit() {
  armedAction = pendingAction.value;
  armedExtra = actionExtra(armedAction);
}

async function buildPackExtra() {
  if (!props.connectionId) return {};
  let chosenSupplyId = packSupplyChoice.value;
  if (!chosenSupplyId || chosenSupplyId === "new") {
    const response = await http.post(
      `/marketplace/connections/${props.connectionId}/shipments`,
      { name: `Delegat ${new Date().toISOString().slice(0, 16).replace("T", " ")}` },
    );
    const created = unwrap(response);
    const shipment = created?.shipment ?? created?.Shipment;
    chosenSupplyId = shipmentExternalId(shipment);
    if (!chosenSupplyId) {
      throw new Error(t("pages.ordersPage.shipmentCreateFailed"));
    }
  }
  const extra = { supplyId: chosenSupplyId };
  if (packBoxChoice.value === "new") extra.createBox = "true";
  else if (packBoxChoice.value && packBoxChoice.value !== "none") {
    extra.boxId = packBoxChoice.value;
  }
  return { extra };
}

async function downloadMarketplaceLabel(kind) {
  if (!props.connectionId) return;
  let path = "";
  let fallbackName = `label-${props.orderId}`;
  if (kind === "order") {
    if (!showLabelButton.value || !props.orderId) return;
    path = marketplaceOrderLabelUrl(props.connectionId, props.orderId);
  } else if (kind === "box") {
    if (!showBoxLabelButton.value || !supplyId.value || !boxId.value) return;
    path = `/marketplace/connections/${props.connectionId}/shipments/${encodeURIComponent(supplyId.value)}/boxes/${encodeURIComponent(boxId.value)}/label`;
    fallbackName = `box-${boxId.value}`;
  } else if (kind === "supply") {
    if (!showSupplyQrButton.value || !supplyId.value) return;
    path = `/marketplace/connections/${props.connectionId}/shipments/${encodeURIComponent(supplyId.value)}/label`;
    fallbackName = `supply-${supplyId.value}`;
  } else {
    return;
  }

  acting.value = kind === "order" ? "label" : kind;
  try {
    const response = await http.get(path, { cache: false });
    const label = unwrap(response);
    saveOrderLabelFile(label, fallbackName, {
      t,
      triggerDownload: triggerBrowserDownload,
    });
    displaySonnerSuccess(t("pages.ordersPage.labelDownloaded"));
  } catch (error) {
    displaySonnerError(error);
  } finally {
    acting.value = "";
  }
}

async function downloadLabel() {
  await downloadMarketplaceLabel("order");
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
  let extra = armedExtra;
  armedAction = "";
  armedExtra = {};
  pendingAction.value = "";
  cancelReason.value = "";
  trackingNumber.value = "";
  deliveryService.value = "";
  if (!action) return;
  try {
    if (action === "pack" && props.canManageShipments) {
      extra = await buildPackExtra();
    }
    await runAction(action, extra);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    resetPackFields();
  }
}

function itemName(item) {
  return orderItemName(item) || t("common.emDash");
}

function itemSku(item) {
  return orderItemSku(item);
}

function itemBarcode(item) {
  return orderItemBarcode(item);
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

function factValue(fact) {
  if (fact?.id === "testOrder") return t("common.yes");
  return fact?.value ?? "";
}

async function copyFact(id, value) {
  const text = String(value || "").trim();
  if (!text) return;
  try {
    await navigator.clipboard.writeText(text);
    copiedFactId.value = id;
    displaySonnerSuccess(t("common.copied"));
    window.clearTimeout(copiedFactTimer);
    copiedFactTimer = window.setTimeout(() => {
      if (copiedFactId.value === id) copiedFactId.value = "";
    }, 1500);
  } catch (error) {
    displaySonnerError(error);
  }
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
        <SheetDescription v-if="order" class="sr-only">
          {{ t(orderStatusLabelKey(status)) }}
          {{ t(channelKey) }}
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
          <div class="flex flex-col gap-6">
            <section v-if="facts.length">
              <InsetGroupLabel>{{
                t("pages.ordersPage.sheetFacts")
              }}</InsetGroupLabel>
              <InsetGroup>
                <InsetGroupItem
                  v-for="fact in facts"
                  :key="fact.id"
                  as="div"
                >
                  <p class="text-xs text-muted-foreground">
                    {{ t(fact.labelKey) }}
                  </p>
                  <p
                    class="text-sm"
                    :class="
                      fact.id === 'status'
                        ? orderStatusClass(status)
                        : 'truncate'
                    "
                  >
                    {{ factValue(fact) }}
                  </p>
                  <p
                    v-if="fact.detail"
                    class="text-xs text-muted-foreground"
                  >
                    {{ fact.detail }}
                  </p>
                  <template v-if="fact.copy" #trailing>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :aria-label="
                        t('pages.ordersPage.sheetCopyAria', {
                          label: t(fact.labelKey),
                        })
                      "
                      @click.stop="copyFact(fact.id, fact.value)"
                    >
                      <Check
                        v-if="copiedFactId === fact.id"
                        class="size-3.5"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </section>

            <section v-if="showMarketplaceDeliveryHint">
              <InsetGroup>
                <InsetGroupItem as="div">
                  <p class="text-sm text-muted-foreground">
                    {{ t("pages.ordersPage.marketplaceDeliveryHint") }}
                  </p>
                </InsetGroupItem>
              </InsetGroup>
            </section>

            <section v-if="buyer">
              <InsetGroupLabel>{{
                t("pages.ordersPage.sheetBuyer")
              }}</InsetGroupLabel>
              <InsetGroup>
                <InsetGroupItem v-if="buyer.name" as="div">
                  <p class="truncate text-sm">{{ buyer.name }}</p>
                </InsetGroupItem>
                <InsetGroupItem v-if="buyer.phone" as="div">
                  <p class="text-xs text-muted-foreground">
                    {{ t("pages.ordersPage.sheetPhone") }}
                  </p>
                  <p class="truncate text-sm">{{ buyer.phone }}</p>
                  <template #trailing>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :aria-label="
                        t('pages.ordersPage.sheetCopyAria', {
                          label: t('pages.ordersPage.sheetPhone'),
                        })
                      "
                      @click.stop="copyFact('phone', buyer.phone)"
                    >
                      <Check
                        v-if="copiedFactId === 'phone'"
                        class="size-3.5"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                  </template>
                </InsetGroupItem>
                <InsetGroupItem v-if="buyer.email" as="div">
                  <p class="text-xs text-muted-foreground">
                    {{ t("pages.ordersPage.sheetEmail") }}
                  </p>
                  <p class="truncate text-sm">{{ buyer.email }}</p>
                  <template #trailing>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :aria-label="
                        t('pages.ordersPage.sheetCopyAria', {
                          label: t('pages.ordersPage.sheetEmail'),
                        })
                      "
                      @click.stop="copyFact('email', buyer.email)"
                    >
                      <Check
                        v-if="copiedFactId === 'email'"
                        class="size-3.5"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </section>

            <section v-if="delivery">
              <InsetGroupLabel>{{
                t("pages.ordersPage.sheetDelivery")
              }}</InsetGroupLabel>
              <InsetGroup>
                <InsetGroupItem v-if="delivery.address" as="div">
                  <p class="text-xs text-muted-foreground">
                    {{ t("pages.ordersPage.sheetAddress") }}
                  </p>
                  <p class="text-sm whitespace-pre-wrap">
                    {{ delivery.address }}
                  </p>
                </InsetGroupItem>
                <InsetGroupItem v-if="delivery.comment" as="div">
                  <p class="text-xs text-muted-foreground">
                    {{ t("pages.ordersPage.sheetComment") }}
                  </p>
                  <p class="text-sm whitespace-pre-wrap">
                    {{ delivery.comment }}
                  </p>
                </InsetGroupItem>
                <InsetGroupItem v-if="delivery.tracking" as="div">
                  <p class="text-xs text-muted-foreground">
                    {{ t("pages.ordersPage.sheetTracking") }}
                  </p>
                  <p class="truncate text-sm">{{ delivery.tracking }}</p>
                  <template #trailing>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :aria-label="
                        t('pages.ordersPage.sheetCopyAria', {
                          label: t('pages.ordersPage.sheetTracking'),
                        })
                      "
                      @click.stop="copyFact('tracking', delivery.tracking)"
                    >
                      <Check
                        v-if="copiedFactId === 'tracking'"
                        class="size-3.5"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </section>

            <section v-if="showShipmentFacts">
              <InsetGroupLabel>{{
                t("pages.ordersPage.sheetShipment")
              }}</InsetGroupLabel>
              <InsetGroup>
                <InsetGroupItem v-if="supplyId" as="div">
                  <p class="text-xs text-muted-foreground">
                    {{ t("pages.ordersPage.sheetSupply") }}
                  </p>
                  <p class="truncate text-sm">{{ supplyId }}</p>
                  <template #trailing>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :aria-label="
                        t('pages.ordersPage.sheetCopyAria', {
                          label: t('pages.ordersPage.sheetSupply'),
                        })
                      "
                      @click.stop="copyFact('supply', supplyId)"
                    >
                      <Check
                        v-if="copiedFactId === 'supply'"
                        class="size-3.5"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                  </template>
                </InsetGroupItem>
                <InsetGroupItem v-if="boxId" as="div">
                  <p class="text-xs text-muted-foreground">
                    {{ t("pages.ordersPage.sheetBox") }}
                  </p>
                  <p class="truncate text-sm">{{ boxId }}</p>
                  <template #trailing>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :aria-label="
                        t('pages.ordersPage.sheetCopyAria', {
                          label: t('pages.ordersPage.sheetBox'),
                        })
                      "
                      @click.stop="copyFact('box', boxId)"
                    >
                      <Check
                        v-if="copiedFactId === 'box'"
                        class="size-3.5"
                      />
                      <Copy v-else class="size-3.5" />
                    </Button>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </section>

            <section v-if="items.length">
              <InsetGroupLabel>{{
                t("pages.ordersPage.sheetItems")
              }}</InsetGroupLabel>
              <InsetGroup>
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
                      <RouterLink
                        v-if="orderItemProductTo(item)"
                        :to="orderItemProductTo(item)"
                        class="truncate text-sm font-medium hover:underline"
                      >
                        {{ itemName(item) }}
                      </RouterLink>
                      <p v-else class="truncate text-sm">{{ itemName(item) }}</p>
                      <p
                        v-if="itemSku(item)"
                        class="mt-0.5 truncate text-xs text-muted-foreground"
                      >
                        {{ t("pages.ordersPage.sheetSku") }}
                        · {{ itemSku(item) }}
                      </p>
                      <p
                        v-if="
                          itemBarcode(item) && itemBarcode(item) !== itemSku(item)
                        "
                        class="mt-0.5 truncate text-xs text-muted-foreground"
                      >
                        {{ t("pages.ordersPage.sheetBarcode") }}
                        · {{ itemBarcode(item) }}
                      </p>
                      <p class="mt-0.5 text-xs text-muted-foreground">
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
            </section>
          </div>
        </template>
      </div>

      <SheetFooter
        v-if="showFooter"
        variant="bar"
        class="flex-wrap gap-2 sm:justify-start"
      >
        <Button
          v-if="showLabelButton"
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
        <Button
          v-if="showBoxLabelButton"
          type="button"
          variant="outline"
          size="sm"
          :disabled="Boolean(acting)"
          @click="downloadMarketplaceLabel('box')"
        >
          <Spinner v-if="acting === 'box'" size="sm" />
          <Barcode v-else class="size-3.5" />
          {{ t("pages.ordersPage.actionDownloadBoxLabel") }}
        </Button>
        <Button
          v-if="showSupplyQrButton"
          type="button"
          variant="outline"
          size="sm"
          :disabled="Boolean(acting)"
          @click="downloadMarketplaceLabel('supply')"
        >
          <Spinner v-if="acting === 'supply'" size="sm" />
          <Barcode v-else class="size-3.5" />
          {{ t("pages.ordersPage.actionDownloadSupplyLabel") }}
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
            <component :is="ORDER_ACTION_ICONS[action]" v-else class="size-3.5" />
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
          <FieldGroup v-if="showShipFields" class="gap-3">
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
          <FieldGroup v-else-if="showPackShipmentFields" class="gap-3">
            <SettingsFormField
              :label="t('pages.ordersPage.packSupplyLabel')"
              label-for="order-pack-supply"
            >
              <Select v-model="packSupplyChoice">
                <SelectTrigger id="order-pack-supply" class="w-full">
                  <SelectValue
                    :placeholder="t('pages.ordersPage.packSupplyNew')"
                  />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="new">{{
                    t("pages.ordersPage.packSupplyNew")
                  }}</SelectItem>
                  <SelectItem
                    v-for="shipment in openShipments"
                    :key="shipmentExternalId(shipment)"
                    :value="shipmentExternalId(shipment)"
                  >
                    {{ shipmentName(shipment) }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </SettingsFormField>
            <SettingsFormField
              v-if="packOfficeLabel"
              :label="t('pages.ordersPage.packOfficeLabel')"
            >
              <p class="text-sm">{{ packOfficeLabel }}</p>
            </SettingsFormField>
            <SettingsFormField
              :label="t('pages.ordersPage.packBoxLabel')"
              label-for="order-pack-box"
            >
              <Select v-model="packBoxChoice">
                <SelectTrigger id="order-pack-box" class="w-full">
                  <SelectValue
                    :placeholder="t('pages.ordersPage.packBoxNone')"
                  />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">{{
                    t("pages.ordersPage.packBoxNone")
                  }}</SelectItem>
                  <SelectItem value="new">{{
                    t("pages.ordersPage.packBoxNew")
                  }}</SelectItem>
                  <SelectItem
                    v-for="box in shipmentBoxes"
                    :key="boxExternalId(box)"
                    :value="boxExternalId(box)"
                  >
                    {{ boxExternalId(box) }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </SettingsFormField>
            <p
              v-if="packingOptionsLoading"
              class="text-xs text-muted-foreground"
            >
              {{ t("pages.ordersPage.packOptionsLoading") }}
            </p>
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
