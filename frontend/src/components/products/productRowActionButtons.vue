<script>
// ProductRowActionButtons - Dropdown or context-menu content with actions for a product table row.
import { computed, onMounted } from "vue";
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
import ProductRowActionMenuItems from "./ProductRowActionMenuItems.vue";
import { PAGE_PRODUCT_EDIT } from "@/router";
import { EllipsisVertical } from "lucide-vue-next";
import { useSelectedStore } from "@/composables/useSelectedStore";

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
    ProductRowActionMenuItems,
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
  emits: ["transfer", "delete"],
  setup() {
    const { memberships, loadMemberships } = useSelectedStore();
    onMounted(() => {
      loadMemberships();
    });
    // Move needs another store the user can access (single-store instances → disabled).
    const canMove = computed(() => memberships.value.length > 1);
    return { canMove };
  },
  data() {
    return {
      showDeleteDialog: false,
    };
  },
  computed: {
    menuSurface() {
      return this.mode === "context" ? "context" : "dropdown";
    },
  },
  methods: {
    navigate() {
      this.$router.push({
        name: PAGE_PRODUCT_EDIT,
        params: { id: this.id },
      });
    },
    openTransfer(mode) {
      if (mode === "move" && !this.canMove) return;
      this.$emit("transfer", {
        id: this.id,
        name: this.name,
        sku: this.sku,
        mode,
      });
    },
    confirmDelete() {
      this.$emit("delete", this.id);
      this.showDeleteDialog = false;
    },
  },
};
</script>

<template>
  <DropdownMenu v-if="mode === 'dropdown'">
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" class="w-full h-full rounded-none px-2 sm:px-3">
        <EllipsisVertical class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-56">
      <ProductRowActionMenuItems
        surface="dropdown"
        :id="id"
        :name="name"
        :sku="sku"
        :can-move="canMove"
        @go-to="navigate"
        @clone="openTransfer('clone')"
        @move="openTransfer('move')"
        @trash="showDeleteDialog = true"
      />
    </DropdownMenuContent>
  </DropdownMenu>

  <ContextMenuContent v-else class="w-56">
    <ProductRowActionMenuItems
      :surface="menuSurface"
      :id="id"
      :name="name"
      :sku="sku"
      :can-move="canMove"
      @go-to="navigate"
      @clone="openTransfer('clone')"
      @move="openTransfer('move')"
      @trash="showDeleteDialog = true"
    />
  </ContextMenuContent>

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
          variant="destructive"
          @click="confirmDelete"
        >
          {{ $t("productRowActions.moveToTrash") }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
