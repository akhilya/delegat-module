<script>
// TrashRowActionButtons - Dropdown menu with confirm dialogs for trashed product row actions.
import Button from "../ui/button/Button.vue";
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
        <Undo2 class="size-4 mr-2" />
        Восстановить
      </DropdownMenuItem>
      <DropdownMenuItem class="text-destructive focus:text-destructive" @click.stop="showDeleteDialog = true">
        <Trash2 class="size-4 mr-2" />
        Удалить навсегда
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>

  <AlertDialog v-model:open="showRestoreDialog">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>Восстановить товар?</AlertDialogTitle>
        <AlertDialogDescription>
          Товар будет восстановлен из корзины и снова появится в списке товаров.
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>Отмена</AlertDialogCancel>
        <AlertDialogAction @click="$emit('restore', id)">
          Восстановить
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>

  <AlertDialog v-model:open="showDeleteDialog">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>Удалить навсегда?</AlertDialogTitle>
        <AlertDialogDescription>
          Все файлы, фото, заметки и данные маркетплейсов будут удалены. Это действие нельзя отменить.
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>Отмена</AlertDialogCancel>
        <AlertDialogAction
          class="bg-destructive text-white hover:bg-destructive/90"
          @click="$emit('permanent-delete', id)"
        >
          Удалить навсегда
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
