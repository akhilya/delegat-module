<script>
/**
 * Shared trash product row actions for DropdownMenu and ContextMenu.
 */
import {
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";
import {
  ContextMenuGroup,
  ContextMenuItem,
  ContextMenuLabel,
  ContextMenuSeparator,
} from "@/components/ui/context-menu";
import { Undo2, Trash2 } from "lucide-vue-next";

export default {
  name: "TrashRowActionMenuItems",
  components: {
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    ContextMenuGroup,
    ContextMenuItem,
    ContextMenuLabel,
    ContextMenuSeparator,
    Undo2,
    Trash2,
  },
  props: {
    /** "dropdown" | "context" */
    surface: {
      type: String,
      default: "dropdown",
      validator: (v) => v === "dropdown" || v === "context",
    },
    name: {
      type: String,
      default: "",
    },
    sku: {
      type: String,
      default: "",
    },
    id: {
      type: Number,
      default: null,
    },
  },
  emits: ["restore", "permanent-delete"],
  computed: {
    ui() {
      const ctx = this.surface === "context";
      return {
        Group: ctx ? "ContextMenuGroup" : "DropdownMenuGroup",
        Item: ctx ? "ContextMenuItem" : "DropdownMenuItem",
        Label: ctx ? "ContextMenuLabel" : "DropdownMenuLabel",
        Separator: ctx ? "ContextMenuSeparator" : "DropdownMenuSeparator",
      };
    },
    headerTitle() {
      return (
        (this.name || "").trim() ||
        (this.sku || "").trim() ||
        (this.id != null ? `#${this.id}` : "")
      );
    },
    headerSubtitle() {
      const sku = (this.sku || "").trim();
      if (!sku) return "";
      if (sku === this.headerTitle) return "";
      return sku;
    },
    showHeader() {
      return Boolean(this.headerTitle);
    },
  },
};
</script>

<template>
  <template v-if="showHeader">
    <component :is="ui.Label" class="min-w-0 overflow-hidden font-normal">
      <div class="flex min-w-0 flex-col gap-0.5">
        <span class="block truncate text-sm font-medium text-foreground">
          {{ headerTitle }}
        </span>
        <span
          v-if="headerSubtitle"
          class="block truncate text-xs font-normal text-muted-foreground"
        >
          {{ headerSubtitle }}
        </span>
      </div>
    </component>
    <component :is="ui.Separator" />
  </template>

  <component :is="ui.Group">
    <component
      :is="ui.Label"
      class="text-xs font-medium text-muted-foreground"
    >
      {{ $t("trashRowActions.menuSectionRestore") }}
    </component>
    <component :is="ui.Item" @click.stop="$emit('restore')">
      <Undo2 />
      {{ $t("trashRowActions.restore") }}
    </component>
  </component>

  <component :is="ui.Separator" />

  <component :is="ui.Group">
    <component
      :is="ui.Label"
      class="text-xs font-medium text-muted-foreground"
    >
      {{ $t("trashRowActions.menuSectionDanger") }}
    </component>
    <component
      :is="ui.Item"
      variant="destructive"
      @click.stop="$emit('permanent-delete')"
    >
      <Trash2 />
      {{ $t("trashRowActions.deleteForever") }}
    </component>
  </component>
</template>
