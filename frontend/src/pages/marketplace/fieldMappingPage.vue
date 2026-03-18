<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import SearchableSelect from "@/components/ui/searchable-select/SearchableSelect.vue";
import MultiSelectDropdown from "@/components/ui/multi-select/MultiSelectDropdown.vue";
import ModifierStack from "@/components/marketplace/ModifierStack.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import { setPageTitle } from "@/router";
import {
  ArrowLeft,
  Save,
  Loader2,
  AlertCircle,
  CheckCircle,
  Database,
  Tag,
  FileText,
  Type,
  Boxes,
  Plus,
  Trash2,
  Eye,
  Settings2,
  ArrowRight,
  Info,
  Download,
  Upload,
  X,
  EllipsisVertical,
  Search,
} from "lucide-vue-next";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import {
  Popover,
  PopoverTrigger,
  PopoverContent,
} from "@/components/ui/popover";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Label,
    Badge,
    Switch,
    SearchableSelect,
    MultiSelectDropdown,
    ModifierStack,
    ArrowLeft,
    Save,
    Loader2,
    AlertCircle,
    CheckCircle,
    Database,
    Tag,
    FileText,
    Type,
    Boxes,
    Plus,
    Trash2,
    Eye,
    Settings2,
    ArrowRight,
    Info,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
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
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Sheet,
    SheetContent,
    SheetHeader,
    SheetTitle,
    SheetDescription,
    Download,
    Upload,
    X,
    EllipsisVertical,
    Popover,
    PopoverTrigger,
    PopoverContent,
    Search,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
  },
  data() {
    return {
      connectionId: null,
      mappingId: null,
      fieldMappings: [],
      availableSources: {
        productFields: [],
        categoryAttributes: [],
        storeAttributes: [],
      },
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
      // Modifier sheet
      editingFieldMapping: null,
      showModifierSheet: false,
      // Preview summary sheet
      showPreviewSheet: false,
      // Mapping settings dialog
      showMappingSettings: false,
      settingsName: "",
      isSavingSettings: false,
      // Cache for lazily-loaded attribute options (keyed "category:<id>" or "store:<id>")
      _attributeOptionsCache: {},
      _attributeOptionsLoading: {},
      // Для добавления нового поля
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
        { label: "Маркетплейсы", to: "/marketplace" },
        { label: "Подключение", to: `/marketplace/${this.connectionId}` },
      ];
      if (this.mappingInfo) {
        items.push({ label: this.mappingInfo.externalCategoryName || "Категория" });
      }
      return items;
    },
    requiredFieldsMapped() {
      return this.fieldMappings.filter(
        (fm) => fm.isRequired && fm.isEnabled && this.hasValidSource(fm)
      ).length;
    },
    totalRequiredFields() {
      return this.fieldMappings.filter((fm) => fm.isRequired).length;
    },
    unmappedRequiredFields() {
      return this.fieldMappings.filter(
        (fm) => fm.isRequired && fm.isEnabled && !this.hasValidSource(fm)
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
            isChanged: sourceValue != resultValue && resultValue != null && !error,
          };
        });
    },
    sortedFieldMappings() {
      const required = this.fieldMappings.filter((fm) => fm.isRequired);
      const enabled = this.fieldMappings.filter((fm) => !fm.isRequired && fm.isEnabled);
      const disabled = this.fieldMappings.filter((fm) => !fm.isRequired && !fm.isEnabled);
      return [...required, ...enabled, ...disabled];
    },
    editingSourceFieldOptions() {
      const fm = this.editingFieldMapping;
      if (!fm) return [];
      if (fm.sourceType === "CategoryAttribute" && fm.sourceAttributeId) {
        return this._attributeOptionsCache[`category:${fm.sourceAttributeId}`] || [];
      } else if (fm.sourceType === "StoreAttribute" && fm.sourceAttributeId) {
        return this._attributeOptionsCache[`store:${fm.sourceAttributeId}`] || [];
      }
      return [];
    },
  },
  methods: {
    ...mapActions([
      "getFieldMappings",
      "saveFieldMappings",
      "toggleFieldMapping",
      "updateFieldMapping",
      "getAvailableSources",
      "getExternalFields",
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
      ]);
      // fetchPreviewProducts needs localCategoryId from mappingInfo, so run after fetchFieldMappings
      await this.fetchPreviewProducts();
      this.isLoading = false;

      // Load descriptions separately — non-blocking, non-critical
      this.fetchExternalFieldDescriptions();
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
              ? (() => { try { return JSON.parse(fm.externalFieldOptions); } catch { return []; } })()
              : [],
            parsedModifiers: fm.modifiersJson
              ? (() => { try { return JSON.parse(fm.modifiersJson); } catch { return []; } })()
              : [],
          }));
          this.mappingInfo = {
            name: data.name,
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
            categoryAttributes: [],
            storeAttributes: [],
          });
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async fetchExternalFieldDescriptions() {
      await this.getExternalFields({
        mappingId: this.mappingId,
        onSuccess: (response) => {
          const fields = extractResponseData(response, []);
          const descMap = {};
          const aspectMap = {};
          for (const f of fields) {
            if (f.description) descMap[f.key] = f.description;
            if (f.isAspect) aspectMap[f.key] = true;
          }
          this.externalFieldDescriptions = descMap;
          this.aspectFields = aspectMap;
        },
        onError: () => { /* non-critical */ },
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
      return (fm.parsedOptions || []).filter((o) => !selected.includes(o.value));
    },

    filterOptions(fm, options) {
      if (!fm._optionSearch) return options.slice(0, 100);
      const q = fm._optionSearch.toLowerCase();
      return options.filter(o => o.label.toLowerCase().includes(q)).slice(0, 200);
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
          return !!fieldMapping.sourceField
            && (this.availableSources.productFields || []).some(f => f.key === fieldMapping.sourceField);
        case "CategoryAttribute":
          return !!fieldMapping.sourceAttributeId
            && (this.availableSources.categoryAttributes || []).some(a => a.id === fieldMapping.sourceAttributeId);
        case "StoreAttribute":
          return !!fieldMapping.sourceAttributeId
            && (this.availableSources.storeAttributes || []).some(a => a.id === fieldMapping.sourceAttributeId);
        case "Static":
          return !!fieldMapping.staticValue;
        case "GroupName":
          return true; // GroupName не требует дополнительных параметров
        default:
          return false;
      }
    },

    isUnmappedRequired(fieldMapping) {
      return fieldMapping.isRequired && fieldMapping.isEnabled && !this.hasValidSource(fieldMapping);
    },

    isUnmappedOptional(fieldMapping) {
      return !fieldMapping.isRequired && fieldMapping.isEnabled && !this.hasValidSource(fieldMapping);
    },

    getSourceTypeIcon(sourceType) {
      switch (sourceType) {
        case "ProductField":
          return Database;
        case "CategoryAttribute":
          return Tag;
        case "StoreAttribute":
          return FileText;
        case "Static":
          return Type;
        case "GroupName":
          return Boxes;
        default:
          return Database;
      }
    },

    getSourceTypeLabel(sourceType) {
      switch (sourceType) {
        case "ProductField":
          return "Поле товара";
        case "CategoryAttribute":
          return "Атрибут категории";
        case "StoreAttribute":
          return "Атрибут магазина";
        case "Static":
          return "Статическое значение";
        case "GroupName":
          return "Название группы";
        default:
          return sourceType;
      }
    },

    getCurrentSourceLabel(fieldMapping) {
      switch (fieldMapping.sourceType) {
        case "ProductField":
          const pf = this.availableSources.productFields?.find(
            (f) => f.key === fieldMapping.sourceField
          );
          return pf?.displayName || fieldMapping.sourceField || "Не выбрано";
        case "CategoryAttribute":
          const ca = this.availableSources.categoryAttributes?.find(
            (a) => a.id === fieldMapping.sourceAttributeId
          );
          return ca?.name || "Не выбрано";
        case "StoreAttribute":
          const sa = this.availableSources.storeAttributes?.find(
            (a) => a.id === fieldMapping.sourceAttributeId
          );
          return sa?.name || "Не выбрано";
        case "Static":
          return fieldMapping.staticValue || "Не задано";
        case "GroupName":
          return "Название группы товаров";
        default:
          return "Не настроено";
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
        modifiersJson: fm.parsedModifiers && fm.parsedModifiers.length > 0
          ? JSON.stringify(fm.parsedModifiers)
          : null,
      }));

      await this.saveFieldMappings({
        mappingId: this.mappingId,
        fieldMappings: mappingsToSave,
        onSuccess: () => {
          displaySonnerSuccess("Маппинги сохранены");
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
      if (this._attributeOptionsCache[cacheKey] || this._attributeOptionsLoading[cacheKey]) return;

      this._attributeOptionsLoading[cacheKey] = true;
      this.getAttributeOptions({
        type,
        id,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          let parsed = [];
          if (data.options) {
            try { parsed = JSON.parse(data.options); } catch {}
          }
          this._attributeOptionsCache = { ...this._attributeOptionsCache, [cacheKey]: parsed };
          this._attributeOptionsLoading[cacheKey] = false;
        },
        onError: () => {
          this._attributeOptionsLoading[cacheKey] = false;
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
            label: `${p.name || p.sku || "Без названия"} (ID: ${p.id})`,
          }));
        },
        onError: () => {},
      });
    },

    onPreviewProductChange(productId) {
      this.previewProductId = productId ? parseInt(productId) : null;
      const query = { ...this.$route.query };
      if (this.previewProductId) {
        query.previewProduct = String(this.previewProductId);
        this.fetchPreview();
      } else {
        delete query.previewProduct;
        this.previewData = {};
      }
      this.$router.replace({ query });
    },

    async fetchPreview() {
      if (!this.previewProductId) return;
      this.isLoadingPreview = true;
      // Send current in-memory field mappings so preview works without saving
      const currentMappings = this.fieldMappings.map((fm) => ({
        externalFieldKey: fm.externalFieldKey,
        sourceType: fm.sourceType,
        sourceField: fm.sourceField,
        sourceAttributeId: fm.sourceAttributeId,
        staticValue: fm.staticValue,
        modifiersJson: fm.parsedModifiers?.length
          ? JSON.stringify(fm.parsedModifiers)
          : null,
        isEnabled: fm.isEnabled,
      }));
      await this.previewFieldMappings({
        mappingId: this.mappingId,
        productId: this.previewProductId,
        fieldMappings: currentMappings,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          const previews = data.previews || [];
          this.previewData = {};
          for (const p of previews) {
            this.previewData[p.externalFieldKey] = p;
          }
        },
        onError: () => {
          this.previewData = {};
        },
      });
      this.isLoadingPreview = false;
    },

    debouncedPreview() {
      if (this.previewDebounceTimer) clearTimeout(this.previewDebounceTimer);
      if (!this.previewProductId) return;
      this.previewDebounceTimer = setTimeout(() => {
        this.fetchPreview();
      }, 300);
    },

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
      this.$router.push(`/marketplace/${this.connectionId}`);
    },

    openMappingSettings() {
      this.settingsName = this.mappingInfo?.name || "";
      this.showMappingSettings = true;
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
        displaySonnerSuccess("Настройки сохранены");
        this.showMappingSettings = false;
      } catch (error) {
        displaySonnerError(error.response?.data?.message || "Ошибка сохранения");
      } finally {
        this.isSavingSettings = false;
      }
    },

    // Добавление нового поля вручную
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
        displaySonnerError({ message: "Введите ключ поля" });
        return;
      }

      // Проверяем что такого ключа ещё нет
      const exists = this.fieldMappings.some(
        (fm) => fm.externalFieldKey === this.newField.externalFieldKey
      );
      if (exists) {
        displaySonnerError({ message: "Поле с таким ключом уже существует" });
        return;
      }

      // Добавляем новое поле
      const newMapping = {
        id: null, // Будет создан на сервере
        externalFieldKey: this.newField.externalFieldKey.trim(),
        externalFieldName: this.newField.externalFieldName.trim() || this.newField.externalFieldKey.trim(),
        isRequired: false,
        isEnabled: true,
        sourceType: this.newField.sourceType,
        sourceField: null,
        sourceAttributeId: null,
        staticValue: this.newField.staticValue || null,
        isCustom: true, // Маркер что это поле добавлено вручную
      };

      this.fieldMappings.push(newMapping);
      this.showAddField = false;
      this.hasChanges = true;
    },

    removeField(fieldMapping) {
      if (!fieldMapping.isCustom && fieldMapping.id) {
        displaySonnerError({ message: "Нельзя удалить поле маркетплейса. Можно только отключить." });
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
        modifiersJson: fm.parsedModifiers && fm.parsedModifiers.length
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
            displaySonnerError({ message: "Неверный формат файла" });
            return;
          }
          let updated = 0;
          for (const item of imported) {
            const fm = this.fieldMappings.find(
              (f) => f.externalFieldKey === item.externalFieldKey
            );
            if (!fm) continue;
            if (item.isEnabled !== undefined) fm.isEnabled = item.isEnabled;
            if (item.sourceType !== undefined) fm.sourceType = item.sourceType;
            if (item.sourceField !== undefined) fm.sourceField = item.sourceField;
            if (item.sourceAttributeId !== undefined) fm.sourceAttributeId = item.sourceAttributeId;
            if (item.staticValue !== undefined) fm.staticValue = item.staticValue;
            if (item.modifiersJson) {
              try {
                fm.parsedModifiers = JSON.parse(item.modifiersJson);
              } catch { /* ignore bad modifiers */ }
            }
            updated++;
          }
          this.hasChanges = true;
          displaySonnerSuccess(`Импортировано: ${updated} полей`);
        } catch {
          displaySonnerError({ message: "Ошибка чтения файла" });
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
    });
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <div class="flex items-center justify-between mb-4">
      <div class="flex items-center gap-4">
        <Button variant="ghost" size="icon" @click="goBack">
          <ArrowLeft class="size-4" />
        </Button>
        <div>
          <h1 class="text-lg font-semibold">Маппинг полей</h1>
          <p v-if="mappingInfo" class="text-sm text-muted-foreground">
            {{ mappingInfo.localCategoryName }} → {{ mappingInfo.externalCategoryName }}
          </p>
        </div>
      </div>
      <div class="flex items-center gap-2">
        <input ref="importFileInput" type="file" accept=".json" class="hidden"
               @change="importMappings" />
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button variant="outline" size="icon">
              <EllipsisVertical class="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem @click="openMappingSettings" class="cursor-pointer">
              <Settings2 class="size-4 mr-2" />
              Настройки
            </DropdownMenuItem>
            <DropdownMenuItem @click="openAddFieldDialog" class="cursor-pointer">
              <Plus class="size-4 mr-2" />
              Добавить поле
            </DropdownMenuItem>
            <DropdownMenuItem @click="exportMappings" class="cursor-pointer">
              <Download class="size-4 mr-2" />
              Экспорт маппинга
            </DropdownMenuItem>
            <DropdownMenuItem @click="triggerImport" class="cursor-pointer">
              <Upload class="size-4 mr-2" />
              Импорт маппинга из файла
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <Button @click="handleSave" :disabled="isSaving || !hasChanges">
          <Loader2 v-if="isSaving" class="size-4 mr-2 animate-spin" />
          <Save v-else class="size-4 mr-2" />
          Сохранить
        </Button>
      </div>
    </div>

    <div v-if="isLoading" class="flex justify-center py-12">
      <Loader2 class="size-8 animate-spin text-muted-foreground" />
    </div>

    <template v-else>
      <Card class="mb-4">
        <CardHeader class="">
          <CardTitle class="text-base flex items-center gap-2">
            <component
              :is="requiredFieldsMapped === totalRequiredFields ? CheckCircle : AlertCircle"
              :class="[
                'size-5',
                requiredFieldsMapped === totalRequiredFields
                  ? 'text-success'
                  : 'text-warning',
              ]"
            />
            Обязательные поля: {{ requiredFieldsMapped }} / {{ totalRequiredFields }}
          </CardTitle>
          <CardDescription>
            Настройте источники данных для полей маркетплейса
          </CardDescription>
        </CardHeader>
      </Card>

      <!-- Предупреждение о незамапленных обязательных полях -->
      <div
        v-if="hasUnmappedRequired"
        class="mb-4 p-4 bg-destructive/10 border border-destructive/30 rounded-lg flex items-start gap-3"
      >
        <AlertCircle class="size-5 text-destructive shrink-0 mt-0.5" />
        <div>
          <p class="font-medium text-destructive">
            Не настроены обязательные поля
          </p>
          <p class="text-sm text-destructive/80 mt-1">
            Экспорт товаров не будет работать, пока не настроены следующие поля:
            <span class="font-medium">
              {{ unmappedRequiredFields.map(f => f.externalFieldName || f.externalFieldKey).join(', ') }}
            </span>
          </p>
        </div>
      </div>

      <!-- Preview product selector -->
      <div class="mb-4 flex items-center gap-3">
        <div class="flex items-center gap-2 text-sm text-muted-foreground">
          <Eye class="size-4" />
          <span>Предпросмотр:</span>
        </div>
        <SearchableSelect
          class="w-[500px]"
          :options="previewProducts"
          :model-value="previewProductId ? String(previewProductId) : ''"
          @update:modelValue="onPreviewProductChange"
          placeholder="Выберите товар для предпросмотра..."
        />
        <Loader2 v-if="isLoadingPreview" class="size-4 animate-spin text-muted-foreground" />
        <Button v-if="previewProductId && !isLoadingPreview" variant="outline" size="sm"
                @click="showPreviewSheet = true">
          <Eye class="size-4 mr-2" /> Сводка
        </Button>
      </div>

      <div class="border rounded-lg h-full w-full grow mb-4 overflow-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-[50px]">Вкл</TableHead>
              <TableHead class="w-[250px]">Поле маркетплейса</TableHead>
              <TableHead class="w-[180px]">Тип источника</TableHead>
              <TableHead>Источник данных</TableHead>
              <TableHead class="w-[100px]">Статус</TableHead>
              <!-- preview column removed — shown inline under data source -->
              <TableHead class="w-[80px]"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <template v-for="fm in sortedFieldMappings" :key="fm.id">
            <TableRow
              :class="[
                { 'opacity-50': !fm.isEnabled },
                { 'bg-destructive/5 border-l-2 border-l-destructive': isUnmappedRequired(fm) },
                { 'bg-warning/5 border-l-2 border-l-warning': isUnmappedOptional(fm) },
                { '!border-b-0': previewProductId && shouldShowInlinePreview(fm.externalFieldKey) }
              ]"
            >
              <TableCell>
                <Switch
                  v-if="!fm.isRequired"
                  :model-value="fm.isEnabled"
                  @update:model-value="handleToggleField(fm)"
                />
              </TableCell>
              <TableCell>
                <div class="flex flex-col">
                  <span class="font-medium flex items-center gap-2">
                    {{ fm.externalFieldName || fm.externalFieldKey }}
                    <span v-if="fm.isRequired" class="text-destructive">*</span>
                    <Badge v-if="fm.isCustom" variant="outline" class="text-xs py-0 h-5">
                      вручную
                    </Badge>
                    <Badge v-if="aspectFields[fm.externalFieldKey]" variant="secondary" class="text-xs py-0 h-5">
                      аспектный
                    </Badge>
                    <TooltipProvider v-if="externalFieldDescriptions[fm.externalFieldKey]">
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <Info class="size-3.5 text-muted-foreground cursor-help shrink-0" />
                        </TooltipTrigger>
                        <TooltipContent side="top" class="max-w-xs">
                          <p>{{ externalFieldDescriptions[fm.externalFieldKey] }}</p>
                        </TooltipContent>
                      </Tooltip>
                    </TooltipProvider>
                  </span>
                  <span class="text-xs text-muted-foreground">
                    {{ fm.externalFieldKey }}
                  </span>
                </div>
              </TableCell>
              <TableCell>
                <Select
                  :model-value="fm.sourceType"
                  @update:modelValue="(v) => onSourceTypeChange(fm, v)"
                  :disabled="!fm.isEnabled"
                >
                  <SelectTrigger class="w-full">
                    <SelectValue>
                      <span class="flex items-center gap-2">
                        <component :is="getSourceTypeIcon(fm.sourceType)" class="size-4" />
                        {{ getSourceTypeLabel(fm.sourceType) }}
                      </span>
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="ProductField">
                      <span class="flex items-center gap-2">
                        <Database class="size-4" />
                        Поле товара
                      </span>
                    </SelectItem>
                    <SelectItem value="CategoryAttribute">
                      <span class="flex items-center gap-2">
                        <Tag class="size-4" />
                        Атрибут категории
                      </span>
                    </SelectItem>
                    <SelectItem value="StoreAttribute">
                      <span class="flex items-center gap-2">
                        <FileText class="size-4" />
                        Атрибут магазина
                      </span>
                    </SelectItem>
                    <SelectItem value="Static">
                      <span class="flex items-center gap-2">
                        <Type class="size-4" />
                        Статическое значение
                      </span>
                    </SelectItem>
                    <SelectItem value="GroupName">
                      <span class="flex items-center gap-2">
                        <Boxes class="size-4" />
                        Название группы
                      </span>
                    </SelectItem>
                  </SelectContent>
                </Select>
              </TableCell>
              <TableCell>
                <template v-if="fm.sourceType === 'ProductField'">
                  <Select
                    :model-value="fm.sourceField"
                    @update:modelValue="(v) => onSourceFieldChange(fm, v)"
                    :disabled="!fm.isEnabled"
                  >
                    <SelectTrigger class="w-full">
                      <SelectValue placeholder="Выберите поле" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem
                        v-for="field in availableSources.productFields"
                        :key="field.key"
                        :value="field.key"
                      >
                        {{ field.displayName }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </template>

                <template v-else-if="fm.sourceType === 'CategoryAttribute'">
                  <Select
                    :model-value="fm.sourceAttributeId?.toString()"
                    @update:modelValue="(v) => onSourceFieldChange(fm, v)"
                    :disabled="!fm.isEnabled"
                  >
                    <SelectTrigger class="w-full">
                      <SelectValue placeholder="Выберите атрибут" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem
                        v-for="attr in availableSources.categoryAttributes"
                        :key="attr.id"
                        :value="attr.id.toString()"
                      >
                        {{ attr.name }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </template>

                <template v-else-if="fm.sourceType === 'StoreAttribute'">
                  <Select
                    :model-value="fm.sourceAttributeId?.toString()"
                    @update:modelValue="(v) => onSourceFieldChange(fm, v)"
                    :disabled="!fm.isEnabled"
                  >
                    <SelectTrigger class="w-full">
                      <SelectValue placeholder="Выберите атрибут" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem
                        v-for="attr in availableSources.storeAttributes"
                        :key="attr.id"
                        :value="attr.id.toString()"
                      >
                        {{ attr.name }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </template>

                <template v-else-if="fm.sourceType === 'Static'">
                  <!-- Raw text mode: plain input for any field type -->
                  <template v-if="fm.rawTextMode">
                    <div class="flex items-center gap-2">
                      <Input
                        class="flex-1"
                        :model-value="fm.staticValue"
                        @update:modelValue="(v) => onStaticValueChange(fm, v)"
                        placeholder="Введите значение"
                        :disabled="!fm.isEnabled"
                      />
                      <button
                        type="button"
                        class="text-xs text-muted-foreground hover:text-foreground underline cursor-pointer whitespace-nowrap"
                        @click="toggleRawTextMode(fm)"
                      >Выбрать из списка</button>
                    </div>
                  </template>

                  <!-- Select field with SearchableSelect -->
                  <template v-else-if="fm.externalFieldType === 'select' && fm.parsedOptions?.length">
                    <div class="flex items-center gap-2">
                      <SearchableSelect
                        class="flex-1"
                        :options="fm.parsedOptions"
                        :model-value="fm.staticValue || ''"
                        @update:modelValue="(v) => onStaticValueChange(fm, v)"
                        placeholder="Выберите значение"
                        :disabled="!fm.isEnabled"
                      />
                      <button
                        type="button"
                        class="text-xs text-muted-foreground hover:text-foreground underline cursor-pointer whitespace-nowrap"
                        @click="toggleRawTextMode(fm)"
                      >Вручную</button>
                    </div>
                  </template>

                  <!-- Multiselect field with chip/tag UI -->
                  <template v-else-if="fm.externalFieldType === 'multiselect'">
                    <div class="flex items-center gap-2">
                      <!-- С опциями маркетплейса: chips + Plus popover -->
                      <template v-if="fm.parsedOptions?.length">
                        <div class="flex-1 flex flex-wrap items-center gap-1 border border-input rounded-md px-2 py-1.5 min-h-9 bg-transparent dark:bg-input/30 shadow-xs">
                          <Badge v-for="(v, i) in getStaticValues(fm)" :key="i" variant="secondary"
                                 class="flex items-center gap-1 pl-2 pr-1 py-0.5">
                            <span class="text-xs">{{ v.label }}</span>
                            <button type="button" @click="removeStaticValue(fm, i)"
                                    class="hover:bg-muted rounded-sm p-0.5" :disabled="!fm.isEnabled">
                              <X class="size-3" />
                            </button>
                          </Badge>
                          <span v-if="!getStaticValues(fm).length" class="text-sm text-muted-foreground">Нет значений</span>
                        </div>
                        <Popover @update:open="open => { if (!open) fm._optionSearch = '' }">
                          <PopoverTrigger as-child>
                            <Button variant="outline" size="icon" class="shrink-0" :disabled="!fm.isEnabled || !getAvailableOptions(fm).length">
                              <Plus class="size-4" />
                            </Button>
                          </PopoverTrigger>
                          <PopoverContent align="start" class="w-[250px] p-0 max-h-72 overflow-hidden flex flex-col">
                            <div v-if="getAvailableOptions(fm).length > 5" class="p-2 border-b">
                              <div class="relative">
                                <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
                                <input
                                  type="text"
                                  v-model="fm._optionSearch"
                                  placeholder="Поиск..."
                                  class="w-full pl-8 pr-3 py-1.5 text-sm border border-input rounded-md bg-background focus:outline-none focus:ring-2 focus:ring-ring"
                                  @click.stop
                                />
                              </div>
                            </div>
                            <div class="overflow-auto flex-1 p-1">
                              <template v-for="filteredOpts in [filterOptions(fm, getAvailableOptions(fm))]" :key="0">
                                <div v-for="option in filteredOpts"
                                     :key="option.value"
                                     class="flex items-center gap-2 px-2 py-1.5 rounded-sm cursor-pointer hover:bg-accent hover:text-accent-foreground transition-colors"
                                     @click="addStaticValue(fm, option.value)">
                                  <span class="text-sm truncate" :title="option.label">{{ option.label }}</span>
                                </div>
                                <div v-if="!filteredOpts.length" class="p-3 text-sm text-muted-foreground text-center">
                                  Ничего не найдено
                                </div>
                              </template>
                            </div>
                          </PopoverContent>
                        </Popover>
                      </template>
                      <!-- Без опций: chips внутри поля ввода -->
                      <template v-else>
                        <div class="flex-1 flex flex-wrap items-center gap-1 border border-input rounded-md px-2 py-1.5 min-h-9 bg-transparent dark:bg-input/30 shadow-xs focus-within:ring-1 focus-within:ring-ring">
                          <Badge v-for="(v, i) in getStaticValues(fm)" :key="i" variant="secondary"
                                 class="flex items-center gap-1 pl-2 pr-1 py-0.5">
                            <span class="text-xs">{{ v.label }}</span>
                            <button type="button" @click="removeStaticValue(fm, i)"
                                    class="hover:bg-muted rounded-sm p-0.5" :disabled="!fm.isEnabled">
                              <X class="size-3" />
                            </button>
                          </Badge>
                          <input
                            class="flex-1 min-w-[120px] bg-transparent text-sm outline-none placeholder:text-muted-foreground"
                            :value="fm._newStaticValue"
                            @input="e => fm._newStaticValue = e.target.value"
                            @keydown.enter.prevent="addFreeformStaticValue(fm)"
                            placeholder="Введите и нажмите Enter"
                            :disabled="!fm.isEnabled"
                          />
                        </div>
                        <Button variant="outline" size="icon" class="shrink-0"
                                @click="addFreeformStaticValue(fm)"
                                :disabled="!fm.isEnabled || !fm._newStaticValue?.trim()">
                          <Plus class="size-4" />
                        </Button>
                      </template>
                      <button type="button"
                              class="text-xs text-muted-foreground hover:text-foreground underline cursor-pointer whitespace-nowrap"
                              @click="toggleRawTextMode(fm)">Вручную</button>
                    </div>
                  </template>

                  <!-- Boolean with raw text toggle -->
                  <template v-else-if="fm.externalFieldType === 'boolean'">
                    <div class="flex items-center gap-2">
                      <Switch
                        :model-value="fm.staticValue === 'true'"
                        @update:modelValue="(v) => onStaticValueChange(fm, v ? 'true' : 'false')"
                        :disabled="!fm.isEnabled"
                      />
                      <button
                        type="button"
                        class="text-xs text-muted-foreground hover:text-foreground underline cursor-pointer whitespace-nowrap"
                        @click="toggleRawTextMode(fm)"
                      >Вручную</button>
                    </div>
                  </template>

                  <!-- Number field -->
                  <template v-else-if="fm.externalFieldType === 'number'">
                    <Input
                      type="number"
                      :model-value="fm.staticValue"
                      @update:modelValue="(v) => onStaticValueChange(fm, v)"
                      placeholder="Введите число"
                      :disabled="!fm.isEnabled"
                    />
                  </template>

                  <!-- String / default -->
                  <template v-else>
                    <Input
                      :model-value="fm.staticValue"
                      @update:modelValue="(v) => onStaticValueChange(fm, v)"
                      placeholder="Введите значение"
                      :disabled="!fm.isEnabled"
                    />
                  </template>
                </template>

                <template v-else-if="fm.sourceType === 'GroupName'">
                  <span class="text-sm text-muted-foreground">
                    Значение берётся из названия группы товара
                  </span>
                </template>

              </TableCell>
              <TableCell>
                <Badge
                  :variant="hasValidSource(fm) && fm.isEnabled ? 'default' : isUnmappedRequired(fm) ? 'destructive' : 'outline'"
                  :class="isUnmappedOptional(fm) ? 'border-warning text-warning-foreground' : ''"
                  :title="!fm.isEnabled ? 'Поле отключено' : !hasValidSource(fm) ? 'Выберите источник данных' : getCurrentSourceLabel(fm)"
                >
                  <component
                    :is="hasValidSource(fm) && fm.isEnabled ? CheckCircle : AlertCircle"
                    :class="['size-3 mr-1', isUnmappedOptional(fm) ? 'text-warning' : '']"
                  />
                  {{ hasValidSource(fm) && fm.isEnabled ? "Настроено" : isUnmappedRequired(fm) ? "Обязательно!" : isUnmappedOptional(fm) ? "Не заполнено" : "Не настроено" }}
                </Badge>
              </TableCell>
              <!-- preview moved inline under data source cell -->
              <TableCell>
                <div class="flex items-center gap-1">
                  <Button
                    v-if="fm.isEnabled"
                    variant="ghost"
                    size="icon"
                    class="size-8 relative"
                    @click="openModifierSheet(fm)"
                    title="Настроить модификаторы"
                  >
                    <Settings2 class="size-4" />
                    <span
                      v-if="getModifierCount(fm) > 0"
                      class="absolute -top-0.5 -right-0.5 flex items-center justify-center size-4 rounded-full bg-primary text-primary-foreground text-[10px] font-medium"
                    >
                      {{ getModifierCount(fm) }}
                    </span>
                  </Button>
                  <Button
                    v-if="fm.isCustom"
                    variant="ghost"
                    size="icon"
                    class="size-8 text-destructive hover:text-destructive"
                    @click="removeField(fm)"
                    title="Удалить поле"
                  >
                    <Trash2 class="size-4" />
                  </Button>
                </div>
              </TableCell>
            </TableRow>
            <!-- Preview sub-row -->
            <TableRow
              v-if="previewProductId && shouldShowInlinePreview(fm.externalFieldKey)"
              :key="fm.id + '-preview'"
              class="border-t-0 !border-b hover:bg-transparent"
            >
              <TableCell :colspan="6" class="pt-0 pb-1.5 px-4">
                <div class="flex items-center gap-1.5 text-xs leading-none min-w-0">
                  <Eye class="size-3 shrink-0 text-muted-foreground" />
                  <template v-if="getPreviewForField(fm.externalFieldKey).error">
                    <span class="text-destructive truncate">{{ getPreviewForField(fm.externalFieldKey).error }}</span>
                  </template>
                  <template v-else>
                    <span class="text-muted-foreground truncate max-w-[250px]" :title="getPreviewForField(fm.externalFieldKey).sourceValue">{{ getPreviewForField(fm.externalFieldKey).sourceValue || '—' }}</span>
                    <span class="text-muted-foreground shrink-0">→</span>
                    <span class="font-medium truncate max-w-[250px]" :title="getPreviewForField(fm.externalFieldKey).resultValue">{{ getPreviewForField(fm.externalFieldKey).resultValue || '—' }}</span>
                  </template>
                </div>
              </TableCell>
            </TableRow>
            </template>

            <TableRow v-if="fieldMappings.length === 0">
              <TableCell :colspan="6" class="text-center py-8 text-muted-foreground">
                Нет полей для маппинга. Нажмите "Добавить поле" чтобы создать вручную.
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </template>

    <!-- Боковое окно модификаторов -->
    <Sheet v-model:open="showModifierSheet">
      <SheetContent side="right" class="overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>Модификаторы</SheetTitle>
          <SheetDescription v-if="editingFieldMapping">
            {{ editingFieldMapping.externalFieldName || editingFieldMapping.externalFieldKey }}
          </SheetDescription>
        </SheetHeader>

        <template v-if="editingFieldMapping">
          <!-- Source info -->
          <div class="mt-4 p-3 rounded-md bg-muted/50 text-sm">
            <div class="text-muted-foreground text-xs mb-1">Источник данных</div>
            <div class="flex items-center gap-2">
              <component :is="getSourceTypeIcon(editingFieldMapping.sourceType)" class="size-4 text-muted-foreground" />
              <span>{{ getSourceTypeLabel(editingFieldMapping.sourceType) }}:</span>
              <span class="font-medium">{{ getCurrentSourceLabel(editingFieldMapping) }}</span>
            </div>
          </div>

          <!-- Modifier chain -->
          <div class="mt-6">
            <div class="text-sm font-medium mb-3">Цепочка модификаторов</div>
            <ModifierStack
              :modifiers="editingFieldMapping.parsedModifiers || []"
              :external-field-type="editingFieldMapping.externalFieldType"
              :external-field-options="editingFieldMapping.parsedOptions || []"
              :source-field-options="editingSourceFieldOptions"
              :available-sources="availableSources"
              :disabled="!editingFieldMapping.isEnabled"
              @update:modifiers="(v) => onModifiersChange(editingFieldMapping, v)"
            />
            <p v-if="!editingFieldMapping.parsedModifiers?.length" class="text-xs text-muted-foreground mt-2">
              Модификаторы преобразуют значение перед отправкой на маркетплейс. Нажмите "+ Модификатор" чтобы добавить.
            </p>
          </div>

          <!-- Preview in sheet -->
          <div v-if="previewProductId && getPreviewForField(editingFieldMapping.externalFieldKey)" class="mt-6">
            <div class="text-sm font-medium mb-2">Предпросмотр</div>
            <div class="p-3 rounded-md border text-sm space-y-2">
              <div v-if="getPreviewForField(editingFieldMapping.externalFieldKey).error" class="text-destructive">
                {{ getPreviewForField(editingFieldMapping.externalFieldKey).error }}
              </div>
              <template v-else>
                <div>
                  <span class="text-xs text-muted-foreground">Исходное значение:</span>
                  <div class="font-mono text-xs mt-0.5 break-all whitespace-pre-wrap">
                    {{ getPreviewForField(editingFieldMapping.externalFieldKey).sourceValue || '—' }}
                  </div>
                </div>
                <div class="flex items-center gap-2 text-muted-foreground">
                  <span>→</span>
                </div>
                <div>
                  <span class="text-xs text-muted-foreground">Результат:</span>
                  <div class="font-mono text-xs mt-0.5 font-medium break-all whitespace-pre-wrap">
                    {{ getPreviewForField(editingFieldMapping.externalFieldKey).resultValue || '—' }}
                  </div>
                </div>
              </template>
            </div>
          </div>
        </template>
      </SheetContent>
    </Sheet>

    <!-- Сводка предпросмотра -->
    <Sheet v-model:open="showPreviewSheet">
      <SheetContent side="right" class="overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Предпросмотр</SheetTitle>
          <SheetDescription>Сводка всех полей. Изменённые значения выделены цветом.</SheetDescription>
        </SheetHeader>

        <div class="mt-4 space-y-2">
          <div
            v-for="item in previewSummary"
            :key="item.externalFieldKey"
            class="rounded-md border p-3 text-sm"
            :class="{
              'bg-destructive/10 border-l-2 border-l-destructive': item.error,
              'bg-warning/10 border-l-2 border-l-warning': item.isChanged,
            }"
          >
            <div class="font-medium text-xs text-muted-foreground mb-1">{{ item.fieldName }}</div>
            <div v-if="item.error" class="text-destructive text-xs break-all whitespace-pre-wrap">{{ item.error }}</div>
            <div v-else class="space-y-1 text-xs">
              <div class="flex gap-2 min-w-0">
                <span class="text-muted-foreground shrink-0">Исх:</span>
                <span class="text-muted-foreground font-mono break-all whitespace-pre-wrap">{{ item.sourceValue ?? '—' }}</span>
              </div>
              <div class="flex gap-2 min-w-0">
                <span class="text-muted-foreground shrink-0">Рез:</span>
                <span class="font-medium font-mono break-all whitespace-pre-wrap">{{ item.resultValue ?? '—' }}</span>
              </div>
            </div>
          </div>
          <div v-if="previewSummary.length === 0" class="text-center text-muted-foreground py-8 text-sm">
            Нет активных полей
          </div>
        </div>
      </SheetContent>
    </Sheet>

    <!-- Диалог добавления нового поля -->
    <!-- Mapping Settings Dialog -->
    <Dialog v-model:open="showMappingSettings">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Настройки маппинга</DialogTitle>
        </DialogHeader>
        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label>Название</Label>
            <Input
              v-model="settingsName"
              placeholder="Название маппинга"
            />
          </div>
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button @click="saveMappingSettings" :disabled="isSavingSettings">
            <Loader2 v-if="isSavingSettings" class="size-4 mr-2 animate-spin" />
            Сохранить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="showAddField">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Добавить поле</DialogTitle>
          <DialogDescription>
            Добавьте поле с произвольным ключом для маркетплейса
          </DialogDescription>
        </DialogHeader>

        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label>Ключ поля *</Label>
            <Input
              v-model="newField.externalFieldKey"
              placeholder="например: custom_field"
            />
          </div>
          <div class="space-y-2">
            <Label>Название (опционально)</Label>
            <Input
              v-model="newField.externalFieldName"
              placeholder="Отображаемое название"
            />
          </div>
          <div class="space-y-2">
            <Label>Тип источника</Label>
            <Select v-model="newField.sourceType">
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Static">Статическое значение</SelectItem>
                <SelectItem value="ProductField">Поле товара</SelectItem>
                <SelectItem value="CategoryAttribute">Атрибут категории</SelectItem>
                <SelectItem value="StoreAttribute">Атрибут магазина</SelectItem>
                <SelectItem value="GroupName">Название группы</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div v-if="newField.sourceType === 'Static'" class="space-y-2">
            <Label>Значение</Label>
            <Input
              v-model="newField.staticValue"
              placeholder="Значение поля"
            />
          </div>
        </div>

        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button @click="addNewField">
            <Plus class="size-4 mr-2" />
            Добавить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </Page>
</template>
