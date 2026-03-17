<script>
// TrashRowActionButtons - Dropdown menu with actions for a trashed product row.
import Button from "../ui/button/Button.vue";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { EllipsisVertical, Undo2, Trash2 } from "lucide-vue-next";

export default {
  components: {
    Button,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
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
      <DropdownMenuItem @click.stop="$emit('restore', id)">
        <Undo2 class="size-4 mr-2" />
        Восстановить
      </DropdownMenuItem>
      <DropdownMenuItem class="text-destructive focus:text-destructive" @click.stop="$emit('permanent-delete', id)">
        <Trash2 class="size-4 mr-2" />
        Удалить навсегда
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
