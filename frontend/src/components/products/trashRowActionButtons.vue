<script>
// TrashRowActionButtons - Dropdown or context-menu content for trashed product row actions.
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ContextMenuContent } from "@/components/ui/context-menu";
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
import TrashRowActionMenuItems from "./TrashRowActionMenuItems.vue";
import { EllipsisVertical } from "lucide-vue-next";

export default {
  components: {
    Button,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuTrigger,
    ContextMenuContent,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    TrashRowActionMenuItems,
    EllipsisVertical,
  },
  props: {
    id: {
      type: Number,
      required: true,
    },
    name: {
      type: String,
      default: "",
    },
    sku: {
      type: String,
      default: "",
    },
    /** "dropdown" | "context" — context renders ContextMenuContent for a parent ContextMenu */
    mode: {
      type: String,
      default: "dropdown",
      validator: (v) => v === "dropdown" || v === "context",
    },
  },
  emits: ["restore", "permanent-delete"],
  data() {
    return {
      showRestoreDialog: false,
      showDeleteDialog: false,
    };
  },
  computed: {
    menuSurface() {
      return this.mode === "context" ? "context" : "dropdown";
    },
  },
  methods: {
    confirmRestore() {
      this.$emit("restore", this.id);
      this.showRestoreDialog = false;
    },
    confirmDelete() {
      this.$emit("permanent-delete", this.id);
      this.showDeleteDialog = false;
    },
  },
};
</script>

<template>
  <DropdownMenu v-if="mode === 'dropdown'">
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" class="w-full h-full rounded-none px-2">
        <EllipsisVertical class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-56">
      <TrashRowActionMenuItems
        surface="dropdown"
        :id="id"
        :name="name"
        :sku="sku"
        @restore="showRestoreDialog = true"
        @permanent-delete="showDeleteDialog = true"
      />
    </DropdownMenuContent>
  </DropdownMenu>

  <ContextMenuContent v-else class="w-56">
    <TrashRowActionMenuItems
      :surface="menuSurface"
      :id="id"
      :name="name"
      :sku="sku"
      @restore="showRestoreDialog = true"
      @permanent-delete="showDeleteDialog = true"
    />
  </ContextMenuContent>

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
        <AlertDialogAction @click="confirmRestore">
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
          @click="confirmDelete"
        >
          {{ $t("trashRowActions.deleteForever") }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
