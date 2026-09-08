<script setup>
/**
 * Confirm a mailbox / packing-overview order job (status or label).
 */
import { computed } from "vue";
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
import { orderJobConfirmMeta } from "@/lib/ordersUi";

const props = defineProps({
  open: { type: Boolean, default: false },
  job: { type: Object, default: null },
  adapterKey: { type: String, default: "" },
});

const emit = defineEmits(["update:open", "confirm"]);
const { t } = useI18n();

const meta = computed(() =>
  orderJobConfirmMeta(props.job, t, { adapterKey: props.adapterKey }),
);
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
      <AlertDialogFooter>
        <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
        <AlertDialogAction
          :variant="meta.destructive ? 'destructive' : 'default'"
          @click="emit('confirm')"
        >
          {{ meta.label }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
