<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import MappingCanvas from "@/components/marketplace/graph/MappingCanvas.vue";
import { mergeProductFieldSources } from "@/components/marketplace/graph/graphModel.js";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { setPageTitle } from "@/router";
import {
  ArrowLeft,
  Download,
  LayoutGrid,
  Loader2,
  MoreVertical,
  Plus,
  RefreshCw,
  Upload,
} from "lucide-vue-next";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
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
import { EmptyState } from "@/components/ui/empty-state";
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
  SheetHeader,
  SheetTitle,
  SheetDescription,
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
    Page,
    PageSection,
    MappingCanvas,
    EmptyState,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Label,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    ArrowLeft,
    Loader2,
    Plus,
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
    SheetTitle,
    SheetDescription,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    MoreVertical,
    LayoutGrid,
    RefreshCw,
    Download,
    Upload,
  },
  data() {
    return {
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
      isSaving: false,
      hasChanges: false,
      mappingInfo: null,
      // Preview
      previewProductId: null,
      previewProducts: [],
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
      showAutoArrangeConfirm: false,
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
    breadcrumbs() {
      const items = [
        { label: this.$t("marketplace.fieldMappingPage.adapters"), to: "/adapter" },
        {
          label: this.mappingInfo?.connectionName || this.$t("marketplace.fieldMappingPage.connection"),
          to: `/adapter/${this.connectionId}`,
        },
        {
          label: this.$t("marketplace.fieldMappingPage.mappings"),
          to: `/adapter/${this.connectionId}?tab=mappings`,
        },
      ];
      if (this.mappingInfo) {
        items.push({
          label: this.mappingInfo.externalCategoryName || this.$t("marketplace.fieldMappingPage.category"),
        });
      }
      return items;
    },
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
      "updateCategoryMapping",
      "getAttributeOptions",
    ]),

    async fetchData() {
      this.isLoading = true;
      await Promise.all([
        this.fetchFieldMappings(),
        this.fetchAvailableSources(),
        this.fetchStoreFiles(),
        this.fetchExternalFieldDescriptions(),
        this.fetchPriceFieldDescriptions(),
      ]);
      // fetchPreviewProducts needs localCategoryId from mappingInfo, so run after fetchFieldMappings
      await this.fetchPreviewProducts();
      this.isLoading = false;
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

          setPageTitle(data.externalCategoryName || data.localCategoryName);
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
        this.storeFiles = []; // shared storage is optional — don't block the editor
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
              options: f.options || [],
              description: f.description || null,
              unit: f.unit || null,
              minValue: f.minValue ?? null,
              maxValue: f.maxValue ?? null,
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
              options: f.options || [],
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

    async fetchPreviewProducts() {
      await this.getAllProducts({
        searchQuery: "",
        startIndex: 0,
        pageSize: 500,
        categoryId: this.mappingInfo?.localCategoryId || null,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          this.previewProducts = (data.items || []).map((p) => ({
            value: String(p.id),
            label: `${p.name || p.sku || this.$t("marketplace.fieldMappingPage.untitled")} (ID: ${p.id})`,
          }));
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

    onSyncAdapterFields() {
      this.$refs.mappingCanvas?.syncAdapterFieldSchema?.();
    },

    onExportMapping() {
      this.$refs.mappingCanvas?.exportMappingFile?.();
    },

    onImportMapping() {
      this.$refs.mappingCanvas?.triggerImportFile?.();
    },

    confirmAutoArrange() {
      this.showAutoArrangeConfirm = false;
      this.$refs.mappingCanvas?.autoArrange?.();
    },

    onPreviewProductChange(productId) {
      this.previewProductId = productId ? parseInt(productId) : null;
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
    this.$store.commit("aiChat/clearPageContext");
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />

    <div class="mb-4 flex items-center gap-3">
      <Breadcrumbs :items="breadcrumbs" class="mb-0 min-w-0 flex-1" />

      <DropdownMenu v-if="connectionId && mappingId && !isLoading">
        <DropdownMenuTrigger as-child>
          <Button
            variant="ghost"
            size="icon"
            class="size-8 shrink-0"
            :aria-label="$t('marketplace.fieldMappingPage.moreActions')"
          >
            <MoreVertical class="size-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" class="w-56">
          <DropdownMenuItem @select="showAutoArrangeConfirm = true">
            <LayoutGrid class="size-4 mr-2" />
            {{ $t("marketplace.fieldMappingPage.autoArrange") }}
          </DropdownMenuItem>
          <DropdownMenuItem @select="onSyncAdapterFields">
            <RefreshCw class="size-4 mr-2" />
            {{ $t("marketplace.fieldMappingPage.checkFieldsUpToDate") }}
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem @select="onExportMapping">
            <Download class="size-4 mr-2" />
            {{ $t("marketplace.fieldMappingPage.export") }}
          </DropdownMenuItem>
          <DropdownMenuItem @select="onImportMapping">
            <Upload class="size-4 mr-2" />
            {{ $t("marketplace.fieldMappingPage.import") }}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>

    <AlertDialog
      :open="showAutoArrangeConfirm"
      @update:open="showAutoArrangeConfirm = $event"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t("marketplace.fieldMappingPage.rearrangeBlocksTitle") }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t("marketplace.fieldMappingPage.rearrangeBlocksDescription") }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            class="bg-primary text-primary-foreground hover:bg-primary/90"
            @click="confirmAutoArrange"
          >
            {{ $t("marketplace.fieldMappingPage.rearrange") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <PageSection :loading="isLoading">
      <template v-if="!isLoading">
        <!-- Visual node graph editor (whole-category Blueprint) — the sole mapping editor.
             The preview-product selector lives in the canvas toolbar. -->
        <div
          v-if="connectionId && mappingId"
          class="relative mb-4 h-[calc(100vh-150px)] min-h-[480px] w-full"
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
            @preview-result="onGraphPreview"
            @select-preview="onPreviewProductChange"
            @clear-preview="onPreviewProductChange(null)"
            @fields-synced="onFieldsSynced"
          />
        </div>


      </template>
    </PageSection>

    <!-- Preview summary -->
    <Sheet v-model:open="showPreviewSheet">
      <SheetContent side="right" class="overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>{{ $t("marketplace.fieldMappingPage.previewTitle") }}</SheetTitle>
          <SheetDescription>{{ $t("marketplace.fieldMappingPage.previewSheetDescription") }}</SheetDescription>
        </SheetHeader>

        <div class="mt-4 space-y-2">
          <div
            v-for="item in previewSummary"
            :key="item.externalFieldKey"
            class="rounded-md border p-3 text-sm"
            :class="{
              'bg-destructive/10': item.error,
              'bg-warning/10': item.isChanged && !item.error,
            }"
          >
            <div v-if="item.error">
              <div class="font-medium text-xs text-muted-foreground mb-1">
                {{ item.fieldName }}
              </div>
              <div
                class="text-destructive text-xs break-all whitespace-pre-wrap"
              >
                {{ item.error }}
              </div>
            </div>
            <div v-else-if="isImageValue(item.resultValue)">
              <Tabs default-value="visual">
                <div class="flex items-center justify-between mb-1">
                  <span class="font-medium text-xs text-muted-foreground">{{
                    item.fieldName
                  }}</span>
                  <TabsList class="h-6 p-0.5">
                    <TabsTrigger value="visual" class="text-[11px] h-5 px-2"
                      >{{ $t("marketplace.fieldMappingPage.visualTab") }}</TabsTrigger
                    >
                    <TabsTrigger value="raw" class="text-[11px] h-5 px-2"
                      >{{ $t("marketplace.fieldMappingPage.rawDataTab") }}</TabsTrigger
                    >
                  </TabsList>
                </div>
                <TabsContent value="visual" class="mt-0">
                  <div class="flex flex-wrap gap-2">
                    <div
                      v-for="(url, idx) in parseImageUrls(item.resultValue)"
                      :key="idx"
                      class="h-24 w-24 rounded-md border overflow-hidden bg-muted relative group"
                    >
                      <img
                        :src="url"
                        class="h-full w-full object-cover"
                        loading="lazy"
                        referrerpolicy="no-referrer"
                        @error="$event.target.classList.add('opacity-0')"
                      />
                      <!-- Fallback: shows image index when img fails -->
                      <div
                        class="absolute inset-0 flex items-center justify-center text-[10px] text-muted-foreground pointer-events-none"
                      >
                        {{ idx + 1 }}
                      </div>
                    </div>
                  </div>
                  <p class="text-[10px] text-muted-foreground mt-2">
                    {{ $t("marketplace.fieldMappingPage.imagesCountHint", { count: parseImageUrls(item.resultValue).length }) }}
                  </p>
                </TabsContent>
                <TabsContent value="raw" class="mt-0 space-y-1 text-xs">
                  <div class="flex gap-2 min-w-0">
                    <span class="text-muted-foreground shrink-0">{{ $t("marketplace.fieldMappingPage.sourceAbbr") }}</span>
                    <span
                      class="text-muted-foreground font-mono break-all whitespace-pre-wrap"
                      >{{ item.sourceValue ?? "—" }}</span
                    >
                  </div>
                  <div class="flex gap-2 min-w-0">
                    <span class="text-muted-foreground shrink-0">{{ $t("marketplace.fieldMappingPage.resultAbbr") }}</span>
                    <span
                      class="font-medium font-mono break-all whitespace-pre-wrap"
                      >{{ item.resultValue ?? "—" }}</span
                    >
                  </div>
                </TabsContent>
              </Tabs>
            </div>
            <div v-else>
              <div class="font-medium text-xs text-muted-foreground mb-1">
                {{ item.fieldName }}
              </div>
              <div class="space-y-1 text-xs">
                <div class="flex gap-2 min-w-0">
                  <span class="text-muted-foreground shrink-0">{{ $t("marketplace.fieldMappingPage.sourceAbbr") }}</span>
                  <span
                    class="text-muted-foreground font-mono break-all whitespace-pre-wrap"
                    >{{ item.sourceValue ?? "—" }}</span
                  >
                </div>
                <div class="flex gap-2 min-w-0">
                  <span class="text-muted-foreground shrink-0">{{ $t("marketplace.fieldMappingPage.resultAbbr") }}</span>
                  <span
                    class="font-medium font-mono break-all whitespace-pre-wrap"
                    >{{ item.resultValue ?? "—" }}</span
                  >
                </div>
              </div>
            </div>
          </div>
          <EmptyState
            v-if="previewSummary.length === 0"
            size="sm"
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
        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label>{{ $t("common.name") }}</Label>
            <Input v-model="settingsName" :placeholder="$t('marketplace.fieldMappingPage.mappingNamePlaceholder')" />
          </div>
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">{{ $t("common.cancel") }}</Button>
          </DialogClose>
          <Button @click="saveMappingSettings" :disabled="isSavingSettings">
            <Loader2 v-if="isSavingSettings" class="size-4 mr-2 animate-spin" />
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

        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label>{{ $t("marketplace.fieldMappingPage.fieldKeyLabel") }}</Label>
            <Input
              v-model="newField.externalFieldKey"
              :placeholder="$t('marketplace.fieldMappingPage.fieldKeyPlaceholder')"
            />
          </div>
          <div class="space-y-2">
            <Label>{{ $t("marketplace.fieldMappingPage.nameOptionalLabel") }}</Label>
            <Input
              v-model="newField.externalFieldName"
              :placeholder="$t('marketplace.fieldMappingPage.displayNamePlaceholder')"
            />
          </div>
          <div class="space-y-2">
            <Label>{{ $t("marketplace.fieldMappingPage.sourceTypeLabel") }}</Label>
            <Select v-model="newField.sourceType">
              <SelectTrigger>
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
          </div>
          <div v-if="newField.sourceType === 'Static'" class="space-y-2">
            <Label>{{ $t("marketplace.graph.inspector.valueLabel") }}</Label>
            <Input v-model="newField.staticValue" :placeholder="$t('marketplace.fieldMappingPage.fieldValuePlaceholder')" />
          </div>
        </div>

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
  </Page>
</template>
