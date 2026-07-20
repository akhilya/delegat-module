<script>
// ProductRowActionButtons - Dropdown menu with actions for a single product table row.
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
import { PAGE_PRODUCT_EDIT } from "@/router";
import { EllipsisVertical, ArrowRight, ArrowRightLeft, Trash2 } from "lucide-vue-next";

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
    ArrowRight,
    ArrowRightLeft,
    Trash2,
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
  },
  emits: ["transfer", "delete"],
  data() {
    return {
      showDeleteDialog: false,
    };
  },
  methods: {
    navigate() {
      this.$router.push({
        name: PAGE_PRODUCT_EDIT,
        params: { id: this.id },
      });
    },
    openTransfer() {
      this.$emit("transfer", { id: this.id, name: this.name, sku: this.sku });
    },
    confirmDelete() {
      this.$emit("delete", this.id);
      this.showDeleteDialog = false;
    },
  },
};
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" class="w-full h-full rounded-none px-2 sm:px-3">
        <EllipsisVertical class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end">
      <DropdownMenuItem @click.stop="navigate">
        <ArrowRight class="size-4 mr-2" />
        {{ $t("productRowActions.goTo") }}
      </DropdownMenuItem>
      <DropdownMenuItem @click.stop="openTransfer">
        <ArrowRightLeft class="size-4 mr-2" />
        {{ $t("productRowActions.moveToStore") }}
      </DropdownMenuItem>
      <DropdownMenuItem
        class="text-destructive focus:text-destructive"
        @click.stop="showDeleteDialog = true"
      >
        <Trash2 class="size-4 mr-2" />
        {{ $t("productRowActions.moveToTrash") }}
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>

  <AlertDialog v-model:open="showDeleteDialog">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ $t("productRowActions.confirmTrashTitle") }}</AlertDialogTitle>
        <AlertDialogDescription>
          {{ $t("productRowActions.confirmTrashDescription") }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
        <AlertDialogAction
          class="bg-destructive text-white hover:bg-destructive/90"
          @click="confirmDelete"
        >
          {{ $t("productRowActions.toTrash") }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>