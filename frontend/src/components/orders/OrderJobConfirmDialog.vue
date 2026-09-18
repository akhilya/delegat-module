<script setup>
/**
 * Confirm a mailbox / packing-overview order job (status or label).
 */
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
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
import OrderActionFieldsForm from "@/components/orders/OrderActionFieldsForm.vue";
import { orderJobConfirmMeta } from "@/lib/ordersUi";
import {
  adapterOrderActionFields,
  applyOrderActionFieldValues,
  defaultOrderActionFieldValues,
  orderActionFieldsValid,
} from "@/lib/orderActionFields";

const props = defineProps({
  open: { type: Boolean, default: false },
  job: { type: Object, default: null },
  adapterKey: { type: String, default: "" },
  adapter: { type: Object, default: null },
});

const emit = defineEmits(["update:open", "confirm"]);
const { t } = useI18n();
const fieldValues = ref({});

const meta = computed(() =>
  orderJobConfirmMeta(props.job, t, { adapterKey: props.adapterKey }),
);

const actionFields = computed(() => {
  if (props.job?.kind !== "status") return [];
  return adapterOrderActionFields(props.adapter, props.job?.action);
});

const confirmReady = computed(() =>
  orderActionFieldsValid(actionFields.value, fieldValues.value),
);

watch(
  () => [props.open, props.job?.action, props.job?.kind, props.adapter],
  () => {
    fieldValues.value = defaultOrderActionFieldValues(actionFields.value);
  },
);

function onConfirm() {
  if (!confirmReady.value) return;
  emit(
    "confirm",
    applyOrderActionFieldValues(actionFields.value, fieldValues.value),
  );
}
</script>

<template>
  <AlertDialog :open="open" @update:open="emit('update:open', $event)">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ meta.title }}</AlertDialogTitle>
        <AlertDialogDescription>
          {{ meta.description }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <OrderActionFieldsForm
        v-if="actionFields.length"
        v-model="fieldValues"
        :fields="actionFields"
        id-prefix="order-job-action"
      />
      <AlertDialogFooter>
        <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
        <AlertDialogAction
          :variant="meta.destructive ? 'destructive' : 'default'"
          :disabled="!confirmReady"
          @click="onConfirm"
        >
          {{ meta.label }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
