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

/** Capture activation before the close handler, for both pointer and keyboard clicks. */
let pendingResult = false;

function onOpenChange(open) {
  if (open) {
    confirmState.open = true;
    pendingResult = false;
    return;
  }
  const result = pendingResult;
  pendingResult = false;
  resolveConfirm(result);
}

function markResult(result) {
  pendingResult = result;
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
      <AlertDialogFooter :layout="confirmState.saveLabel ? 'stack' : 'split'">
        <template v-if="confirmState.saveLabel">
          <AlertDialogAction @click.capture="markResult('save')">
            {{ confirmState.saveLabel }}
          </AlertDialogAction>
          <AlertDialogAction
            :variant="confirmState.destructive ? 'destructive' : 'outline'"
            @click.capture="markResult(true)"
          >
            {{ confirmState.confirmLabel }}
          </AlertDialogAction>
          <AlertDialogCancel>
            {{ confirmState.cancelLabel }}
          </AlertDialogCancel>
        </template>
        <template v-else>
          <AlertDialogCancel>
            {{ confirmState.cancelLabel }}
          </AlertDialogCancel>
          <AlertDialogAction
            :variant="confirmState.destructive ? 'destructive' : 'default'"
            @click.capture="markResult(true)"
          >
            {{ confirmState.confirmLabel }}
          </AlertDialogAction>
        </template>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
