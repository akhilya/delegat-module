<script setup>
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
import { confirmState, resolveConfirm } from "@/composables/useConfirm";

/** Set before AlertDialogAction closes the root — update:open(false) must see it. */
let pendingAccept = false;

function onOpenChange(open) {
  if (open) {
    confirmState.open = true;
    pendingAccept = false;
    return;
  }
  const accepted = pendingAccept;
  pendingAccept = false;
  resolveConfirm(accepted);
}

function markAccept() {
  pendingAccept = true;
}

function preventOutside(event) {
  event.preventDefault();
}
</script>

<template>
  <AlertDialog :open="confirmState.open" @update:open="onOpenChange">
    <AlertDialogContent
      @pointer-down-outside="preventOutside"
      @focus-outside="preventOutside"
      @interact-outside="preventOutside"
    >
      <AlertDialogHeader>
        <AlertDialogTitle>{{ confirmState.title }}</AlertDialogTitle>
        <AlertDialogDescription v-if="confirmState.description">
          {{ confirmState.description }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>
          {{ confirmState.cancelLabel }}
        </AlertDialogCancel>
        <AlertDialogAction
          :variant="confirmState.destructive ? 'destructive' : 'default'"
          @pointerdown="markAccept"
          @click="markAccept"
        >
          {{ confirmState.confirmLabel }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
