<script>
// TrashRowActionButtons - Dropdown menu with confirm dialogs for trashed product row actions.
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
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
import { EllipsisVertical, Undo2, Trash2 } from "lucide-vue-next";

export default {
  components: {
    Button,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    EllipsisVertical,
    Undo2,
    Trash2,
  },
  props: {
    id: {
      type: Number,
      required: true,
    },
  },
  emits: ["restore", "permanent-delete"],
  data() {
    return {
      showRestoreDialog: false,
      showDeleteDialog: false,
    };
  },
};
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" class="w-full h-full rounded-none px-2">
        <EllipsisVertical class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end">
      <DropdownMenuItem @click.stop="showRestoreDialog = true">
        <Undo2 />
        {{ $t("trashRowActions.restore") }}
      </DropdownMenuItem>
      <DropdownMenuItem
        variant="destructive"
        @click.stop="showDeleteDialog = true"
      >
        <Trash2 />
        {{ $t("trashRowActions.deleteForever") }}
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>

  <AlertDialog v-model:open="showRestoreDialog">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ $t("trashRowActions.confirmRestoreTitle") }}</AlertDialogTitle>
        <AlertDialogDescription>
          {{ $t("trashRowActions.confirmRestoreDescription") }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
        <AlertDialogAction @click="$emit('restore', id)">
          {{ $t("trashRowActions.restore") }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>

  <AlertDialog v-model:open="showDeleteDialog">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ $t("trashRowActions.confirmDeleteTitle") }}</AlertDialogTitle>
        <AlertDialogDescription>
          {{ $t("trashRowActions.confirmDeleteDescription") }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
        <AlertDialogAction
          variant="destructive"
          @click="$emit('permanent-delete', id)"
        >
          {{ $t("trashRowActions.deleteForever") }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
