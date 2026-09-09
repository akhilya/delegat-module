<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import MappingCanvas from "@/components/marketplace/graph/MappingCanvas.vue";
import { mergeProductFieldSources } from "@/components/marketplace/graph/graphModel.js";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { setPageTitle, setBreadcrumbParentLabel } from "@/router";
import {
  Plus,
} from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import AppImage from "@/components/AppImage.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import { SHEET_WIDTH_DEFAULTS, SHEET_WIDTH_STORAGE } from "@/composables/useSheetResize";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import { mappingDisplayName } from "@/lib/mappingDisplayName";
import { localizeExternalFieldOptions } from "@/components/marketplace/graph/fieldLabels.js";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetHeaderClose,
  SheetTitle,
} from "@/components/ui/sheet";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export default {
  components: {
    AppPageChrome,
    PageSection,
    MappingCanvas,
    EmptyState,
    Button,
    Input,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    Spinner,
    AppImage,
    Plus,
    SettingsFormField,
    FieldGroup,
    InsetGroup,
    InsetGroupItem,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogFooter,
    DialogClose,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Sheet,
    SheetContent,
    SheetHeader,
    SheetHeaderClose,
    SheetTitle,
    SheetDescription,
  },
  data() {
    return {
      mappingSheetWidth: SHEET_WIDTH_DEFAULTS.mappingPreview,
      mappingSheetKey: SHEET_WIDTH_STORAGE.mappingPreview,
      viewMode: "graph",
      connectionId: null,
      mappingId: null,
      fieldMappings: [],
      availableSources: {
        productFields: [],
        stockFields: [],
        priceFields: [],
        categoryAttributes: [],
        storeAttributes: [],
      },
      storeFiles: [],
      isLoading: false,
      loadStatus: "mapping",
      isSaving: false,
      hasChanges: false,
      mappingInfo: null,
      // Preview
      previewProductId: null,
      previewProducts: [],
      previewProductsLoading: false,
      previewProductSearchTimer: null,
      previewSelectedOption: null,
      previewData: {}, // keyed by externalFieldKey
      isLoadingPreview: false,
      previewDebounceTimer: null,
      // External field descriptions keyed by externalFieldKey
      externalFieldDescriptions: {},
      // Aspect fields (size, color) keyed by externalFieldKey
      aspectFields: {},
      // Live field definitions from /external-fields (options, type, name) keyed by externalFieldKey
      externalFieldDefs: {},
      priceFieldDefs: {},
      priceFieldMappings: [],
      // Modifier sheet
      editingFieldMapping: null,
      showModifierSheet: false,
      // Preview summary sheet
      showPreviewSheet: false,
      previewVisualMode: false,
      // Mapping settings dialog
      showMappingSettings: false,
      settingsName: "",
      isSavingSettings: false,
      // Cache for lazily-loaded attribute options (keyed "category:<id>" or "store:<id>")
      attributeOptionsCache: {},
      attributeOptionsLoading: {},
      // For manually adding a new field
      showAddField: false,
      newField: {
        externalFieldKey: "",
        externalFieldName: "",
        sourceType: "Static",
        staticValue: "",
      },
    };
  },
  computed: {
    requiredFieldsMapped() {
      return this.fieldMappings.filter(
        (fm) => fm.isRequired && fm.isEnabled && this.hasValidSource(fm),
      ).length;
    },
    totalRequiredFields() {
      return this.fieldMappings.filter((fm) => fm.isRequired).length;
    },
    unmappedRequiredFields() {
      return this.fieldMappings.filter(
        (fm) => fm.isRequired && fm.isEnabled && !this.hasValidSource(fm),
      );
    },
    hasUnmappedRequired() {
      return this.unmappedRequiredFields.length > 0;
    },
    loadStatusText() {
      return this.$t(`marketplace.fieldMappingPage.loadStatus.${this.loadStatus}`);
    },
    previewSummary() {
      return this.fieldMappings
        .filter((fm) => fm.isEnabled)
        .map((fm) => {
          const p = this.previewData[fm.externalFieldKey];
          const sourceValue = p?.sourceValue ?? null;
          const resultValue = p?.resultValue ?? null;
          const error = p?.error ?? null;
          return {
            fieldName: fm.externalFieldName || fm.externalFieldKey,
            externalFieldKey: fm.externalFieldKey,
            sourceValue,
            resultValue,
            error,
            isChanged:
              sourceValue != resultValue && resultValue != null && !error,
          };
        });
    },
    sortedFieldMappings() {
      const required = this.fieldMappings.filter((fm) => fm.isRequired);
      const enabled = this.fieldMappings.filter(
        (fm) => !fm.isRequired && fm.isEnabled,
      );
      const disabled = this.fieldMappings.filter(
        (fm) => !fm.isRequired && !fm.isEnabled,
      );
      return [...required, ...enabled, ...disabled];
    },
    editingSourceFieldOptions() {
      const fm = this.editingFieldMapping;
      if (!fm) return [];
      if (fm.sourceType === "CategoryAttribute" && fm.sourceAttributeId) {
        return (
          this.attributeOptionsCache[`category:${fm.sourceAttributeId}`] || []
        );
      } else if (fm.sourceType === "StoreAttribute" && fm.sourceAttributeId) {
        return (
          this.attributeOptionsCache[`store:${fm.sourceAttributeId}`] || []
        );
      }
      return [];
    },
  },
  watch: {
    "$store.state.aiChat.mappingRefreshToken"() {
      this.fetchData();
    },
  },
  methods: {
    ...mapActions([
      "getFieldMappings",
      "saveFieldMappings",
      "toggleFieldMapping",
      "updateFieldMapping",
      "getAvailableSources",
      "getStoreFiles",
      "getExternalFields",
      "getPriceFields",
      "previewFieldMappings",
      "getAllProducts",
      "getProduct",
      "updateCategoryMapping",
      "getAttributeOptions",
    ]),

    waitForPaint() {
      return new Promise((resolve) => {
        requestAnimationFrame(() => requestAnimationFrame(resolve));
      });
    },

    async fetchData() {
      this.isLoading = true;
      this.loadStatus = "mapping";
      await this.waitForPaint();
      try {
        const dictionaries = this.fetchExternalFieldDescriptions();
        const priceFields = this.fetchPriceFieldDescriptions();
        const sources = this.fetchAvailableSources();
        const files = this.fetchStoreFiles();
        await this.fetchFieldMappings();
        this.loadStatus = "dictionaries";
        await this.waitForPaint();
        await Promise.all([dictionaries, priceFields]);
        this.loadStatus = "sources";
        await this.waitForPaint();
        await Promise.all([sources, files]);
        this.loadStatus = "products";
        await this.waitForPaint();
        await this.fetchPreviewProducts();
        await this.ensureSelectedPreviewInOptions();
      } finally {
        this.isLoading = false;
      }
    },

    async fetchFieldMappings() {
      await this.getFieldMappings({
        mappingId: this.mappingId,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          this.fieldMappings = (data.fieldMappings || []).map((fm) => ({
            ...fm,
            rawTextMode: false,
            _newStaticValue: "",
            _optionSearch: "",
            parsedOptions: fm.externalFieldOptions
              ? (() => {
                  try {
                    return JSON.parse(fm.externalFieldOptions);
                  } catch {
                    return [];
                  }
                })()
              : [],
            parsedModifiers: fm.modifiersJson
              ? (() => {
                  try {
                    return JSON.parse(fm.modifiersJson);
                  } catch {
                    return [];
                  }
                })()
              : [],
          }));
          this.mappingInfo = {
            name: data.name,
            connectionName: data.connectionName,
            localCategoryId: data.localCategoryId,
            localCategoryName: data.localCategoryName,
            externalCategoryId: data.externalCategoryId,
            externalCategoryName: data.externalCategoryName,
          };

          setPageTitle(
            mappingDisplayName(data) || data.localCategoryName,
          );
          setBreadcrumbParentLabel(data.connectionName);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async fetchAvailableSources() {
      await this.getAvailableSources({
        mappingId: this.mappingId,
        onSuccess: (response) => {
          this.availableSources = extractResponseData(response, {
            productFields: [],
            stockFields: [],
            priceFields: [],
            categoryAttributes: [],
            storeAttributes: [],
          });
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async fetchStoreFiles() {
      try {
        this.storeFiles = (await this.getStoreFiles()) || [];
      } catch {
        this.storeFiles = []; // shared storage is optional - don't block the editor
      }
    },

    async fetchExternalFieldDescriptions() {
      await this.getExternalFields({
        mappingId: this.mappingId,
        onSuccess: (response) => {
          const fields = extractResponseData(response, []);
          const descMap = {};
          const aspectMap = {};
          const defsMap = {};
          for (const f of fields) {
            if (f.description) descMap[f.key] = f.description;
            if (f.isAspect) aspectMap[f.key] = true;
            defsMap[f.key] = {
              name: f.name,
              type: f.type,
              options: localizeExternalFieldOptions(f.options || []),
              description: f.description || null,
              unit: f.unit || null,
              minValue: f.minValue ?? null,
              maxValue: f.maxValue ?? null,
              maxLength: f.maxLength ?? null,
              allowCustomValues: f.allowCustomValues ?? false,
              maxValues: f.maxValues ?? null,
            };
          }
          this.externalFieldDescriptions = descMap;
          this.aspectFields = aspectMap;
          this.externalFieldDefs = defsMap;
        },
        onError: () => {
          /* non-critical */
        },
      });
    },

    async fetchPriceFieldDescriptions() {
      await this.getPriceFields({
        mappingId: this.mappingId,
        onSuccess: (response) => {
          const fields = extractResponseData(response, []);
          const defsMap = {};
          const mappings = [];
          for (const f of fields) {
            defsMap[f.key] = {
              name: f.name,
              type: f.type,
              options: localizeExternalFieldOptions(f.options || []),
              description: f.description,
            };
            mappings.push({
              externalFieldKey: f.key,
              externalFieldName: f.name,
              externalFieldType: f.type,
              isRequired: !!f.isRequired,
              isEnabled: true,
            });
          }
          this.priceFieldDefs = defsMap;
          this.priceFieldMappings = mappings;
        },
        onError: () => {
          this.priceFieldDefs = {};
          this.priceFieldMappings = [];
        },
      });
    },

    async handleToggleField(fieldMapping) {
      await this.toggleFieldMapping({
        mappingId: this.mappingId,
        fieldMappingId: fieldMapping.id,
        onSuccess: () => {
          fieldMapping.isEnabled = !fieldMapping.isEnabled;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    onSourceTypeChange(fieldMapping, newType) {
      fieldMapping.sourceType = newType;
      fieldMapping.sourceField = null;
      fieldMapping.sourceAttributeId = null;
      fieldMapping.staticValue = null;
      this.hasChanges = true;
      this.debouncedPreview();
    },

    onSourceFieldChange(fieldMapping, value) {
      if (fieldMapping.sourceType === "ProductField") {
        fieldMapping.sourceField = value;
      } else if (
        fieldMapping.sourceType === "CategoryAttribute" ||
        fieldMapping.sourceType === "StoreAttribute"
      ) {
        fieldMapping.sourceAttributeId = parseInt(value) || null;
      }
      this.hasChanges = true;
      this.debouncedPreview();
    },

    onStaticValueChange(fieldMapping, value) {
      fieldMapping.staticValue = value != null ? String(value) : null;
      this.hasChanges = true;
      this.debouncedPreview();
    },

    _parseMultiselectValue(value) {
      if (!value) return [];
      if (value.startsWith("[")) {
        try {
          const arr = JSON.parse(value);
          return Array.isArray(arr) ? arr : [];
        } catch {
          return [];
        }
      }
      return value.split(",").filter((v) => v !== "");
    },

    onMultiselectChange(fieldMapping, optionValue, checked) {
      const current = this._parseMultiselectValue(fieldMapping.staticValue);
      if (checked) {
        if (!current.includes(optionValue)) current.push(optionValue);
      } else {
        const idx = current.indexOf(optionValue);
        if (idx !== -1) current.splice(idx, 1);
      }
      fieldMapping.staticValue = JSON.stringify(current);
      this.hasChanges = true;
    },

    getMultiselectLabels(fm) {
      const values = this._parseMultiselectValue(fm.staticValue);
      return values.map((val) => {
        const opt = (fm.parsedOptions || []).find((o) => o.value === val);
        return opt ? opt.label : val;
      });
    },

    getStaticValues(fm) {
      const values = this._parseMultiselectValue(fm.staticValue);
      return values.map((val) => {
        const opt = (fm.parsedOptions || []).find((o) => o.value === val);
        return { value: val, label: opt ? opt.label : val };
      });
    },

    addStaticValue(fm, value) {
      if (!value) return;
      const current = this._parseMultiselectValue(fm.staticValue);
      if (current.includes(value)) return;
      current.push(value);
      fm.staticValue = JSON.stringify(current);
      this.hasChanges = true;
    },

    removeStaticValue(fm, index) {
      const current = this._parseMultiselectValue(fm.staticValue);
      current.splice(index, 1);
      fm.staticValue = current.length ? JSON.stringify(current) : "";
      this.hasChanges = true;
    },

    getAvailableOptions(fm) {
      const selected = this._parseMultiselectValue(fm.staticValue);
      return (fm.parsedOptions || []).filter(
        (o) => !selected.includes(o.value),
      );
    },

    filterOptions(fm, options) {
      if (!fm._optionSearch) return options.slice(0, 100);
      const q = fm._optionSearch.toLowerCase();
      return options
        .filter((o) => o.label.toLowerCase().includes(q))
        .slice(0, 200);
    },

    addFreeformStaticValue(fm) {
      const val = (fm._newStaticValue || "").trim();
      if (!val) return;
      this.addStaticValue(fm, val);
      fm._newStaticValue = "";
    },

    onMultiselectDropdownChange(fm, selectedLabels) {
      const values = selectedLabels.map((label) => {
        const opt = (fm.parsedOptions || []).find((o) => o.label === label);
        return opt ? opt.value : label;
      });
      fm.staticValue = JSON.stringify(values);
      this.hasChanges = true;
    },

    toggleRawTextMode(fm) {
      fm.rawTextMode = !fm.rawTextMode;
    },

    hasValidSource(fieldMapping) {
      switch (fieldMapping.sourceType) {
        case "ProductField":
          return (
            !!fieldMapping.sourceField &&
            mergeProductFieldSources(this.availableSources).some(
              (f) => f.key === fieldMapping.sourceField,
            )
          );
        case "CategoryAttribute":
          return (
            !!fieldMapping.sourceAttributeId &&
            (this.availableSources.categoryAttributes || []).some(
              (a) => a.id === fieldMapping.sourceAttributeId,
            )
          );
        case "StoreAttribute":
          return (
            !!fieldMapping.sourceAttributeId &&
            (this.availableSources.storeAttributes || []).some(
              (a) => a.id === fieldMapping.sourceAttributeId,
            )
          );
        case "Static":
          return !!fieldMapping.staticValue;
        case "GroupName":
          return true; // GroupName does not require any additional parameters
        default:
          return false;
      }
    },

    isUnmappedRequired(fieldMapping) {
      return (
        fieldMapping.isRequired &&
        fieldMapping.isEnabled &&
        !this.hasValidSource(fieldMapping)
      );
    },

    isUnmappedOptional(fieldMapping) {
      return (
        !fieldMapping.isRequired &&
        fieldMapping.isEnabled &&
        !this.hasValidSource(fieldMapping)
      );
    },

    getSourceTypeLabel(sourceType) {
      switch (sourceType) {
        case "ProductField":
          return this.$t("marketplace.fieldMappingPage.sourceType.productField");
        case "CategoryAttribute":
          return this.$t("marketplace.fieldMappingPage.sourceType.categoryAttribute");
        case "StoreAttribute":
          return this.$t("marketplace.fieldMappingPage.sourceType.storeAttribute");
        case "Static":
          return this.$t("marketplace.fieldMappingPage.sourceType.staticValue");
        case "GroupName":
          return this.$t("marketplace.fieldMappingPage.sourceType.groupName");
        default:
          return sourceType;
      }
    },

    getCurrentSourceLabel(fieldMapping) {
      switch (fieldMapping.sourceType) {
        case "ProductField": {
          const pf = mergeProductFieldSources(this.availableSources).find(
            (f) => f.key === fieldMapping.sourceField,
          );
          return pf?.displayName || fieldMapping.sourceField || this.$t("marketplace.fieldMappingPage.notSelected");
        }
        case "CategoryAttribute": {
          const ca = this.availableSources.categoryAttributes?.find(
            (a) => a.id === fieldMapping.sourceAttributeId,
          );
          return ca?.name || this.$t("marketplace.fieldMappingPage.notSelected");
        }
        case "StoreAttribute": {
          const sa = this.availableSources.storeAttributes?.find(
            (a) => a.id === fieldMapping.sourceAttributeId,
          );
          return sa?.name || this.$t("marketplace.fieldMappingPage.notSelected");
        }
        case "Static":
          return fieldMapping.staticValue || this.$t("marketplace.fieldMappingPage.notSet");
        case "GroupName":
          return this.$t("marketplace.fieldMappingPage.productGroupName");
        default:
          return this.$t("marketplace.fieldMappingPage.notConfigured");
      }
    },

    async handleSave() {
      this.isSaving = true;
      const mappingsToSave = this.fieldMappings.map((fm) => ({
        id: fm.id,
        externalFieldKey: fm.externalFieldKey,
        sourceType: fm.sourceType,
        sourceField: fm.sourceField,
        sourceAttributeId: fm.sourceAttributeId,
        staticValue: fm.staticValue,
        isEnabled: fm.isEnabled,
        modifiersJson:
          fm.parsedModifiers && fm.parsedModifiers.length > 0
            ? JSON.stringify(fm.parsedModifiers)
            : null,
      }));

      await this.saveFieldMappings({
        mappingId: this.mappingId,
        fieldMappings: mappingsToSave,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("marketplace.fieldMappingPage.mappingsSaved"));
          this.hasChanges = false;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isSaving = false;
    },

    // === Modifiers ===

    openModifierSheet(fieldMapping) {
      this.editingFieldMapping = fieldMapping;
      this.showModifierSheet = true;
      this.ensureAttributeOptionsLoaded(fieldMapping);
    },

    ensureAttributeOptionsLoaded(fm) {
      let type = null;
      let id = null;
      if (fm.sourceType === "CategoryAttribute" && fm.sourceAttributeId) {
        type = "category";
        id = fm.sourceAttributeId;
      } else if (fm.sourceType === "StoreAttribute" && fm.sourceAttributeId) {
        type = "store";
        id = fm.sourceAttributeId;
      }
      if (!type || !id) return;

      const cacheKey = `${type}:${id}`;
      if (
        this.attributeOptionsCache[cacheKey] ||
        this.attributeOptionsLoading[cacheKey]
      )
        return;

      this.attributeOptionsLoading[cacheKey] = true;
      this.getAttributeOptions({
        type,
        id,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          let parsed = [];
          if (data.options) {
            try {
              parsed = JSON.parse(data.options);
            } catch {
              /* ignore */
            }
          }
          this.attributeOptionsCache = {
            ...this.attributeOptionsCache,
            [cacheKey]: parsed,
          };
          this.attributeOptionsLoading[cacheKey] = false;
        },
        onError: () => {
          this.attributeOptionsLoading[cacheKey] = false;
        },
      });
    },

    closeModifierSheet() {
      this.showModifierSheet = false;
      this.editingFieldMapping = null;
    },

    getModifierCount(fieldMapping) {
      return (fieldMapping.parsedModifiers || []).length;
    },

    onModifiersChange(fieldMapping, modifiers) {
      fieldMapping.parsedModifiers = modifiers;
      this.hasChanges = true;
      this.debouncedPreview();
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
      await this.getAllProducts({
        searchQuery: searchQuery || "",
        startIndex: 0,
        pageSize: 50,
        categoryId: this.mappingInfo?.localCategoryId || null,
        onSuccess: (response) => {
          if (requestId !== this._previewSearchRequestId) return;
          const data = extractResponseData(response, {});
          const items = (data.items || []).map((p) =>
            this.mapPreviewProductOption(p),
          );
          const selected = this.previewSelectedOption;
          if (
            selected &&
            !items.some((o) => o.value === selected.value)
          ) {
            this.previewProducts = [selected, ...items];
          } else {
            this.previewProducts = items;
          }
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
      await this.getProduct({
        productId: this.previewProductId,
        onSuccess: (response) => {
          const p = extractResponseData(response, null);
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

    updateAiPageContext() {
      this.$store.commit("aiChat/setPageContext", {
        page: "fieldMapping",
        mappingId: parseInt(this.mappingId),
        mappingName: this.mappingInfo?.name || this.$t("marketplace.fieldMappingPage.mappingHash", { id: this.mappingId }),
        connectionId: parseInt(this.connectionId),
        connectionName: this.mappingInfo?.connectionName || "",
        previewProductId: this.previewProductId || null,
      });
    },

    async onFieldsSynced() {
      await Promise.all([
        this.fetchFieldMappings(),
        this.fetchExternalFieldDescriptions(),
        this.fetchPriceFieldDescriptions(),
      ]);
      await this.$refs.mappingCanvas?.reloadAfterFieldSync?.();
    },

    onPreviewProductChange(productId) {
      this.previewProductId = productId ? parseInt(productId) : null;
      if (this.previewProductId) {
        const found = this.previewProducts.find(
          (o) => o.value === String(this.previewProductId),
        );
        if (found) this.previewSelectedOption = found;
      } else {
        this.previewSelectedOption = null;
      }
      const query = { ...this.$route.query };
      if (this.previewProductId) {
        query.previewProduct = String(this.previewProductId);
        // The graph canvas watches previewProductId and previews itself → onGraphPreview.
      } else {
        delete query.previewProduct;
        this.previewData = {};
      }
      this.$router.replace({ query });
      this.updateAiPageContext();
    },

    // Graph editor pushes its preview outputs up so the summary sheet (previewSummary)
    // reflects the graph instead of the legacy table preview.
    onGraphPreview({ outputs }) {
      const next = {};
      for (const [key, value] of Object.entries(outputs || {})) {
        next[key] = {
          externalFieldKey: key,
          sourceValue: null,
          resultValue: value,
          error: null,
        };
      }
      this.previewData = next;
    },

    // Legacy no-op: the graph canvas previews itself now (kept so dead table-edit handlers don't throw).
    debouncedPreview() {},

    getPreviewForField(externalFieldKey) {
      return this.previewData[externalFieldKey] || null;
    },
    shouldShowInlinePreview(externalFieldKey) {
      const p = this.previewData[externalFieldKey];
      if (!p) return false;
      if (p.error) return true;
      // Hide when no values or when source == result (no change)
      if (p.resultValue == null && p.sourceValue == null) return false;
      return p.sourceValue != p.resultValue;
    },

    goBack() {
      this.$router.push(`/adapter/${this.connectionId}`);
    },

    openMappingSettings() {
      this.settingsName = this.mappingInfo?.name || "";
      this.showMappingSettings = true;
    },

    isImageValue(value) {
      if (!value) return false;
      // Handle arrays (Vue may pass the raw object from API)
      if (Array.isArray(value))
        return value.some((v) => typeof v === "string" && v.includes("http"));
      const str = String(value);
      // JSON array of URLs
      if (str.startsWith("[") && str.includes("http")) return true;
      // Single URL with image extension (even with query params like presigned URLs)
      if (/https?:\/\/\S+\.(jpg|jpeg|png|webp|gif|avif)(\?|$)/i.test(str))
        return true;
      // Presigned MinIO URL (contains /images/ path segment)
      if (/https?:\/\/\S+\/images\/\S+/i.test(str)) return true;
      return false;
    },
    parseImageUrls(value) {
      if (!value) return [];
      // Already an array
      if (Array.isArray(value))
        return value.filter((u) => typeof u === "string" && u.includes("http"));
      const str = String(value);
      // Try JSON parse
      try {
        const parsed = JSON.parse(str);
        if (Array.isArray(parsed))
          return parsed.filter(
            (u) => typeof u === "string" && u.includes("http"),
          );
      } catch {
        /* ignore */
      }
      // Single URL
      if (str.startsWith("http")) return [str];
      // Comma-separated
      return str
        .split(",")
        .map((s) => s.trim())
        .filter((s) => s.startsWith("http"));
    },
    async saveMappingSettings() {
      this.isSavingSettings = true;
      try {
        await this.updateCategoryMapping({
          connectionId: this.connectionId,
          mappingId: this.mappingId,
          data: { name: this.settingsName || null },
        });
        this.mappingInfo.name = this.settingsName || null;
        displaySonnerSuccess(this.$t("marketplace.fieldMappingPage.settingsSaved"));
        this.showMappingSettings = false;
      } catch (error) {
        displaySonnerError(
          error.response?.data?.message || this.$t("marketplace.fieldMappingPage.saveError"),
        );
      } finally {
        this.isSavingSettings = false;
      }
    },

    // Add a new field manually
    openAddFieldDialog() {
      this.newField = {
        externalFieldKey: "",
        externalFieldName: "",
        sourceType: "Static",
        staticValue: "",
      };
      this.showAddField = true;
    },

    cancelAddField() {
      this.showAddField = false;
    },

    addNewField() {
      if (!this.newField.externalFieldKey.trim()) {
        displaySonnerError({ message: this.$t("marketplace.fieldMappingPage.enterFieldKey") });
        return;
      }

      // Check that this key doesn't already exist
      const exists = this.fieldMappings.some(
        (fm) => fm.externalFieldKey === this.newField.externalFieldKey,
      );
      if (exists) {
        displaySonnerError({ message: this.$t("marketplace.fieldMappingPage.fieldKeyExists") });
        return;
      }

      // Add the new field
      const newMapping = {
        id: null, // Will be created on the server
        externalFieldKey: this.newField.externalFieldKey.trim(),
        externalFieldName:
          this.newField.externalFieldName.trim() ||
          this.newField.externalFieldKey.trim(),
        isRequired: false,
        isEnabled: true,
        sourceType: this.newField.sourceType,
        sourceField: null,
        sourceAttributeId: null,
        staticValue: this.newField.staticValue || null,
        isCustom: true, // Marker that this field was added manually
      };

      this.fieldMappings.push(newMapping);
      this.showAddField = false;
      this.hasChanges = true;
    },

    removeField(fieldMapping) {
      if (!fieldMapping.isCustom && fieldMapping.id) {
        displaySonnerError({
          message: this.$t("marketplace.fieldMappingPage.cannotDeleteAdapterField"),
        });
        return;
      }
      const index = this.fieldMappings.indexOf(fieldMapping);
      if (index > -1) {
        this.fieldMappings.splice(index, 1);
        this.hasChanges = true;
      }
    },
    exportMappings() {
      const data = this.fieldMappings.map((fm) => ({
        externalFieldKey: fm.externalFieldKey,
        externalFieldName: fm.externalFieldName,
        externalFieldType: fm.externalFieldType,
        isRequired: fm.isRequired,
        isEnabled: fm.isEnabled,
        sourceType: fm.sourceType,
        sourceField: fm.sourceField,
        sourceAttributeId: fm.sourceAttributeId,
        staticValue: fm.staticValue,
        modifiersJson:
          fm.parsedModifiers && fm.parsedModifiers.length
            ? JSON.stringify(fm.parsedModifiers)
            : null,
      }));
      const json = JSON.stringify(data, null, 2);
      const blob = new Blob([json], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `mapping-${this.mappingId}.json`;
      a.click();
      URL.revokeObjectURL(url);
    },
    triggerImport() {
      this.$refs.importFileInput.click();
    },
    importMappings(event) {
      const file = event.target.files?.[0];
      if (!file) return;
      const reader = new FileReader();
      reader.onload = (e) => {
        try {
          const imported = JSON.parse(e.target.result);
          if (!Array.isArray(imported)) {
            displaySonnerError({ message: this.$t("marketplace.fieldMappingPage.invalidFileFormat") });
            return;
          }
          let updated = 0;
          for (const item of imported) {
            const fm = this.fieldMappings.find(
              (f) => f.externalFieldKey === item.externalFieldKey,
            );
            if (!fm) continue;
            if (item.isEnabled !== undefined) fm.isEnabled = item.isEnabled;
            if (item.sourceType !== undefined) fm.sourceType = item.sourceType;
            if (item.sourceField !== undefined)
              fm.sourceField = item.sourceField;
            if (item.sourceAttributeId !== undefined)
              fm.sourceAttributeId = item.sourceAttributeId;
            if (item.staticValue !== undefined)
              fm.staticValue = item.staticValue;
            if (item.modifiersJson) {
              try {
                fm.parsedModifiers = JSON.parse(item.modifiersJson);
              } catch {
                /* ignore bad modifiers */
              }
            }
            updated++;
          }
          this.hasChanges = true;
          displaySonnerSuccess(this.$t("marketplace.fieldMappingPage.importedFieldsCount", { count: updated }));
        } catch {
          displaySonnerError({ message: this.$t("marketplace.fieldMappingPage.fileReadError") });
        }
      };
      reader.readAsText(file);
      event.target.value = "";
    },

  },
  mounted() {
    this.connectionId = this.$route.params.connId;
    this.mappingId = this.$route.params.mappingId;
    this.fetchData().then(() => {
      const qp = this.$route.query.previewProduct;
      if (qp) {
        this.onPreviewProductChange(qp);
      }
      this.updateAiPageContext();
    });
  },
  beforeUnmount() {
    if (this.previewProductSearchTimer) {
      clearTimeout(this.previewProductSearchTimer);
    }
    this.$store.commit("aiChat/clearPageContext");
  },
};
</script>

<template>
  <AppPageChrome fill-viewport>
    <PageSection
      :loading="isLoading"
      :loading-message="loadStatusText"
      class="!mb-0 flex min-h-0 flex-1 flex-col overflow-hidden"
    >
      <template v-if="!isLoading">
        <!-- Visual node graph editor (whole-category Blueprint) - the sole mapping editor.
             The preview-product selector lives in the canvas toolbar. -->
        <div
          v-if="connectionId && mappingId"
          class="relative min-h-0 w-full flex-1"
        >
          <MappingCanvas
            ref="mappingCanvas"
            :connection-id="connectionId"
            :mapping-id="mappingId"
            :field-mappings="fieldMappings"
            :external-field-defs="externalFieldDefs"
            :price-field-mappings="priceFieldMappings"
            :price-field-defs="priceFieldDefs"
            :available-sources="{ ...availableSources, storeFiles }"
            :preview-product-id="previewProductId"
            :preview-products="previewProducts"
            :preview-products-loading="previewProductsLoading"
            @preview-result="onGraphPreview"
            @select-preview="onPreviewProductChange"
            @clear-preview="onPreviewProductChange(null)"
            @search-preview="onPreviewProductSearch"
            @fields-synced="onFieldsSynced"
          />
        </div>


      </template>
    </PageSection>

    <!-- Preview summary -->
    <Sheet v-model:open="showPreviewSheet">
      <SheetContent
        side="right"
        inset
        resizable
        :show-close="false"
        class="flex w-full flex-col gap-0 p-0"
        :default-width="mappingSheetWidth"
        :storage-key="mappingSheetKey"
      >
        <SheetHeader variant="bar">
          <div class="flex min-h-8 items-center gap-2">
            <SheetTitle class="min-w-0 flex-1 truncate">
              {{ $t("marketplace.fieldMappingPage.previewTitle") }}
            </SheetTitle>
            <SheetHeaderClose />
          </div>
          <SheetDescription>
            {{ $t("marketplace.fieldMappingPage.previewSheetDescription") }}
          </SheetDescription>
        </SheetHeader>

        <div class="min-h-0 flex-1 overflow-y-auto px-4 py-4">
          <InsetGroup v-if="previewSummary.length > 0">
            <InsetGroupItem
              v-for="item in previewSummary"
              :key="item.externalFieldKey"
              as="div"
            >
              <p class="truncate">{{ item.fieldName }}</p>
              <div v-if="item.error" class="mt-1 text-xs text-destructive break-all whitespace-pre-wrap">
                {{ item.error }}
              </div>
              <div v-else-if="isImageValue(item.resultValue)" class="mt-2">
                <Tabs default-value="visual">
                  <TabsList class="h-6 p-0.5">
                    <TabsTrigger value="visual" class="text-[11px] h-5 px-2">
                      {{ $t("marketplace.fieldMappingPage.visualTab") }}
                    </TabsTrigger>
                    <TabsTrigger value="raw" class="text-[11px] h-5 px-2">
                      {{ $t("marketplace.fieldMappingPage.rawDataTab") }}
                    </TabsTrigger>
                  </TabsList>
                  <TabsContent value="visual" class="mt-2">
                    <div class="flex flex-wrap gap-2">
                      <div
                        v-for="(url, idx) in parseImageUrls(item.resultValue)"
                        :key="idx"
                        class="group relative h-24 w-24 overflow-hidden rounded-xl bg-muted ring-1 ring-inset ring-border/70"
                      >
                        <AppImage
                          :src="url"
                          :alt="String(idx + 1)"
                          spinner-size="sm"
                          class="absolute inset-0 size-full"
                          img-class="size-full object-cover"
                          loading="lazy"
                          referrerpolicy="no-referrer"
                        />
                        <div
                          class="pointer-events-none absolute inset-0 flex items-center justify-center text-[10px] text-muted-foreground"
                        >
                          {{ idx + 1 }}
                        </div>
                      </div>
                    </div>
                    <p class="text-muted-foreground mt-2 text-xs font-normal">
                      {{ $t("marketplace.fieldMappingPage.imagesCountHint", { count: parseImageUrls(item.resultValue).length }) }}
                    </p>
                  </TabsContent>
                  <TabsContent value="raw" class="mt-2 space-y-1 text-xs">
                    <div class="flex gap-2 min-w-0">
                      <span class="text-muted-foreground shrink-0">{{ $t("marketplace.fieldMappingPage.sourceAbbr") }}</span>
                      <span
                        class="text-muted-foreground font-mono break-all whitespace-pre-wrap"
                        >{{ item.sourceValue ?? "-" }}</span
                      >
                    </div>
                    <div class="flex gap-2 min-w-0">
                      <span class="text-muted-foreground shrink-0">{{ $t("marketplace.fieldMappingPage.resultAbbr") }}</span>
                      <span
                        class="font-medium font-mono break-all whitespace-pre-wrap"
                        >{{ item.resultValue ?? "-" }}</span
                      >
                    </div>
                  </TabsContent>
                </Tabs>
              </div>
              <div v-else class="mt-1 space-y-1 text-xs">
                <div class="flex gap-2 min-w-0">
                  <span class="text-muted-foreground shrink-0">{{ $t("marketplace.fieldMappingPage.sourceAbbr") }}</span>
                  <span
                    class="text-muted-foreground font-mono break-all whitespace-pre-wrap"
                    >{{ item.sourceValue ?? "-" }}</span
                  >
                </div>
                <div class="flex gap-2 min-w-0">
                  <span class="text-muted-foreground shrink-0">{{ $t("marketplace.fieldMappingPage.resultAbbr") }}</span>
                  <span
                    class="font-medium font-mono break-all whitespace-pre-wrap"
                    >{{ item.resultValue ?? "-" }}</span
                  >
                </div>
              </div>
            </InsetGroupItem>
          </InsetGroup>
          <EmptyState
            v-else
            size="sm"
            :framed="false"
            :title="$t('marketplace.fieldMappingPage.noActiveFieldsTitle')"
            :description="$t('marketplace.fieldMappingPage.noActiveFieldsDescription')"
          />
        </div>
      </SheetContent>
    </Sheet>

    <!-- Add new field dialog -->
    <!-- Mapping Settings Dialog -->
    <Dialog v-model:open="showMappingSettings">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>{{ $t("marketplace.fieldMappingPage.mappingSettingsTitle") }}</DialogTitle>
          <DialogDescription>{{ $t("marketplace.fieldMappingPage.mappingSettingsDescription") }}</DialogDescription>
        </DialogHeader>
        <FieldGroup class="gap-5 py-2">
          <SettingsFormField :label="$t('common.name')" label-for="mapping-settings-name">
            <Input
              id="mapping-settings-name"
              v-model="settingsName"
              :placeholder="$t('marketplace.fieldMappingPage.mappingNamePlaceholder')"
            />
          </SettingsFormField>
        </FieldGroup>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">{{ $t("common.cancel") }}</Button>
          </DialogClose>
          <Button @click="saveMappingSettings" :disabled="isSavingSettings">
            <Spinner v-if="isSavingSettings" class="mr-2" />
            {{ $t("common.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="showAddField">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t("marketplace.fieldMappingPage.addFieldTitle") }}</DialogTitle>
          <DialogDescription>
            {{ $t("marketplace.fieldMappingPage.addFieldDescription") }}
          </DialogDescription>
        </DialogHeader>

        <FieldGroup class="gap-5 py-2">
          <SettingsFormField :label="$t('marketplace.fieldMappingPage.fieldKeyLabel')" label-for="new-field-key">
            <Input
              id="new-field-key"
              v-model="newField.externalFieldKey"
              :placeholder="$t('marketplace.fieldMappingPage.fieldKeyPlaceholder')"
            />
          </SettingsFormField>
          <SettingsFormField :label="$t('marketplace.fieldMappingPage.nameOptionalLabel')" label-for="new-field-name">
            <Input
              id="new-field-name"
              v-model="newField.externalFieldName"
              :placeholder="$t('marketplace.fieldMappingPage.displayNamePlaceholder')"
            />
          </SettingsFormField>
          <SettingsFormField :label="$t('marketplace.fieldMappingPage.sourceTypeLabel')" label-for="new-field-source">
            <Select v-model="newField.sourceType">
              <SelectTrigger id="new-field-source">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Static">{{ $t("marketplace.fieldMappingPage.sourceType.staticValue") }}</SelectItem>
                <SelectItem value="ProductField">{{ $t("marketplace.fieldMappingPage.sourceType.productField") }}</SelectItem>
                <SelectItem value="CategoryAttribute"
                  >{{ $t("marketplace.fieldMappingPage.sourceType.categoryAttribute") }}</SelectItem
                >
                <SelectItem value="StoreAttribute">{{ $t("marketplace.fieldMappingPage.sourceType.storeAttribute") }}</SelectItem>
                <SelectItem value="GroupName">{{ $t("marketplace.fieldMappingPage.sourceType.groupName") }}</SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>
          <SettingsFormField
            v-if="newField.sourceType === 'Static'"
            :label="$t('marketplace.graph.inspector.valueLabel')"
            label-for="new-field-value"
          >
            <Input
              id="new-field-value"
              v-model="newField.staticValue"
              :placeholder="$t('marketplace.fieldMappingPage.fieldValuePlaceholder')"
            />
          </SettingsFormField>
        </FieldGroup>

        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">{{ $t("common.cancel") }}</Button>
          </DialogClose>
          <Button @click="addNewField">
            <Plus class="size-4 mr-2" />
            {{ $t("common.add") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </AppPageChrome>
</template>
