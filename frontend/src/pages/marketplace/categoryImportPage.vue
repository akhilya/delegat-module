<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import ExternalCategoryBrowser from "@/components/marketplace/ExternalCategoryBrowser.vue";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import Switch from "@/components/ui/switch/Switch.vue";
import { getAttributeUnit } from "@/lib/attributeUnits";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { ArrowLeft, ChevronRight, Download, FolderTree } from "lucide-vue-next";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import AttributeGroupIcon from "@/components/products/AttributeGroupIcon.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { EmptyState } from "@/components/ui/empty-state";
import {
  PAGE_MARKETPLACE,
  setPageTitle,
  setBreadcrumbParentLabel,
} from "@/router";

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    SettingsFormField,
    SettingsFormSection,
    ExternalCategoryBrowser,
    InsetGroup,
    InsetGroupItem,
    Button,
    Input,
    Checkbox,
    RadioGroup,
    RadioGroupItem,
    Switch,
    Download,
    FolderTree,
    ArrowLeft,
    ChevronRight,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
    AttributeGroupIcon,
    EmptyState,
    SearchableSelect,
  },
  data() {
    return {
      connection: null,
      capabilities: null,
      localCategories: [],
      externalCategories: [],
      isLoading: false,

      importStage: "category",
      importMode: "new",
      importTargetCategoryId: null,
      importCategoryId: "",
      importCategoryName: "",
      importCategoryExternalName: "",
      importExternalFields: [],
      importSelectedFieldKeys: [],
      attributeDecisions: {},
      importFieldDetails: null,
      showImportFieldDetailsDialog: false,
      isLoadingImportFieldDetails: false,
      importFieldDetailsLoadVersion: 0,
      isLoadingFields: false,
      isImporting: false,

      externalCategorySearch: "",
      currentParentId: null,
      categoryPath: [],
      isLoadingCategories: false,
    };
  },
  computed: {
    connectionId() {
      return parseInt(this.$route.params.connId);
    },
    adapterLabel() {
      return (
        this.connection?.adapterKey ||
        this.$t("marketplace.categoryImportPage.adapterGenitive")
      );
    },
    importAttributeFields() {
      return this.importExternalFields.filter((f) => !f.isBaseField);
    },
    allAttributeFieldsSelected() {
      return (
        this.importAttributeFields.length > 0 &&
        this.importSelectedFieldKeys.length ===
          this.importAttributeFields.length
      );
    },
    importButtonDisabled() {
      if (this.isImporting) return true;
      if (!this.importCategoryId) return true;
      if (this.importMode === "existing" && !this.importTargetCategoryId)
        return true;
      if (this.hasUnresolvedDuplicateDecisions) return true;
      return false;
    },
    hasUnresolvedDuplicateDecisions() {
      if (this.importMode !== "existing") return false;
      return this.importAttributeFields.some(
        (field) =>
          this.importSelectedFieldKeys.includes(field.key) &&
          (field.potentialDuplicates || []).length > 0 &&
          !this.attributeDecisions[field.key],
      );
    },
    hasImportConfig() {
      return this.importExternalFields.length > 0 && !this.isLoadingFields;
    },
    localCategoryOptions() {
      return this.localCategories.map((c) => ({
        value: c.id,
        label: `${c.name} (${this.$t("marketplace.categoryImportPage.attrsAbbrCount", { count: c.attributesCount })})`,
      }));
    },
    canCreateCategory() {
      return !!this.capabilities?.capabilities?.createCategory;
    },
    isEmptyRemoteRoot() {
      return (
        !this.externalCategorySearch &&
        !this.currentParentId &&
        this.externalCategories.length === 0
      );
    },
  },
  methods: {
    ...mapActions([
      "getConnection",
      "getCapabilities",
      "getLocalCategories",
      "getExternalCategories",
      "getExternalCategoryFields",
      "importCategory",
    ]),

    async fetchInitialData() {
      this.isLoading = true;
      await Promise.all([
        this.fetchConnection(),
        this.fetchCapabilities(),
        this.fetchLocalCategories(),
      ]);
      await this.loadRootCategories();
      this.isLoading = false;
    },

    async fetchConnection() {
      await this.getConnection({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.connection = extractResponseData(response);
          setPageTitle(
            `${this.$t("marketplace.categoryImportPage.importShort")} · ${this.connection.name}`,
          );
          setBreadcrumbParentLabel(this.connection.name);
        },
        onError: (error) => {
          displaySonnerError(error);
          this.$router.push(PAGE_MARKETPLACE);
        },
      });
    },

    async fetchCapabilities() {
      await this.getCapabilities({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.capabilities = extractResponseData(response);
        },
        onError: () => {
          this.capabilities = null;
        },
      });
    },

    async fetchLocalCategories() {
      await this.getLocalCategories({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.localCategories = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async searchExternalCategories() {
      this.isLoadingCategories = true;
      await this.getExternalCategories({
        connectionId: this.connectionId,
        search: this.externalCategorySearch || undefined,
        parentId: this.externalCategorySearch
          ? undefined
          : this.currentParentId,
        onSuccess: (response) => {
          this.externalCategories = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingCategories = false;
    },

    async loadRootCategories() {
      this.externalCategorySearch = "";
      this.currentParentId = null;
      this.categoryPath = [];
      await this.searchExternalCategories();
    },

    async navigateToCategory(category) {
      if (!category.hasChildren) return;
      this.externalCategorySearch = "";
      this.currentParentId = category.id;
      this.categoryPath.push({ id: category.id, name: category.name });
      await this.searchExternalCategories();
    },

    async navigateToPathIndex(index) {
      if (index < 0) {
        await this.loadRootCategories();
        return;
      }
      this.externalCategorySearch = "";
      const targetCategory = this.categoryPath[index];
      this.currentParentId = targetCategory.id;
      this.categoryPath = this.categoryPath.slice(0, index + 1);
      await this.searchExternalCategories();
    },

    async selectExternalCategory(category) {
      if (category.hasChildren) {
        this.navigateToCategory(category);
        return;
      }
      this.importCategoryId = category.id;
      this.importCategoryExternalName = category.name;
      this.importStage = "attributes";
      await this.fetchExternalCategoryFields();
      this.$nextTick(() => this.$refs.importSettingsHeading?.focus());
    },

    async fetchExternalCategoryFields({ resetSelection = true } = {}) {
      if (!this.importCategoryId) return;

      const selectedKeys = [...this.importSelectedFieldKeys];
      this.isLoadingFields = true;
      this.importFieldDetailsLoadVersion++;
      this.importFieldDetails = null;
      this.showImportFieldDetailsDialog = false;
      this.isLoadingImportFieldDetails = false;
      this.importExternalFields = [];
      this.attributeDecisions = {};
      if (resetSelection) this.importSelectedFieldKeys = [];

      await this.getExternalCategoryFields({
        connectionId: this.connectionId,
        externalCategoryId: this.importCategoryId,
        localCategoryId:
          this.importMode === "existing" && this.importTargetCategoryId
            ? Number(this.importTargetCategoryId)
            : null,
        onSuccess: (response) => {
          this.importExternalFields = extractResponseData(response, []);
          this.attributeDecisions = this.defaultAttributeDecisions(
            this.importExternalFields,
          );
          this.importSelectedFieldKeys = resetSelection
            ? this.importAttributeFields.map((f) => f.key)
            : selectedKeys.filter((key) =>
                this.importAttributeFields.some((field) => field.key === key),
              );
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingFields = false;
    },

    async setImportMode(mode) {
      if (this.importMode === mode) return;
      this.importMode = mode;
      this.attributeDecisions = {};
      if (
        mode === "existing" &&
        this.importTargetCategoryId &&
        this.importCategoryId
      ) {
        await this.fetchExternalCategoryFields({ resetSelection: false });
      } else if (mode === "new") {
        this.importExternalFields = this.importExternalFields.map((field) => ({
          ...field,
          potentialDuplicates: [],
        }));
      }
    },

    async setImportTargetCategoryId(categoryId) {
      this.importTargetCategoryId = categoryId;
      this.attributeDecisions = {};
      if (
        this.importMode === "existing" &&
        categoryId &&
        this.importCategoryId
      ) {
        await this.fetchExternalCategoryFields({ resetSelection: false });
      }
    },

    defaultAttributeDecisions(fields) {
      return Object.fromEntries(
        fields.flatMap((field) => {
          const candidate = field.isBaseField
            ? null
            : field.potentialDuplicates?.[0];
          return candidate
            ? [
                [
                  field.key,
                  { action: "reuse", existingAttributeId: candidate.id },
                ],
              ]
            : [];
        }),
      );
    },

    attributeDecisionValue(fieldKey) {
      const decision = this.attributeDecisions[fieldKey];
      if (!decision) return null;
      return decision.action === "reuse"
        ? `reuse:${decision.existingAttributeId}`
        : decision.action;
    },

    setAttributeDecision(fieldKey, value) {
      if (!value) {
        delete this.attributeDecisions[fieldKey];
        return;
      }
      if (value.startsWith("reuse:")) {
        this.attributeDecisions[fieldKey] = {
          action: "reuse",
          existingAttributeId: Number(value.slice("reuse:".length)),
        };
      } else {
        this.attributeDecisions[fieldKey] = { action: value };
      }
    },

    duplicateCandidateCaption(candidate) {
      const parts = [this.getImportTypeLabel(candidate.type)];
      if (candidate.unitCode)
        parts.push(this.getImportUnitLabel(candidate.unitCode));
      return parts.join(" · ");
    },

    changeAdapterCategory() {
      this.importStage = "category";
      this.$nextTick(() => {
        this.$refs.adapterCategoryBrowser?.$el?.querySelector("input")?.focus();
      });
    },

    toggleFieldSelection(key) {
      const index = this.importSelectedFieldKeys.indexOf(key);
      if (index === -1) {
        this.importSelectedFieldKeys.push(key);
      } else {
        this.importSelectedFieldKeys.splice(index, 1);
      }
    },

    toggleAllFields() {
      if (this.allAttributeFieldsSelected) {
        this.importSelectedFieldKeys = [];
      } else {
        this.importSelectedFieldKeys = this.importAttributeFields.map(
          (f) => f.key,
        );
      }
    },

    getImportTypeLabel(type) {
      const typeKeys = {
        Text: "text",
        Number: "number",
        Boolean: "boolean",
        Select: "select",
        MultiSelect: "multiSelect",
        Barcode: "barcode",
      };
      return this.$t(
        `marketplace.categoryImportPage.attributeTypes.${typeKeys[type] || "unknown"}`,
      );
    },

    getImportUnitLabel(unitCode) {
      const unit = getAttributeUnit(unitCode);
      return unit
        ? this.$t(`components.attributeUnitSelect.units.${unit.code}`)
        : unitCode;
    },

    getImportFieldCaption(field) {
      const parts = [];
      if (field.unitCode) {
        parts.push(
          this.$t("marketplace.categoryImportPage.unitCaption", {
            unit: this.getImportUnitLabel(field.unitCode),
          }),
        );
      }
      parts.push(this.getImportTypeLabel(field.importType));
      if (field.optionsCount > 0) {
        parts.push(
          this.$t("marketplace.categoryImportPage.optionsAbbrCount", {
            count: field.optionsCount,
          }),
        );
      }
      return parts.join(" · ");
    },

    async openImportFieldDetails(field) {
      const version = ++this.importFieldDetailsLoadVersion;
      const externalCategoryId = this.importCategoryId;
      this.importFieldDetails = { ...field };
      this.showImportFieldDetailsDialog = true;
      this.isLoadingImportFieldDetails = field.optionsCount > 0;

      if (field.optionsCount <= 0) return;

      await this.getExternalCategoryFields({
        connectionId: this.connectionId,
        externalCategoryId,
        fieldKey: field.key,
        onSuccess: (response) => {
          if (
            version !== this.importFieldDetailsLoadVersion ||
            !this.showImportFieldDetailsDialog
          )
            return;
          const details = extractResponseData(response, [])[0];
          if (details) this.importFieldDetails = { ...field, ...details };
        },
        onError: (error) => {
          if (
            version === this.importFieldDetailsLoadVersion &&
            this.showImportFieldDetailsDialog
          )
            displaySonnerError(error);
        },
      });

      if (version === this.importFieldDetailsLoadVersion)
        this.isLoadingImportFieldDetails = false;
    },

    handleImportFieldDetailsDialogOpenChange(open) {
      this.showImportFieldDetailsDialog = open;
      if (!open) {
        this.importFieldDetailsLoadVersion++;
        this.isLoadingImportFieldDetails = false;
      }
    },

    formatImportOptions(options) {
      return options
        .map((option) =>
          option.value === option.label
            ? option.label
            : `${option.label} (${option.value})`,
        )
        .join("\n");
    },

    getImportFieldValue(value) {
      return value === null || value === undefined || value === ""
        ? "—"
        : String(value);
    },

    async handleImportCategory() {
      if (!this.importCategoryId) {
        displaySonnerError(
          this.$t("marketplace.categoryImportPage.selectAdapterCategory"),
        );
        return;
      }

      if (!this.hasImportConfig) {
        displaySonnerError(
          this.$t("marketplace.categoryImportPage.selectLeafCategory"),
        );
        return;
      }

      if (this.importMode === "existing" && !this.importTargetCategoryId) {
        displaySonnerError(
          this.$t("marketplace.categoryImportPage.selectExistingCategory"),
        );
        return;
      }

      if (this.hasUnresolvedDuplicateDecisions) {
        displaySonnerError(
          this.$t("marketplace.categoryImportPage.resolveDuplicateChoices"),
        );
        return;
      }

      this.isImporting = true;
      await this.importCategory({
        connectionId: this.connectionId,
        data: {
          externalCategoryId: this.importCategoryId,
          externalCategoryName: this.importCategoryExternalName || null,
          localCategoryName:
            this.importMode === "new" ? this.importCategoryName || null : null,
          localCategoryId:
            this.importMode === "existing"
              ? parseInt(this.importTargetCategoryId)
              : null,
          selectedFieldKeys: this.importSelectedFieldKeys,
          attributeDecisions: this.importAttributeFields
            .filter(
              (field) =>
                this.importSelectedFieldKeys.includes(field.key) &&
                (field.potentialDuplicates || []).length > 0 &&
                this.attributeDecisions[field.key],
            )
            .map((field) => ({
              fieldKey: field.key,
              ...this.attributeDecisions[field.key],
            })),
        },
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          if (result.alreadyMapped) {
            displaySonnerSuccess(
              this.$t("marketplace.categoryImportPage.alreadyMapped"),
            );
          } else if (result.queued) {
            displaySonnerSuccess(
              this.$t("marketplace.categoryImportPage.categoryQueued"),
            );
          } else {
            displaySonnerSuccess(
              result.message ||
                this.$t("marketplace.categoryImportPage.categoryImported"),
            );
          }
          this.$router.push(`/adapter/${this.connectionId}?tab=mappings`);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isImporting = false;
    },
  },
  mounted() {
    this.fetchInitialData();
  },
};
</script>

<template>
  <AppPageChrome fill-viewport>
    <PageSection
      :loading="isLoading"
      class="!mb-0 flex min-h-0 flex-1 flex-col"
    >
      <div
        v-if="connection"
        class="flex min-h-0 w-full max-w-xl flex-1 flex-col"
      >
        <SettingsFormSection
          v-if="importStage === 'category'"
          class="!mt-0 !max-w-none flex min-h-0 flex-1 flex-col overflow-hidden"
          content-class="min-h-0 flex-1"
          :title="$t('marketplace.categoryImportPage.adapterCategory')"
        >
          <ExternalCategoryBrowser
            ref="adapterCategoryBrowser"
            v-model:search="externalCategorySearch"
            class="min-h-0 flex-1"
            :categories="externalCategories"
            :path="categoryPath"
            :loading="isLoadingCategories"
            :selected-id="importCategoryId"
            :search-placeholder="
              $t('marketplace.categoryImportPage.searchByNamePlaceholder')
            "
            :search-aria-label="
              $t('marketplace.categoryImportPage.findCategories')
            "
            :empty-title="
              $t('marketplace.categoryImportPage.categoriesNotFoundTitle')
            "
            :empty-description="
              isEmptyRemoteRoot && canCreateCategory
                ? $t(
                    'marketplace.categoryImportPage.categoriesNotFoundCreateHint',
                    { adapter: adapterLabel },
                  )
                : $t(
                    'marketplace.categoryImportPage.categoriesNotFoundDescription',
                  )
            "
            @search="searchExternalCategories"
            @select="selectExternalCategory"
            @navigate="navigateToCategory"
            @path-index="navigateToPathIndex"
          >
            <template #empty-action>
              <Button
                v-if="isEmptyRemoteRoot && canCreateCategory"
                @click="
                  $router.push(`/adapter/${connectionId}/categories/create`)
                "
              >
                <FolderTree class="mr-2 size-4" />
                {{
                  $t("marketplace.categoryImportPage.createFromLocalCta", {
                    adapter: adapterLabel,
                  })
                }}
              </Button>
            </template>
          </ExternalCategoryBrowser>
        </SettingsFormSection>

        <div v-else class="flex min-h-0 flex-1 flex-col gap-5">
          <h2 ref="importSettingsHeading" tabindex="-1" class="sr-only">
            {{ $t("marketplace.categoryImportPage.adapterCategory") }}:
            {{ importCategoryExternalName }}
          </h2>

          <div
            v-if="isLoadingFields"
            class="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 text-muted-foreground"
          >
            <Spinner size="2xl" />
            <span class="text-sm">{{
              $t("marketplace.categoryImportPage.loadingCategoryFields")
            }}</span>
          </div>

          <EmptyState
            v-else-if="!hasImportConfig"
            class="w-full flex-1 py-12"
            :framed="false"
            :title="$t('marketplace.categoryImportPage.noCategoryFields')"
          >
            <template #icon>
              <FolderTree />
            </template>
          </EmptyState>

          <template v-else>
            <SettingsFormSection
              class="!mt-0 !max-w-none shrink-0"
              content-class="gap-3"
              :title="$t('marketplace.categoryImportPage.whereToImportTitle')"
            >
              <p id="category-import-target-label" class="sr-only">
                {{ $t("marketplace.categoryImportPage.whereToImportTitle") }}
              </p>
              <RadioGroup
                :model-value="importMode"
                aria-labelledby="category-import-target-label"
                orientation="horizontal"
                class="flex flex-wrap gap-x-5 gap-y-1"
                @update:model-value="setImportMode($event)"
              >
                <div class="flex min-h-9 items-center gap-2 text-sm">
                  <RadioGroupItem id="category-import-mode-new" value="new" />
                  <label for="category-import-mode-new" class="cursor-pointer">
                    {{ $t("marketplace.categoryImportPage.createNewCategory") }}
                  </label>
                </div>
                <div class="flex min-h-9 items-center gap-2 text-sm">
                  <RadioGroupItem
                    id="category-import-mode-existing"
                    value="existing"
                  />
                  <label
                    for="category-import-mode-existing"
                    class="cursor-pointer"
                  >
                    {{
                      $t("marketplace.categoryImportPage.intoExistingCategory")
                    }}
                  </label>
                </div>
              </RadioGroup>
              <SettingsFormField
                v-if="importMode === 'new'"
                class="!gap-1.5 sm:max-w-xl"
                :label="$t('marketplace.categoryImportPage.categoryNameLabel')"
              >
                <Input
                  v-model="importCategoryName"
                  :placeholder="
                    $t('marketplace.categoryImportPage.categoryNamePlaceholder')
                  "
                />
              </SettingsFormField>
              <SettingsFormField
                v-else
                class="!gap-1.5 sm:max-w-xl"
                :label="$t('marketplace.categoryImportPage.localCategoryLabel')"
              >
                <SearchableSelect
                  :model-value="importTargetCategoryId"
                  :options="localCategoryOptions"
                  :placeholder="
                    $t(
                      'marketplace.categoryImportPage.selectCategoryPlaceholder',
                    )
                  "
                  :search-placeholder="
                    $t(
                      'marketplace.categoryImportPage.searchCategoryPlaceholder',
                    )
                  "
                  :empty-text="
                    $t('marketplace.categoryImportPage.categoriesNotFoundTitle')
                  "
                  @update:model-value="setImportTargetCategoryId($event)"
                />
              </SettingsFormField>
            </SettingsFormSection>

            <SettingsFormSection
              class="!mt-0 !max-w-none flex min-h-0 flex-1 flex-col"
              content-class="min-h-0 flex-1 gap-3"
              :title="$t('marketplace.categoryImportPage.attributesTitle')"
            >
              <template #actions>
                <Button
                  v-if="importAttributeFields.length > 0"
                  variant="ghost"
                  size="sm"
                  class="shrink-0"
                  @click="toggleAllFields"
                >
                  {{
                    allAttributeFieldsSelected
                      ? $t("marketplace.categoryImportPage.deselectAll")
                      : $t("marketplace.categoryImportPage.selectAll")
                  }}
                </Button>
              </template>

              <div
                v-if="importAttributeFields.length === 0"
                class="flex min-h-0 flex-1 items-center justify-center px-4 text-center text-sm text-muted-foreground"
              >
                {{
                  $t("marketplace.categoryImportPage.noAdditionalAttributes")
                }}
              </div>

              <TooltipProvider v-else :delay-duration="200">
                <InsetGroup scrollable class="flex-1">
                  <InsetGroupItem
                    v-for="(field, fieldIndex) in importAttributeFields"
                    :key="field.key"
                    as="div"
                    class="attribute-row gap-1 px-2 py-1.5"
                  >
                    <div class="flex w-full min-w-0 items-center gap-1">
                      <label
                        class="flex size-9 shrink-0 cursor-pointer items-center justify-center"
                        :aria-label="
                          $t('marketplace.categoryImportPage.selectField', {
                            name: field.importName || field.name,
                          })
                        "
                      >
                        <Checkbox
                          :model-value="
                            importSelectedFieldKeys.includes(field.key)
                          "
                          :aria-label="
                            $t('marketplace.categoryImportPage.selectField', {
                              name: field.importName || field.name,
                            })
                          "
                          @update:model-value="toggleFieldSelection(field.key)"
                        />
                      </label>
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <Button
                            type="button"
                            variant="ghost"
                            class="h-auto min-w-0 flex-1 justify-start gap-2 px-2 py-2 text-left font-normal hover:bg-transparent"
                            @click="openImportFieldDetails(field)"
                          >
                            <span class="min-w-0 flex-1">
                              <span class="block truncate">
                                {{ field.importName || field.name }}
                                <span
                                  v-if="field.isRequired"
                                  class="text-destructive"
                                  :title="
                                    $t(
                                      'marketplace.categoryImportPage.requiredInMarketplace',
                                    )
                                  "
                                  aria-hidden="true"
                                  >*</span
                                >
                              </span>
                              <span
                                class="mt-0.5 block truncate text-xs font-normal text-muted-foreground"
                              >
                                {{ getImportFieldCaption(field) }}
                              </span>
                            </span>
                            <ChevronRight
                              class="size-4 text-muted-foreground/40"
                            />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent
                          v-if="field.description"
                          side="top"
                          class="max-w-sm whitespace-pre-wrap text-left"
                        >
                          {{ field.description }}
                        </TooltipContent>
                      </Tooltip>
                    </div>

                    <div
                      v-if="
                        importMode === 'existing' &&
                        importSelectedFieldKeys.includes(field.key) &&
                        (field.potentialDuplicates || []).length > 0
                      "
                      class="ml-9 flex flex-col gap-1 pb-1 pr-2 sm:ml-11"
                    >
                      <p class="px-2 pt-1 text-xs text-muted-foreground">
                        {{
                          $t("marketplace.categoryImportPage.similarAttribute")
                        }}
                      </p>
                      <RadioGroup
                        :model-value="attributeDecisionValue(field.key)"
                        :aria-label="`${field.importName || field.name}: ${$t('marketplace.categoryImportPage.similarAttribute')}`"
                        class="grid min-w-0 gap-0.5"
                        @update:model-value="
                          setAttributeDecision(field.key, $event)
                        "
                      >
                        <div
                          v-for="candidate in field.potentialDuplicates"
                          :key="candidate.id"
                          class="flex min-w-0 cursor-pointer items-start gap-2.5 rounded-lg px-2.5 py-2 transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] focus-within:ring-2 focus-within:ring-ring/40"
                          @click="
                            setAttributeDecision(
                              field.key,
                              `reuse:${candidate.id}`,
                            )
                          "
                          :class="
                            attributeDecisionValue(field.key) ===
                            `reuse:${candidate.id}`
                              ? 'bg-muted/70'
                              : 'hover:bg-muted/50'
                          "
                        >
                          <RadioGroupItem
                            :id="`duplicate-${fieldIndex}-${candidate.id}`"
                            :value="`reuse:${candidate.id}`"
                            :aria-label="candidate.name"
                            class="mt-0.5"
                          />
                          <label
                            :for="`duplicate-${fieldIndex}-${candidate.id}`"
                            class="min-w-0 flex-1 cursor-pointer"
                          >
                            <span class="block truncate text-sm font-medium">
                              {{ candidate.name }}
                            </span>
                            <span
                              class="mt-0.5 block text-xs text-muted-foreground"
                            >
                              {{ duplicateCandidateCaption(candidate) }}
                            </span>
                          </label>
                        </div>
                        <div
                          class="flex min-w-0 cursor-pointer items-start gap-2.5 rounded-lg px-2.5 py-2 text-muted-foreground transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] focus-within:ring-2 focus-within:ring-ring/40 hover:bg-muted/50 hover:text-foreground"
                          @click="setAttributeDecision(field.key, 'create')"
                          :class="
                            attributeDecisionValue(field.key) === 'create'
                              ? 'bg-muted/70 text-foreground'
                              : ''
                          "
                        >
                          <RadioGroupItem
                            :id="`duplicate-${fieldIndex}-create`"
                            value="create"
                            :aria-label="
                              $t(
                                'marketplace.categoryImportPage.createNewAttribute',
                              )
                            "
                            class="mt-0.5"
                          />
                          <label
                            :for="`duplicate-${fieldIndex}-create`"
                            class="flex-1 cursor-pointer text-sm font-medium"
                          >
                            {{
                              $t(
                                "marketplace.categoryImportPage.createNewAttribute",
                              )
                            }}
                          </label>
                        </div>
                      </RadioGroup>
                    </div>
                  </InsetGroupItem>
                </InsetGroup>
              </TooltipProvider>
            </SettingsFormSection>
          </template>

          <div
            class="mt-auto flex shrink-0 flex-wrap items-center justify-between gap-3"
          >
            <Button
              variant="ghost"
              size="sm"
              class="shrink-0"
              @click="changeAdapterCategory"
            >
              <ArrowLeft class="mr-1 size-4" />
              {{ $t("marketplace.categoryImportPage.changeAdapterCategory") }}
            </Button>
            <div
              v-if="!isLoadingFields && hasImportConfig"
              class="ml-auto flex min-w-0 flex-wrap items-center justify-end gap-3"
            >
              <p
                v-if="hasUnresolvedDuplicateDecisions"
                class="text-xs text-muted-foreground"
              >
                {{
                  $t("marketplace.categoryImportPage.resolveDuplicateChoices")
                }}
              </p>
              <Button
                class="shrink-0"
                :disabled="importButtonDisabled"
                @click="handleImportCategory"
              >
                <Spinner v-if="isImporting" class="mr-2" />
                <Download v-else class="mr-2 size-4" />
                {{ $t("marketplace.categoryImportPage.import") }}
                <template v-if="importSelectedFieldKeys.length > 0">
                  ·
                  {{
                    $t("marketplace.categoryImportPage.attrsAbbrCount", {
                      count: importSelectedFieldKeys.length,
                    })
                  }}
                </template>
              </Button>
            </div>
          </div>
        </div>
      </div>
    </PageSection>

    <Dialog
      :open="showImportFieldDetailsDialog"
      @update:open="handleImportFieldDetailsDialogOpenChange"
    >
      <DialogContent
        v-if="importFieldDetails"
        size="2xl"
        class="max-h-[min(90vh,48rem)] flex flex-col gap-0 overflow-hidden p-0"
      >
        <DialogHeader variant="bar">
          <DialogTitle>
            {{ importFieldDetails.importName || importFieldDetails.name }}
          </DialogTitle>
          <DialogDescription>
            {{ $t("marketplace.categoryImportPage.fieldDetailsDescription") }}
          </DialogDescription>
        </DialogHeader>

        <div class="flex-1 overflow-y-auto px-6 py-5">
          <SettingsFormSection
            class="!max-w-none"
            :title="$t('marketplace.categoryImportPage.fieldDetailsBasic')"
          >
            <SettingsFormField :label="$t('common.name')">
              <div
                class="min-h-9 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm"
              >
                {{ importFieldDetails.importName || importFieldDetails.name }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.marketplaceName')"
            >
              <div
                class="min-h-9 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm"
              >
                {{ importFieldDetails.name }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.attributeKey')"
            >
              <div
                class="min-h-9 break-all rounded-control border border-input bg-muted/30 px-3 py-2 font-mono text-sm"
              >
                {{ importFieldDetails.attributeKey }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.fieldKey')"
            >
              <div
                class="min-h-9 break-all rounded-control border border-input bg-muted/30 px-3 py-2 font-mono text-sm"
              >
                {{ importFieldDetails.key }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.fieldType')"
            >
              <div
                class="min-h-9 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm"
              >
                {{ getImportTypeLabel(importFieldDetails.importType) }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="importFieldDetails.unitCode"
              :label="$t('marketplace.categoryImportPage.unitLabel')"
            >
              <div
                class="min-h-9 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm"
              >
                {{ getImportUnitLabel(importFieldDetails.unitCode) }}
                <span class="text-muted-foreground"
                  >({{ importFieldDetails.unitCode }})</span
                >
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.attributeGroup')"
            >
              <div
                class="flex min-h-9 items-center gap-2 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm"
              >
                <AttributeGroupIcon
                  :group-name="importFieldDetails.attributeGroup"
                  :group-icon="importFieldDetails.groupIcon"
                  aria-hidden="true"
                />
                {{ getImportFieldValue(importFieldDetails.attributeGroup) }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="
                $t('marketplace.categoryImportPage.requiredInMarketplace')
              "
            >
              <div class="flex h-9 items-center gap-2">
                <Switch
                  :model-value="importFieldDetails.isRequired"
                  disabled
                  aria-hidden="true"
                />
                <span class="text-sm text-muted-foreground">
                  {{
                    importFieldDetails.isRequired
                      ? $t("common.yes")
                      : $t("common.no")
                  }}
                </span>
              </div>
            </SettingsFormField>
          </SettingsFormSection>

          <SettingsFormSection
            class="!max-w-none"
            :title="
              $t('marketplace.categoryImportPage.fieldDetailsAttributeSettings')
            "
          >
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.attributeRequired')"
            >
              <div class="flex h-9 items-center gap-2">
                <Switch
                  :model-value="importFieldDetails.attributeIsRequired"
                  disabled
                  aria-hidden="true"
                />
                <span class="text-sm text-muted-foreground">
                  {{
                    importFieldDetails.attributeIsRequired
                      ? $t("common.yes")
                      : $t("common.no")
                  }}
                </span>
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.attributeUnique')"
            >
              <div class="flex h-9 items-center gap-2">
                <Switch
                  :model-value="importFieldDetails.isUnique"
                  disabled
                  aria-hidden="true"
                />
                <span class="text-sm text-muted-foreground">
                  {{
                    importFieldDetails.isUnique
                      ? $t("common.yes")
                      : $t("common.no")
                  }}
                </span>
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="importFieldDetails.importType === 'Text'"
              :label="$t('marketplace.categoryImportPage.attributeMultiline')"
            >
              <div class="flex h-9 items-center gap-2">
                <Switch
                  :model-value="importFieldDetails.isMultiline"
                  disabled
                  aria-hidden="true"
                />
                <span class="text-sm text-muted-foreground">
                  {{
                    importFieldDetails.isMultiline
                      ? $t("common.yes")
                      : $t("common.no")
                  }}
                </span>
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.attributeVisible')"
            >
              <div class="flex h-9 items-center gap-2">
                <Switch
                  :model-value="importFieldDetails.showOnProductPage"
                  disabled
                  aria-hidden="true"
                />
                <span class="text-sm text-muted-foreground">
                  {{
                    importFieldDetails.showOnProductPage
                      ? $t("common.yes")
                      : $t("common.no")
                  }}
                </span>
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="
                ['Select', 'MultiSelect'].includes(
                  importFieldDetails.importType,
                )
              "
              :label="$t('marketplace.categoryImportPage.allowCustomValues')"
            >
              <div class="flex h-9 items-center gap-2">
                <Switch
                  :model-value="importFieldDetails.allowCustomValues"
                  disabled
                  aria-hidden="true"
                />
                <span class="text-sm text-muted-foreground">
                  {{
                    importFieldDetails.allowCustomValues
                      ? $t("common.yes")
                      : $t("common.no")
                  }}
                </span>
              </div>
            </SettingsFormField>
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.defaultValue')"
            >
              <div
                class="min-h-9 whitespace-pre-wrap break-words rounded-control border border-input bg-muted/30 px-3 py-2 text-sm"
              >
                {{ getImportFieldValue(importFieldDetails.defaultValue) }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="importFieldDetails.description"
              :label="$t('common.description')"
            >
              <div
                class="min-h-9 whitespace-pre-wrap break-words rounded-control border border-input bg-muted/30 px-3 py-2 text-sm"
              >
                {{ importFieldDetails.description }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="importFieldDetails.description"
              :label="$t('marketplace.categoryImportPage.descriptionInTooltip')"
            >
              <div class="flex h-9 items-center gap-2">
                <Switch
                  :model-value="importFieldDetails.descriptionInTooltip"
                  disabled
                  aria-hidden="true"
                />
                <span class="text-sm text-muted-foreground">
                  {{
                    importFieldDetails.descriptionInTooltip
                      ? $t("common.yes")
                      : $t("common.no")
                  }}
                </span>
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="
                importFieldDetails.importType !== 'Number' &&
                importFieldDetails.maxLength != null
              "
              :label="$t('marketplace.categoryImportPage.maxLength')"
            >
              <div
                class="min-h-9 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm tabular-nums"
              >
                {{ importFieldDetails.maxLength }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="
                importFieldDetails.importType === 'Number' &&
                importFieldDetails.minValue != null
              "
              :label="$t('marketplace.categoryImportPage.minValue')"
            >
              <div
                class="min-h-9 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm tabular-nums"
              >
                {{ importFieldDetails.minValue }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="
                importFieldDetails.importType === 'Number' &&
                importFieldDetails.maxValue != null
              "
              :label="$t('marketplace.categoryImportPage.maxValue')"
            >
              <div
                class="min-h-9 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm tabular-nums"
              >
                {{ importFieldDetails.maxValue }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="
                ['Select', 'MultiSelect'].includes(
                  importFieldDetails.importType,
                ) && importFieldDetails.maxValues != null
              "
              :label="$t('marketplace.categoryImportPage.maxValues')"
            >
              <div
                class="min-h-9 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm tabular-nums"
              >
                {{ importFieldDetails.maxValues }}
              </div>
            </SettingsFormField>
            <SettingsFormField
              v-if="importFieldDetails.validationPattern"
              :label="$t('marketplace.categoryImportPage.validationPattern')"
            >
              <div
                class="min-h-9 whitespace-pre-wrap break-all rounded-control border border-input bg-muted/30 px-3 py-2 font-mono text-sm"
              >
                {{ importFieldDetails.validationPattern }}
              </div>
            </SettingsFormField>
          </SettingsFormSection>

          <SettingsFormSection
            v-if="importFieldDetails.optionsCount > 0"
            class="!max-w-none"
            :title="
              $t('marketplace.categoryImportPage.optionsTitle', {
                count: importFieldDetails.optionsCount,
              })
            "
          >
            <SettingsFormField
              :label="$t('marketplace.categoryImportPage.optionsLabel')"
            >
              <div
                v-if="isLoadingImportFieldDetails"
                class="flex min-h-20 items-center justify-center gap-2 rounded-control border border-input bg-muted/30 px-3 py-2 text-sm text-muted-foreground"
              >
                <Spinner />
                {{ $t("marketplace.categoryImportPage.loadingOptions") }}
              </div>
              <pre
                v-else-if="Array.isArray(importFieldDetails.options)"
                class="max-h-64 overflow-auto whitespace-pre-wrap break-words rounded-control border border-input bg-muted/30 px-3 py-2 font-sans text-sm"
                >{{ formatImportOptions(importFieldDetails.options) }}</pre>
              <p v-else class="text-sm text-muted-foreground">
                {{ $t("marketplace.categoryImportPage.optionsLoadFailed") }}
              </p>
            </SettingsFormField>
          </SettingsFormSection>
        </div>
      </DialogContent>
    </Dialog>
  </AppPageChrome>
</template>
