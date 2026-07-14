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
</script>

<template>
  <AlertDialog
    :open="confirmState.open"
    @update:open="(v) => { confirmState.open = v; }"
  >
    <AlertDialogContent @escape-key-down="resolveConfirm(false)">
      <AlertDialogHeader>
        <AlertDialogTitle>{{ confirmState.title }}</AlertDialogTitle>
        <AlertDialogDescription v-if="confirmState.description">
          {{ confirmState.description }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel @click="resolveConfirm(false)">
          {{ confirmState.cancelLabel }}
        </AlertDialogCancel>
        <AlertDialogAction
          :class="confirmState.destructive ? 'bg-destructive text-white hover:bg-destructive/90' : ''"
          @click="resolveConfirm(true)"
        >
          {{ confirmState.confirmLabel }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>