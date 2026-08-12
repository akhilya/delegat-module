<script>
/**
 * Products table "Adapters" cell: ButtonGroup of store connections.
 * Menu/actions come from ProductAdapterConnectionMenu (same as product Adapters tab).
 */
import { Button } from "@/components/ui/button";
import { ButtonGroup } from "@/components/ui/button-group";
import { Spinner } from "@/components/ui/spinner";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import ProductAdapterConnectionMenu from "@/components/products/ProductAdapterConnectionMenu.vue";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { fillProductEditUrl } from "@/lib/productEditUrl";
import { t } from "@/i18n";
import {
  adapterConnectionButtonClass,
  adapterConnectionStatusLabel,
  hasMappingIssue,
  normalizeExportMode,
} from "@/lib/adapterConnectionStatus";

export default {
  name: "ProductAdaptersCell",
  emits: ["changed"],
  components: {
    Button,
    ButtonGroup,
    Spinner,
    AdapterIcon,
    ProductAdapterConnectionMenu,
  },
  props: {
    product: { type: Object, required: true },
    connections: { type: Array, default: () => [] },
    disabled: { type: Boolean, default: false },
  },
  data() {
    return {
      busyKey: null,
      detailsLoading: false,
      detailsLoaded: false,
      detailsError: false,
      richByConnectionId: null,
      categoryId: null,
      categoryName: null,
      mappingSelections: {},
    };
  },
  computed: {
    activeConnections() {
      return (this.connections || []).filter((c) => {
        const active = c.isActive ?? c.IsActive;
        return active !== false;
      });
    },
    linksByConnectionId() {
      const map = {};
      for (const link of this.product?.adapterLinks || []) {
        const id = link.connectionId ?? link.ConnectionId;
        if (id != null) map[id] = link;
      }
      return map;
    },
  },
  methods: {
    ...mapActions([
      "getAvailableConnectionsForProduct",
      "exportProduct",
      "exportFromLink",
      "exportPrices",
      "exportStocks",
      "importFromLink",
      "unlinkProduct",
      "verifyLink",
      "setLinkPreferredExportMode",
      "saveMappingSelections",
    ]),

    linkFor(conn) {
      return this.linksByConnectionId[conn.id] || null;
    },

    richFor(conn) {
      return this.richByConnectionId?.[conn.id] || null;
    },

    /** Merge list-row link flags with available-connections row for the menu. */
    menuConnection(conn) {
      const rich = this.richFor(conn);
      const link = this.linkFor(conn);
      if (rich) return rich;
      return {
        connectionId: conn.id,
        connectionName: conn.name,
        adapterKey: conn.adapterKey,
        adapterName: conn.name || conn.adapterKey,
        isLinked: Boolean(link),
        linkId: link?.linkId,
        linkStatus: link?.status,
        status: link?.status,
        externalProductId: link?.externalProductId,
        preferredExportMode: link?.preferredExportMode,
        hasMapping: link?.hasMapping,
        mappingMissing: link?.mappingMissing,
        canExport: true,
        canImport: Boolean(link),
        canExportPrices: true,
        canExportStocks: true,
        availableMappings: [],
      };
    },

    displayConn(conn) {
      const rich = this.richFor(conn);
      const link = this.linkFor(conn);
      return {
        ...(link || {}),
        ...(rich || {}),
        linkStatus: rich?.linkStatus || link?.status,
        status: rich?.linkStatus || link?.status,
        hasMapping: rich?.hasMapping ?? link?.hasMapping,
        mappingMissing: rich?.mappingMissing ?? link?.mappingMissing,
      };
    },

    buttonClass(conn) {
      return adapterConnectionButtonClass(this.displayConn(conn));
    },

    statusLabel(conn) {
      return adapterConnectionStatusLabel(this.displayConn(conn), {
        categoryName: this.categoryName,
      });
    },

    buttonTitle(conn) {
      const base = `${conn.name || conn.adapterKey} — ${this.statusLabel(conn)}`;
      if (this.disabled) {
        return `${base}. ${t("products.adaptersColumn.disabledInTrash")}`;
      }
      return base;
    },

    marketplaceUrl(conn) {
      const rich = this.richFor(conn);
      const link = this.linkFor(conn);
      const template =
        rich?.productEditUrlTemplate ||
        conn.productEditUrlTemplate ||
        conn.ProductEditUrlTemplate;
      const externalId = rich?.externalProductId || link?.externalProductId;
      if (!template || !externalId) return null;
      const publicCreds =
        rich?.publicCredentials ||
        conn.publicCredentials ||
        conn.PublicCredentials ||
        {};
      return fillProductEditUrl(template, { ...publicCreds, externalId });
    },

    configureMappingTo(conn) {
      const query = { tab: "mappings", createMapping: "true" };
      if (this.categoryId != null) query.localCategoryId = String(this.categoryId);
      if (this.categoryName) query.localCategoryName = this.categoryName;
      return { path: `/adapter/${conn.id}`, query };
    },

    adaptersTabTo() {
      return {
        path: `/products/${this.product.id}/edit`,
        query: { tab: "marketplaces" },
      };
    },

    async ensureDetails() {
      if (this.disabled || this.detailsLoaded || this.detailsLoading) return;
      this.detailsLoading = true;
      this.detailsError = false;
      await this.getAvailableConnectionsForProduct({
        productId: this.product.id,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          this.categoryId = data.categoryId ?? null;
          this.categoryName = data.categoryName ?? null;
          const map = {};
          const selections = {};
          for (const row of data.allConnections || []) {
            map[row.connectionId] = row;
            if (row.selectedMappingId) {
              selections[row.connectionId] = row.selectedMappingId;
            } else if (row.availableMappings?.length === 1) {
              selections[row.connectionId] = row.availableMappings[0].id;
            }
          }
          this.richByConnectionId = map;
          this.mappingSelections = selections;
          this.detailsLoaded = true;
        },
        onError: (e) => {
          this.detailsError = true;
          displaySonnerError(e);
        },
      });
      this.detailsLoading = false;
    },

    onMenuOpen(open) {
      if (this.disabled) return;
      if (open) this.ensureDetails();
    },

    async handleMappingChange(conn, mappingId) {
      this.mappingSelections = {
        ...this.mappingSelections,
        [conn.id]: mappingId,
      };
      try {
        await this.saveMappingSelections({
          connectionId: conn.id,
          selections: [
            { productId: this.product.id, categoryMappingId: mappingId },
          ],
        });
        this.detailsLoaded = false;
        this.richByConnectionId = null;
        await this.ensureDetails();
      } catch {
        displaySonnerError(
          t("productMarketplaceIntegrations.errorSavingMapping"),
        );
      }
    },

    async runExport(conn) {
      const rich = this.richFor(conn);
      if (!rich || hasMappingIssue(rich) || !rich.canExport) {
        if (rich?.mappingMissing) {
          displaySonnerError(
            t("productMarketplaceIntegrations.mappingDeletedError"),
          );
        } else if (rich && !rich.hasMapping) {
          displaySonnerError(
            t("productMarketplaceIntegrations.mappingNotConfigured", {
              category: this.categoryName || "",
            }),
          );
        }
        return;
      }
      if (
        rich.availableMappings?.length > 1 &&
        !this.mappingSelections[conn.id]
      ) {
        displaySonnerError(
          t("productMarketplaceIntegrations.chooseMappingBeforeExport"),
        );
        return;
      }

      const link = this.linkFor(conn);
      const linkId = rich.linkId || link?.linkId;
      this.busyKey = `export-${conn.id}`;
      try {
        if (linkId) {
          const mode = normalizeExportMode(rich.preferredExportMode);
          if (mode === "price") {
            if (!rich.canExportPrices) {
              displaySonnerError(
                t("productMarketplaceIntegrations.noLinkedForPriceExport"),
              );
              return;
            }
            await this.exportPrices({
              connectionId: conn.id,
              linkIds: [linkId],
              onSuccess: () => {
                displaySonnerSuccess(
                  t("productMarketplaceIntegrations.priceExportedToAdapter", {
                    adapter: rich.adapterName || conn.name || conn.adapterKey,
                  }),
                );
                this.$emit("changed");
              },
              onError: (e) => displaySonnerError(e),
            });
          } else if (mode === "stock") {
            if (!rich.canExportStocks) {
              displaySonnerError(
                t("productMarketplaceIntegrations.noLinkedForStockExport"),
              );
              return;
            }
            await this.exportStocks({
              connectionId: conn.id,
              linkIds: [linkId],
              onSuccess: () => {
                displaySonnerSuccess(
                  t("productMarketplaceIntegrations.stockExportedToAdapter", {
                    adapter: rich.adapterName || conn.name || conn.adapterKey,
                  }),
                );
                this.$emit("changed");
              },
              onError: (e) => displaySonnerError(e),
            });
          } else {
            await this.exportFromLink({
              linkId,
              onSuccess: () => {
                displaySonnerSuccess(
                  t("productMarketplaceIntegrations.productUpdatedOnAdapter", {
                    adapter: rich.adapterName || conn.name || conn.adapterKey,
                  }),
                );
                this.$emit("changed");
              },
              onError: (e) => displaySonnerError(e),
            });
          }
        } else {
          await this.exportProduct({
            connectionId: conn.id,
            productId: this.product.id,
            onSuccess: () => {
              displaySonnerSuccess(
                t("productMarketplaceIntegrations.productExportedToAdapter", {
                  adapter: rich.adapterName || conn.name || conn.adapterKey,
                }),
              );
              this.detailsLoaded = false;
              this.richByConnectionId = null;
              this.$emit("changed");
            },
            onError: (e) => displaySonnerError(e),
          });
        }
      } finally {
        this.busyKey = null;
      }
    },

    async setExportMode(conn, mode) {
      const rich = this.richFor(conn);
      const linkId = rich?.linkId || this.linkFor(conn)?.linkId;
      if (!linkId || hasMappingIssue(rich)) return;
      await this.setLinkPreferredExportMode({
        linkId,
        mode,
        onSuccess: () => {
          if (rich) rich.preferredExportMode = mode;
          const link = this.linkFor(conn);
          if (link) link.preferredExportMode = mode;
        },
        onError: (e) => displaySonnerError(e),
      });
    },

    async runImport(conn) {
      const rich = this.richFor(conn);
      const linkId = rich?.linkId || this.linkFor(conn)?.linkId;
      if (!linkId || !rich?.canImport) return;
      this.busyKey = `import-${conn.id}`;
      try {
        await this.importFromLink({
          linkId,
          onSuccess: (response) => {
            const result = extractResponseData(response, {});
            if (result.updatedFields?.length) {
              displaySonnerSuccess(
                t("productMarketplaceIntegrations.updatedFieldsMessage", {
                  fields: result.updatedFields.join(", "),
                }),
              );
            } else {
              displaySonnerSuccess(
                t("productMarketplaceIntegrations.noChangesFound"),
              );
            }
            this.$emit("changed");
          },
          onError: (e) => displaySonnerError(e),
        });
      } finally {
        this.busyKey = null;
      }
    },

    async runVerify(conn) {
      const rich = this.richFor(conn);
      const linkId = rich?.linkId || this.linkFor(conn)?.linkId;
      if (!linkId) return;
      this.busyKey = `verify-${conn.id}`;
      try {
        await this.verifyLink({
          linkId,
          onSuccess: (response) => {
            const result = extractResponseData(response, {});
            const status = result.status || result.Status;
            if (result.broken > 0 || status === "RemoteMissing") {
              displaySonnerError(
                t("productMarketplaceIntegrations.verifyBroken"),
              );
            } else {
              displaySonnerSuccess(
                t("productMarketplaceIntegrations.verifySuccess"),
              );
            }
            // Patch local status — do not reload the whole products table.
            const link = this.linkFor(conn);
            if (link && status) link.status = status;
            if (rich && status) rich.linkStatus = status;
          },
          onError: (e) =>
            displaySonnerError(
              e || t("productMarketplaceIntegrations.verifyError"),
            ),
        });
      } finally {
        this.busyKey = null;
      }
    },

    async runUnlink(conn) {
      const rich = this.richFor(conn);
      const linkId = rich?.linkId || this.linkFor(conn)?.linkId;
      if (!linkId) return;
      const ok = await confirm({
        title: t("common.confirm"),
        description: t(
          "productMarketplaceIntegrations.unlinkConfirmDescription",
          {
            adapter: rich?.adapterName || conn.name || conn.adapterKey,
          },
        ),
        confirmLabel: t("productMarketplaceIntegrations.menuUnlink"),
        variant: "destructive",
      });
      if (!ok) return;
      this.busyKey = `unlink-${conn.id}`;
      try {
        await this.unlinkProduct({
          linkId,
          onSuccess: () => {
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.productUnlinked", {
                adapter: rich?.adapterName || conn.name || conn.adapterKey,
              }),
            );
            this.detailsLoaded = false;
            this.richByConnectionId = null;
            this.$emit("changed");
          },
          onError: (e) => displaySonnerError(e),
        });
      } finally {
        this.busyKey = null;
      }
    },

    stopRow(e) {
      e.stopPropagation();
    },
  },
};
</script>

