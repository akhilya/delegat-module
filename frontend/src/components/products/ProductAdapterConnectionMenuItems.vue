<script>
/**
 * Shared adapter connection menu body for DropdownMenu and ContextMenu surfaces.
 */
import { Spinner } from "@/components/ui/spinner";
import {
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubTrigger,
  DropdownMenuSubContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
} from "@/components/ui/dropdown-menu";
import {
  ContextMenuGroup,
  ContextMenuItem,
  ContextMenuLabel,
  ContextMenuSeparator,
  ContextMenuSub,
  ContextMenuSubTrigger,
  ContextMenuSubContent,
  ContextMenuRadioGroup,
  ContextMenuRadioItem,
} from "@/components/ui/context-menu";
import {
  Upload,
  Download,
  ExternalLink,
  Unlink,
  Link2,
  SearchCheck,
  Package,
  DollarSign,
  Boxes,
  Layers,
  RefreshCw,
} from "lucide-vue-next";
import {
  adapterConnectionMenuMode,
  adapterConnectionStatusLabel,
  exportModeLabel,
  hasMappingIssue,
  normalizeExportMode,
} from "@/lib/adapterConnectionStatus";

export default {
  name: "ProductAdapterConnectionMenuItems",
  components: {
    Spinner,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuSub,
    DropdownMenuSubTrigger,
    DropdownMenuSubContent,
    DropdownMenuRadioGroup,
    DropdownMenuRadioItem,
    ContextMenuGroup,
    ContextMenuItem,
    ContextMenuLabel,
    ContextMenuSeparator,
    ContextMenuSub,
    ContextMenuSubTrigger,
    ContextMenuSubContent,
    ContextMenuRadioGroup,
    ContextMenuRadioItem,
    Upload,
    Download,
    ExternalLink,
    Unlink,
    Link2,
    SearchCheck,
    Package,
    DollarSign,
    Boxes,
    Layers,
    RefreshCw,
  },
  props: {
    /** "dropdown" | "context" */
    surface: {
      type: String,
      default: "dropdown",
      validator: (v) => v === "dropdown" || v === "context",
    },
    connection: { type: Object, required: true },
    selectedMappingId: { type: [Number, String], default: null },
    marketplaceUrl: { type: String, default: null },
    configureMappingTo: { type: Object, default: null },
    linkOnProductTo: { type: Object, default: null },
    categoryName: { type: String, default: null },
    loading: { type: Boolean, default: false },
    exportBusy: { type: Boolean, default: false },
    importBusy: { type: Boolean, default: false },
    verifyBusy: { type: Boolean, default: false },
    unlinkBusy: { type: Boolean, default: false },
    showRelink: { type: Boolean, default: false },
    showRecreate: { type: Boolean, default: false },
    showLinkManually: { type: Boolean, default: false },
    showDangerSection: { type: Boolean, default: false },
    showLinkOnProduct: { type: Boolean, default: false },
  },
  emits: [
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
  computed: {
    ui() {
      const ctx = this.surface === "context";
      return {
        Group: ctx ? "ContextMenuGroup" : "DropdownMenuGroup",
        Item: ctx ? "ContextMenuItem" : "DropdownMenuItem",
        Label: ctx ? "ContextMenuLabel" : "DropdownMenuLabel",
        Separator: ctx ? "ContextMenuSeparator" : "DropdownMenuSeparator",
        Sub: ctx ? "ContextMenuSub" : "DropdownMenuSub",
        SubTrigger: ctx ? "ContextMenuSubTrigger" : "DropdownMenuSubTrigger",
        SubContent: ctx ? "ContextMenuSubContent" : "DropdownMenuSubContent",
        RadioGroup: ctx ? "ContextMenuRadioGroup" : "DropdownMenuRadioGroup",
        RadioItem: ctx ? "ContextMenuRadioItem" : "DropdownMenuRadioItem",
      };
    },
    mode() {
      return adapterConnectionMenuMode(this.connection);
    },
    title() {
      return (
        this.connection.adapterName ||
        this.connection.connectionName ||
        this.connection.adapterKey ||
        ""
      );
    },
    subtitle() {
      const status = adapterConnectionStatusLabel(this.connection, {
        categoryName: this.categoryName,
      });
      const ext = this.connection.externalProductId;
      if (ext) return `${status} · ${ext}`;
      if (this.connection.connectionName && this.mode !== "missingMapping") {
        return this.connection.connectionName;
      }
      return status;
    },
    subtitleDestructive() {
      return hasMappingIssue(this.connection);
    },
    exportMode() {
      return normalizeExportMode(this.connection.preferredExportMode);
    },
    exportLabel() {
      if (this.mode === "ready") {
        return this.$t("productMarketplaceIntegrations.export");
      }
      if (this.connection.isLinked) {
        return exportModeLabel(this.exportMode);
      }
      return this.$t("productMarketplaceIntegrations.menuExportFull");
    },
    showMappingPicker() {
      const maps = this.connection.availableMappings || [];
      if (maps.length > 1) return true;
      return Boolean(this.connection.mappingMissing && maps.length > 0);
    },
    exportDisabled() {
      if (this.exportBusy) return true;
      if (this.mode === "missingMapping") return true;
      if (this.connection.mappingMissing) return true;
      if (!this.connection.canExport && this.mode !== "ready") return true;
      return false;
    },
  },
  methods: {
    mappingLabel(m) {
      return (
        m.name ||
        m.externalCategoryName ||
        this.$t("productMarketplaceIntegrations.mappingFallback", { id: m.id })
      );
    },
  },
};
</script>

<template>
  <component :is="ui.Label" class="font-normal">
    <div class="flex flex-col gap-0.5">
      <span class="truncate text-sm font-medium text-foreground">
        {{ title }}
      </span>
      <span
        class="truncate text-xs font-normal"
        :class="
          subtitleDestructive ? 'text-destructive' : 'text-muted-foreground'
        "
      >
        {{ subtitle }}
      </span>
    </div>
  </component>
  <component :is="ui.Separator" />

  <div
    v-if="loading"
    class="flex items-center gap-2 px-2 py-2 text-xs text-muted-foreground"
  >
    <Spinner size="sm" />
    {{ $t("common.loading") }}
  </div>

  <template v-else-if="mode === 'missingMapping'">
    <component :is="ui.Group">
      <component :is="ui.Item" v-if="configureMappingTo" as-child>
        <RouterLink :to="configureMappingTo">
          <Layers />
          {{ $t("products.adaptersColumn.configureMapping") }}
        </RouterLink>
      </component>
      <component :is="ui.Sub" v-if="showMappingPicker">
        <component :is="ui.SubTrigger">
          <Layers />
          {{ $t("productMarketplaceIntegrations.menuMapping") }}
        </component>
        <component :is="ui.SubContent" class="w-52">
          <component
            :is="ui.RadioGroup"
            :model-value="String(selectedMappingId ?? '')"
            @update:model-value="$emit('update:mapping', Number($event))"
          >
            <component
              :is="ui.RadioItem"
              v-for="m in connection.availableMappings"
              :key="m.id"
              :value="String(m.id)"
              class="truncate"
            >
              {{ mappingLabel(m) }}
            </component>
          </component>
        </component>
      </component>
      <component :is="ui.Item" disabled class="opacity-60">
        <Upload />
        {{ $t("productMarketplaceIntegrations.menuExportFull") }}
      </component>
      <component
        :is="ui.Item"
        v-if="connection.isLinked"
        variant="destructive"
        :disabled="unlinkBusy"
        @click="$emit('unlink')"
      >
        <Spinner v-if="unlinkBusy" />
        <Unlink v-else />
        {{ $t("productMarketplaceIntegrations.menuUnlink") }}
      </component>
    </component>
  </template>

  <template v-else-if="mode === 'ready'">
    <component :is="ui.Group">
      <component
        :is="ui.Item"
        :disabled="exportBusy"
        @click="$emit('export')"
      >
        <Upload />
        {{ $t("productMarketplaceIntegrations.export") }}
      </component>
      <component
        :is="ui.Item"
        v-if="showLinkManually"
        @click="$emit('link-manually')"
      >
        <Link2 />
        {{ $t("productMarketplaceIntegrations.linkManually") }}
      </component>
      <component
        :is="ui.Item"
        v-else-if="showLinkOnProduct && linkOnProductTo"
        as-child
      >
        <RouterLink :to="linkOnProductTo">
          <Link2 />
          {{ $t("products.adaptersColumn.linkOnProduct") }}
        </RouterLink>
      </component>
      <component :is="ui.Sub" v-if="showMappingPicker">
        <component :is="ui.SubTrigger">
          <Layers />
          {{ $t("productMarketplaceIntegrations.menuMapping") }}
        </component>
        <component :is="ui.SubContent" class="w-52">
          <component
            :is="ui.RadioGroup"
            :model-value="String(selectedMappingId ?? '')"
            @update:model-value="$emit('update:mapping', Number($event))"
          >
            <component
              :is="ui.RadioItem"
              v-for="m in connection.availableMappings"
              :key="m.id"
              :value="String(m.id)"
              class="truncate"
            >
              {{ mappingLabel(m) }}
            </component>
          </component>
        </component>
      </component>
    </component>
  </template>

  <template v-else>
    <component :is="ui.Group">
      <component
        :is="ui.Label"
        class="text-xs font-medium text-muted-foreground"
      >
        {{ $t("productMarketplaceIntegrations.menuSectionSync") }}
      </component>
      <div class="flex items-stretch">
        <component
          :is="ui.Item"
          class="flex-1"
          :disabled="exportDisabled"
          @click="$emit('export')"
        >
          <Upload />
          {{ exportLabel }}
        </component>
        <component :is="ui.Sub">
          <component
            :is="ui.SubTrigger"
            class="w-8 shrink-0 justify-center px-0 [&>svg:last-child]:ml-0"
            :disabled="exportDisabled"
            :aria-label="$t('productMarketplaceIntegrations.menuExportMode')"
          />
          <component :is="ui.SubContent" class="w-52">
            <component
              :is="ui.RadioGroup"
              :model-value="exportMode"
              @update:model-value="$emit('update:exportMode', $event)"
            >
              <component :is="ui.RadioItem" value="full">
                <Package />
                {{ $t("productMarketplaceIntegrations.menuExportFull") }}
              </component>
              <component
                :is="ui.RadioItem"
                v-if="connection.canExportPrices"
                value="price"
              >
                <DollarSign />
                {{ $t("productMarketplaceIntegrations.menuExportPrice") }}
              </component>
              <component
                :is="ui.RadioItem"
                v-if="connection.canExportStocks"
                value="stock"
              >
                <Boxes />
                {{ $t("productMarketplaceIntegrations.menuExportStock") }}
              </component>
            </component>
          </component>
        </component>
      </div>
      <component
        :is="ui.Item"
        v-if="connection.canImport"
        :disabled="importBusy"
        @click="$emit('import')"
      >
        <Download />
        {{ $t("productMarketplaceIntegrations.menuImport") }}
      </component>
    </component>

    <component :is="ui.Separator" />

    <component :is="ui.Group">
      <component
        :is="ui.Label"
        class="text-xs font-medium text-muted-foreground"
      >
        {{ $t("productMarketplaceIntegrations.menuSectionLink") }}
      </component>
      <component
        :is="ui.Item"
        :disabled="verifyBusy || !connection.canImport"
        @click="$emit('verify')"
      >
        <Spinner v-if="verifyBusy" />
        <SearchCheck v-else />
        {{ $t("productMarketplaceIntegrations.menuVerify") }}
      </component>
      <component :is="ui.Item" v-if="showRelink" @click="$emit('relink')">
        <Link2 />
        {{ $t("productMarketplaceIntegrations.menuRelink") }}
      </component>
      <component :is="ui.Sub" v-if="showMappingPicker">
        <component :is="ui.SubTrigger">
          <Layers />
          {{ $t("productMarketplaceIntegrations.menuMapping") }}
        </component>
        <component :is="ui.SubContent" class="w-52">
          <component
            :is="ui.RadioGroup"
            :model-value="String(selectedMappingId ?? '')"
            @update:model-value="$emit('update:mapping', Number($event))"
          >
            <component
              :is="ui.RadioItem"
              v-for="m in connection.availableMappings"
              :key="m.id"
              :value="String(m.id)"
              class="truncate"
            >
              {{ mappingLabel(m) }}
            </component>
          </component>
        </component>
      </component>
      <component :is="ui.Item" v-if="marketplaceUrl" as-child>
        <a
          :href="marketplaceUrl"
          target="_blank"
          rel="noopener noreferrer"
        >
          <ExternalLink />
          {{ $t("productMarketplaceIntegrations.openOnAdapter") }}
        </a>
      </component>
    </component>

    <template v-if="showDangerSection">
      <component :is="ui.Separator" />
      <component :is="ui.Group">
        <component
          :is="ui.Label"
          class="text-xs font-medium text-muted-foreground"
        >
          {{ $t("productMarketplaceIntegrations.menuSectionDanger") }}
        </component>
        <component
          :is="ui.Item"
          variant="destructive"
          :disabled="unlinkBusy"
          @click="$emit('unlink')"
        >
          <Spinner v-if="unlinkBusy" />
          <Unlink v-else />
          {{ $t("productMarketplaceIntegrations.menuUnlink") }}
        </component>
        <component
          :is="ui.Item"
          v-if="showRecreate && connection.canArchive && connection.canExport"
          @click="$emit('recreate')"
        >
          <RefreshCw />
          {{ $t("productMarketplaceIntegrations.menuRecreate") }}
        </component>
      </component>
    </template>
    <component
      :is="ui.Item"
      v-else
      variant="destructive"
      :disabled="unlinkBusy"
      @click="$emit('unlink')"
    >
      <Spinner v-if="unlinkBusy" />
      <Unlink v-else />
      {{ $t("productMarketplaceIntegrations.menuUnlink") }}
    </component>
  </template>
</template>
