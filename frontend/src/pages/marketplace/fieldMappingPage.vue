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
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
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
} from "lucide-vue-next";
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
  },
  methods: {
    ...mapActions([
      "getFieldMappings",
      "saveFieldMappings",
      "toggleFieldMapping",
      "updateFieldMapping",
      "getAvailableSources",
    ]),

    async fetchData() {
      this.isLoading = true;
      await Promise.all([
        this.fetchFieldMappings(),
        this.fetchAvailableSources(),
      ]);
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
            parsedOptions: fm.externalFieldOptions
              ? (() => { try { return JSON.parse(fm.externalFieldOptions); } catch { return []; } })()
              : [],
          }));
          this.mappingInfo = {
            localCategoryName: data.localCategoryName,
            externalCategoryId: data.externalCategoryId,
            externalCategoryName: data.externalCategoryName,
          };
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
    },

    onStaticValueChange(fieldMapping, value) {
      fieldMapping.staticValue = value != null ? String(value) : null;
      this.hasChanges = true;
    },

    onMultiselectChange(fieldMapping, optionValue, checked) {
      const current = (fieldMapping.staticValue || "")
        .split(",")
        .filter((v) => v !== "");
      if (checked) {
        if (!current.includes(optionValue)) current.push(optionValue);
      } else {
        const idx = current.indexOf(optionValue);
        if (idx !== -1) current.splice(idx, 1);
      }
      fieldMapping.staticValue = current.join(",");
      this.hasChanges = true;
    },

    getMultiselectLabels(fm) {
      if (!fm.staticValue) return [];
      const values = fm.staticValue.split(",").filter((v) => v !== "");
      return values.map((val) => {
        const opt = (fm.parsedOptions || []).find((o) => o.value === val);
        return opt ? opt.label : val;
      });
    },

    onMultiselectDropdownChange(fm, selectedLabels) {
      const values = selectedLabels.map((label) => {
        const opt = (fm.parsedOptions || []).find((o) => o.label === label);
        return opt ? opt.value : label;
      });
      fm.staticValue = values.join(",");
      this.hasChanges = true;
    },

    toggleRawTextMode(fm) {
      fm.rawTextMode = !fm.rawTextMode;
    },

    hasValidSource(fieldMapping) {
      switch (fieldMapping.sourceType) {
        case "ProductField":
          return !!fieldMapping.sourceField;
        case "CategoryAttribute":
        case "StoreAttribute":
          return !!fieldMapping.sourceAttributeId;
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

    goBack() {
      this.$router.push(`/marketplace/${this.connectionId}`);
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
  },
  mounted() {
    this.connectionId = this.$route.params.connId;
    this.mappingId = this.$route.params.mappingId;
    this.fetchData();
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
        <Button variant="outline" @click="openAddFieldDialog">
          <Plus class="size-4 mr-2" />
          Добавить поле
        </Button>
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
                  ? 'text-green-500'
                  : 'text-amber-500',
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

      <div class="border rounded-lg h-full w-full grow mb-4 overflow-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-[50px]">Вкл</TableHead>
              <TableHead class="w-[250px]">Поле маркетплейса</TableHead>
              <TableHead class="w-[180px]">Тип источника</TableHead>
              <TableHead>Источник данных</TableHead>
              <TableHead class="w-[100px]">Статус</TableHead>
              <TableHead class="w-[50px]"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow
              v-for="fm in fieldMappings"
              :key="fm.id"
              :class="[
                { 'opacity-50': !fm.isEnabled },
                { 'bg-destructive/5 border-l-2 border-l-destructive': isUnmappedRequired(fm) },
                { 'bg-amber-500/5 border-l-2 border-l-amber-500': isUnmappedOptional(fm) }
              ]"
            >
              <TableCell>
                <Switch
                  :model-value="fm.isEnabled"
                  @update:model-value="handleToggleField(fm)"
                  :disabled="fm.isRequired"
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

                  <!-- Multiselect field with MultiSelectDropdown -->
                  <template v-else-if="fm.externalFieldType === 'multiselect' && fm.parsedOptions?.length">
                    <div class="flex items-center gap-2">
                      <MultiSelectDropdown
                        class="flex-1"
                        :options="fm.parsedOptions.map(o => o.label)"
                        :model-value="getMultiselectLabels(fm)"
                        @update:modelValue="(v) => onMultiselectDropdownChange(fm, v)"
                        placeholder="Выберите значения"
                        :disabled="!fm.isEnabled"
                      />
                      <button
                        type="button"
                        class="text-xs text-muted-foreground hover:text-foreground underline cursor-pointer whitespace-nowrap"
                        @click="toggleRawTextMode(fm)"
                      >Вручную</button>
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
                  :class="isUnmappedOptional(fm) ? 'border-amber-500 text-amber-600' : ''"
                  :title="!fm.isEnabled ? 'Поле отключено' : !hasValidSource(fm) ? 'Выберите источник данных' : getCurrentSourceLabel(fm)"
                >
                  <component
                    :is="hasValidSource(fm) && fm.isEnabled ? CheckCircle : AlertCircle"
                    :class="['size-3 mr-1', isUnmappedOptional(fm) ? 'text-amber-500' : '']"
                  />
                  {{ hasValidSource(fm) && fm.isEnabled ? "Настроено" : isUnmappedRequired(fm) ? "Обязательно!" : isUnmappedOptional(fm) ? "Не заполнено" : "Не настроено" }}
                </Badge>
              </TableCell>
              <TableCell>
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
              </TableCell>
            </TableRow>

            <TableRow v-if="fieldMappings.length === 0">
              <TableCell colspan="6" class="text-center py-8 text-muted-foreground">
                Нет полей для маппинга. Нажмите "Добавить поле" чтобы создать вручную.
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </template>

    <!-- Диалог добавления нового поля -->
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
