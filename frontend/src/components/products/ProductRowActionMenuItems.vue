<script>
/**
 * Shared catalog product row actions for DropdownMenu and ContextMenu.
 * Structure mirrors ProductAdapterConnectionMenuItems: header → sections → danger.
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
import { ArrowRight, ArrowRightLeft, Copy, Trash2 } from "lucide-vue-next";

export default {
  name: "ProductRowActionMenuItems",
  components: {
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    ContextMenuGroup,
    ContextMenuItem,
    ContextMenuLabel,
    ContextMenuSeparator,
    ArrowRight,
    ArrowRightLeft,
    Copy,
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
    /** When false, Move is shown disabled (e.g. single-store instance). */
    canMove: {
      type: Boolean,
      default: true,
    },
  },
  emits: ["go-to", "clone", "move", "trash"],
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
  },
};
</script>

<template>
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

  <component :is="ui.Group">
    <component
      :is="ui.Label"
      class="text-xs font-medium text-muted-foreground"
    >
      {{ $t("productRowActions.menuSectionProduct") }}
    </component>
    <component :is="ui.Item" @click.stop="$emit('go-to')">
      <ArrowRight />
      {{ $t("productRowActions.goTo") }}
    </component>
    <component :is="ui.Item" @click.stop="$emit('clone')">
      <Copy />
      {{ $t("productRowActions.cloneToStore") }}
    </component>
    <component
      :is="ui.Item"
      :disabled="!canMove"
      :title="!canMove ? $t('productRowActions.moveToStoreDisabledSingleStore') : undefined"
      @click.stop="canMove && $emit('move')"
    >
      <ArrowRightLeft />
      {{ $t("productRowActions.moveToStore") }}
    </component>
  </component>

  <component :is="ui.Separator" />

  <component :is="ui.Group">
    <component
      :is="ui.Label"
      class="text-xs font-medium text-muted-foreground"
    >
      {{ $t("productRowActions.menuSectionDanger") }}
    </component>
    <component
      :is="ui.Item"
      variant="destructive"
      @click.stop="$emit('trash')"
    >
      <Trash2 />
      {{ $t("productRowActions.moveToTrash") }}
    </component>
  </component>
</template>
