<script>
/**
 * Shared product↔adapter connection actions menu (dropdown trigger).
 * Used by the product Adapters tab and the products-table Adapters column.
 * For cursor-positioned right-click, wrap the row in ContextMenu and reuse
 * ProductAdapterConnectionMenuItems with surface="context".
 */
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import ProductAdapterConnectionMenuItems from "@/components/products/ProductAdapterConnectionMenuItems.vue";

export default {
  name: "ProductAdapterConnectionMenu",
  components: {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuTrigger,
    ProductAdapterConnectionMenuItems,
  },
  props: {
    /** available-connections row (or compatible shape). */
    connection: { type: Object, required: true },
    selectedMappingId: { type: [Number, String], default: null },
    marketplaceUrl: { type: String, default: null },
    configureMappingTo: { type: Object, default: null },
    linkOnProductTo: { type: Object, default: null },
    categoryName: { type: String, default: null },
    align: { type: String, default: "end" },
    loading: { type: Boolean, default: false },
    exportBusy: { type: Boolean, default: false },
    importBusy: { type: Boolean, default: false },
    verifyBusy: { type: Boolean, default: false },
    unlinkBusy: { type: Boolean, default: false },
    /** Product-tab extras */
    showRelink: { type: Boolean, default: false },
    showRecreate: { type: Boolean, default: false },
    showLinkManually: { type: Boolean, default: false },
    showDangerSection: { type: Boolean, default: false },
    showLinkOnProduct: { type: Boolean, default: false },
  },
  emits: [
    "update:open",
    "export",
    "update:exportMode",
    "import",
    "verify",
    "unlink",
    "update:mapping",
    "relink",
    "link-manually",
    "recreate",
  ],
  methods: {
    onOpen(open) {
      this.$emit("update:open", open);
    },
  },
};
</script>

<template>
  <DropdownMenu @update:open="onOpen">
    <DropdownMenuTrigger as-child>
      <slot name="trigger" />
    </DropdownMenuTrigger>
    <DropdownMenuContent :align="align" class="w-56" @click.stop>
      <ProductAdapterConnectionMenuItems
        surface="dropdown"
        :connection="connection"
        :selected-mapping-id="selectedMappingId"
        :marketplace-url="marketplaceUrl"
        :configure-mapping-to="configureMappingTo"
        :link-on-product-to="linkOnProductTo"
        :category-name="categoryName"
        :loading="loading"
        :export-busy="exportBusy"
        :import-busy="importBusy"
        :verify-busy="verifyBusy"
        :unlink-busy="unlinkBusy"
        :show-relink="showRelink"
        :show-recreate="showRecreate"
        :show-link-manually="showLinkManually"
        :show-danger-section="showDangerSection"
        :show-link-on-product="showLinkOnProduct"
        @export="$emit('export')"
        @update:export-mode="$emit('update:exportMode', $event)"
        @import="$emit('import')"
        @verify="$emit('verify')"
        @unlink="$emit('unlink')"
        @update:mapping="$emit('update:mapping', $event)"
        @relink="$emit('relink')"
        @link-manually="$emit('link-manually')"
        @recreate="$emit('recreate')"
      />
    </DropdownMenuContent>
  </DropdownMenu>
</template>