<template>
  <div
    v-if="activeConnections.length === 0"
    class="text-muted-foreground text-sm"
    @click="stopRow"
  >
    —
  </div>
  <ButtonGroup
    v-else-if="disabled"
    class="max-w-full flex-wrap opacity-60"
    @click="stopRow"
  >
    <Button
      v-for="conn in activeConnections"
      :key="conn.id"
      type="button"
      variant="outline"
      size="icon"
      disabled
      :class="buttonClass(conn)"
      :title="buttonTitle(conn)"
      :aria-label="buttonTitle(conn)"
    >
      <AdapterIcon :adapter-key="conn.adapterKey" class="size-4" />
    </Button>
  </ButtonGroup>
  <ButtonGroup v-else class="max-w-full flex-wrap" @click="stopRow">
    <ProductAdapterConnectionMenu
      v-for="conn in activeConnections"
      :key="conn.id"
      :connection="menuConnection(conn)"
      :selected-mapping-id="mappingSelections[conn.id]"
      :marketplace-url="marketplaceUrl(conn)"
      :configure-mapping-to="configureMappingTo(conn)"
      :link-on-product-to="adaptersTabTo()"
      :category-name="categoryName"
      align="start"
      :loading="detailsLoading && !detailsLoaded"
      :export-busy="busyKey === `export-${conn.id}`"
      :import-busy="busyKey === `import-${conn.id}`"
      :verify-busy="busyKey === `verify-${conn.id}`"
      :unlink-busy="busyKey === `unlink-${conn.id}`"
      show-link-on-product
      @update:open="onMenuOpen"
      @export="runExport(conn)"
      @update:export-mode="setExportMode(conn, $event)"
      @import="runImport(conn)"
      @verify="runVerify(conn)"
      @unlink="runUnlink(conn)"
      @update:mapping="handleMappingChange(conn, $event)"
    >
      <template #trigger>
        <Button
          type="button"
          variant="outline"
          size="icon"
          :class="buttonClass(conn)"
          :title="buttonTitle(conn)"
          :aria-label="`${conn.name || conn.adapterKey}: ${statusLabel(conn)}`"
          :disabled="busyKey != null && String(busyKey).endsWith(`-${conn.id}`)"
        >
          <Spinner
            v-if="busyKey && String(busyKey).endsWith(`-${conn.id}`)"
            size="sm"
          />
          <AdapterIcon v-else :adapter-key="conn.adapterKey" class="size-4" />
        </Button>
      </template>
    </ProductAdapterConnectionMenu>
  </ButtonGroup>
</template>
