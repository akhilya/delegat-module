<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import MappingCanvas from "@/components/marketplace/graph/MappingCanvas.vue";
import { mapActions } from "vuex";
import { displaySonnerError, extractResponseData } from "@/store/sonnerHelper";
import { setPageTitle, setBreadcrumbParentLabel } from "@/router";
import { mappingDisplayName } from "@/lib/mappingDisplayName";
import { localizeExternalFieldOptions } from "@/components/marketplace/graph/fieldLabels.js";
import { confirm } from "@/composables/useConfirm";
import { normalizeUnitCode } from "@/lib/attributeUnits";

const EMPTY_SOURCES = Object.freeze({
  productFields: [],
  stockFields: [],
  priceFields: [],
  categoryAttributes: [],
  storeAttributes: [],
});

function toFieldDef(f) {
  return {
    name: f.name,
    type: f.type,
    options: localizeExternalFieldOptions(f.options || []),
    description: f.description || null,
    group: f.group ?? null,
    sortOrder: f.sortOrder,
    unit: f.unit || null,
    unitCode: normalizeUnitCode(f.unitCode || f.unit),
    unitId: f.unitId || null,
    minValue: f.minValue ?? null,
    maxValue: f.maxValue ?? null,
    maxLength: f.maxLength ?? null,
    allowCustomValues: f.allowCustomValues ?? false,
    maxValues: f.maxValues ?? null,
  };
}

export default {
  components: {
    AppPageChrome,
    PageSection,
    MappingCanvas,
  },
  async beforeRouteLeave() {
    const canvas = this.$refs.mappingCanvas;
    if (!canvas?.isDirty?.()) return true;
    const result = await confirm({
      title: this.$t("marketplace.fieldMappingPage.leaveConfirm.title"),
      description: this.$t(
        "marketplace.fieldMappingPage.leaveConfirm.description",
      ),
      saveLabel: this.$t("marketplace.fieldMappingPage.leaveConfirm.saveLabel"),
      confirmLabel: this.$t(
        "marketplace.fieldMappingPage.leaveConfirm.confirmLabel",
      ),
      cancelLabel: this.$t(
        "marketplace.fieldMappingPage.leaveConfirm.cancelLabel",
      ),
      destructive: false,
    });
    if (result === "save") return canvas.save();
    return result === true;
  },
  data() {
    return {
      connectionId: null,
      mappingId: null,
      availableSources: { ...EMPTY_SOURCES },
      storeFiles: [],
      isLoading: false,
      mappingInfo: null,
      // Live field definitions keyed by field key (name, type, options, units, limits).
      externalFieldDefs: {},
      priceFieldDefs: {},
      priceFieldMappings: [],
      // Graph response fetched with the page data; handed to the canvas on its first load.
      prefetchedGraph: null,
      previewProductId: null,
      previewProducts: [],
      previewProductsLoading: false,
      previewProductSearchTimer: null,
      previewSelectedOption: null,
    };
  },
  computed: {
    canvasSources() {
      return { ...this.availableSources, storeFiles: this.storeFiles };
    },
  },
  watch: {
    "$store.state.aiChat.mappingRefreshToken"() {
      this.onAssistantChangedMapping();
    },
  },
  mounted() {
    this.connectionId = this.$route.params.connId;
    this.mappingId = this.$route.params.mappingId;
    this.fetchData().then(() => {
      const qp = this.$route.query.previewProduct;
      if (qp) this.onPreviewProductChange(qp);
      this.updateAiPageContext();
    });
  },
  beforeUnmount() {
    if (this.previewProductSearchTimer) {
      clearTimeout(this.previewProductSearchTimer);
    }
    this.$store.commit("aiChat/clearPageContext");
  },
  methods: {
    ...mapActions([
      "getMappingGraph",
      "getAvailableSources",
      "getStoreFiles",
      "lookupProducts",
    ]),

    async fetchData() {
      this.isLoading = true;
      try {
        // One graph request returns the graph, mapping info and marketplace schema.
        const [graph] = await Promise.all([
          this.requestGraph(),
          this.fetchAvailableSources(),
          this.fetchStoreFiles(),
        ]);
        this.prefetchedGraph = graph;
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.isLoading = false;
      }
      // The selector searches on open. A saved preview choice must not block the editor.
      void this.ensureSelectedPreviewInOptions();
    },

    async requestGraph() {
      const data = await this.getMappingGraph({
        connectionId: this.connectionId,
        mappingId: this.mappingId,
        includeSchema: true,
      });
      this.applyGraphResponse(data);
      return data;
    },

    // Canvas loader: first call reuses the prefetched response, later calls refetch.
    async loadGraph() {
      const prefetched = this.prefetchedGraph;
      this.prefetchedGraph = null;
      if (prefetched) return prefetched;
      const data = await this.requestGraph();
      // Let the canvas receive updated field props before it rebuilds the graph.
      await this.$nextTick();
      return data;
    },

    applyGraphResponse(data) {
      const info = data?.mapping;
      if (info) {
        this.mappingInfo = info;
        setPageTitle(mappingDisplayName(info) || info.localCategoryName);
        setBreadcrumbParentLabel(info.connectionName);
      }
      if (Array.isArray(data?.externalFields)) {
        const defs = {};
        for (const f of data.externalFields) defs[f.key] = toFieldDef(f);
        this.externalFieldDefs = defs;
      }
      if (Array.isArray(data?.priceFields)) {
        const defs = {};
        const mappings = [];
        for (const f of data.priceFields) {
          defs[f.key] = toFieldDef(f);
          mappings.push({
            externalFieldKey: f.key,
            externalFieldName: f.name,
            externalFieldType: f.type,
            externalFieldUnit: f.unit || null,
            externalFieldUnitCode: f.unitCode || null,
            externalFieldUnitId: f.unitId || null,
            group: f.group,
            description: f.description,
            sortOrder: f.sortOrder,
            isRequired: !!f.isRequired,
            isEnabled: true,
          });
        }
        this.priceFieldDefs = defs;
        this.priceFieldMappings = mappings;
      }
    },

    async fetchAvailableSources() {
      await this.getAvailableSources({
        mappingId: this.mappingId,
        onSuccess: (response) => {
          this.availableSources = {
            ...EMPTY_SOURCES,
            ...extractResponseData(response, {}),
          };
        },
        onError: (error) => displaySonnerError(error),
      });
    },

    async fetchStoreFiles() {
      try {
        this.storeFiles = (await this.getStoreFiles()) || [];
      } catch {
        this.storeFiles = []; // shared storage is optional - don't block the editor
      }
    },

    async onFieldsSynced() {
      await this.$refs.mappingCanvas?.reload?.({ preserveLayout: true });
    },

    // The assistant edited this mapping server-side: pull the new graph unless that would
    // discard the operator's unsaved edits without asking.
    async onAssistantChangedMapping() {
      const canvas = this.$refs.mappingCanvas;
      if (!canvas) return;
      void this.fetchAvailableSources();
      if (canvas.isDirty?.()) {
        const ok = await confirm({
          title: this.$t("marketplace.fieldMappingPage.externalChange.title"),
          description: this.$t(
            "marketplace.fieldMappingPage.externalChange.description",
          ),
          confirmLabel: this.$t(
            "marketplace.fieldMappingPage.externalChange.reload",
          ),
          cancelLabel: this.$t(
            "marketplace.fieldMappingPage.externalChange.keep",
          ),
        });
        if (!ok) return;
      }
      await canvas.reload({ preserveLayout: true });
    },

    // === Preview ===

    mapPreviewProductOption(p) {
      return {
        value: String(p.id),
        label: `${p.name || p.sku || this.$t("marketplace.fieldMappingPage.untitled")}${p.sku ? ` · ${p.sku}` : ""} (ID: ${p.id})`,
      };
    },

    async fetchPreviewProducts(searchQuery = "") {
      const requestId = (this._previewSearchRequestId =
        (this._previewSearchRequestId || 0) + 1);
      this.previewProductsLoading = true;
      await this.lookupProducts({
        searchQuery: searchQuery || "",
        categoryId: this.mappingInfo?.localCategoryId || null,
        onSuccess: (response) => {
          if (requestId !== this._previewSearchRequestId) return;
          const items = (extractResponseData(response, {}).items || []).map(
            (p) => this.mapPreviewProductOption(p),
          );
          const selected = this.previewSelectedOption;
          this.previewProducts =
            selected && !items.some((o) => o.value === selected.value)
              ? [selected, ...items]
              : items;
          this.previewProductsLoading = false;
        },
        onError: () => {
          if (requestId !== this._previewSearchRequestId) return;
          this.previewProductsLoading = false;
        },
      });
    },

    onPreviewProductSearch(query) {
      if (this.previewProductSearchTimer) {
        clearTimeout(this.previewProductSearchTimer);
      }
      this.previewProductSearchTimer = setTimeout(() => {
        this.fetchPreviewProducts(query || "");
      }, 250);
    },

    async ensureSelectedPreviewInOptions() {
      if (!this.previewProductId) return;
      const id = String(this.previewProductId);
      const existing = this.previewProducts.find((o) => o.value === id);
      if (existing) {
        this.previewSelectedOption = existing;
        return;
      }
      await this.lookupProducts({
        productId: this.previewProductId,
        onSuccess: (response) => {
          const p = extractResponseData(response, {})?.items?.[0];
          if (!p?.id) return;
          const opt = this.mapPreviewProductOption(p);
          this.previewSelectedOption = opt;
          this.previewProducts = [
            opt,
            ...this.previewProducts.filter((o) => o.value !== opt.value),
          ];
        },
        onError: () => {},
      });
    },

    onPreviewProductChange(productId) {
      this.previewProductId = productId ? parseInt(productId) : null;
      this.previewSelectedOption = this.previewProductId
        ? this.previewProducts.find(
            (o) => o.value === String(this.previewProductId),
          ) || this.previewSelectedOption
        : null;
      const query = { ...this.$route.query };
      if (this.previewProductId) {
        query.previewProduct = String(this.previewProductId);
      } else {
        delete query.previewProduct;
      }
      this.$router.replace({ query });
      this.updateAiPageContext();
    },

    updateAiPageContext() {
      this.$store.commit("aiChat/setPageContext", {
        page: "fieldMapping",
        mappingId: parseInt(this.mappingId),
        mappingName:
          this.mappingInfo?.name ||
          this.$t("marketplace.fieldMappingPage.mappingHash", {
            id: this.mappingId,
          }),
        connectionId: parseInt(this.connectionId),
        connectionName: this.mappingInfo?.connectionName || "",
        previewProductId: this.previewProductId || null,
      });
    },
  },
};
</script>

<template>
  <AppPageChrome fill-viewport>
    <PageSection
      :loading="isLoading"
      :loading-message="$t('marketplace.fieldMappingPage.loadStatus.mapping')"
      class="!mb-0 flex min-h-0 flex-1 flex-col overflow-hidden"
    >
      <div
        v-if="!isLoading && connectionId && mappingId"
        class="relative min-h-0 w-full flex-1"
      >
        <MappingCanvas
          ref="mappingCanvas"
          :connection-id="connectionId"
          :mapping-id="mappingId"
          :graph-loader="loadGraph"
          :external-field-defs="externalFieldDefs"
          :price-field-mappings="priceFieldMappings"
          :price-field-defs="priceFieldDefs"
          :available-sources="canvasSources"
          :preview-product-id="previewProductId"
          :preview-products="previewProducts"
          :preview-products-loading="previewProductsLoading"
          @select-preview="onPreviewProductChange"
          @clear-preview="onPreviewProductChange(null)"
          @search-preview="onPreviewProductSearch"
          @fields-synced="onFieldsSynced"
        />
      </div>
    </PageSection>
  </AppPageChrome>
</template>
