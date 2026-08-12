<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import ExternalCategoryBrowser from "@/components/marketplace/ExternalCategoryBrowser.vue";
import { FieldSet, FieldLegend, FieldDescription } from "@/components/ui/field";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  Sparkles,
  Link2,
  Download,
  FolderTree,
} from "lucide-vue-next";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_MARKETPLACE, setPageTitle } from "@/router";

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    SettingsFormField,
    SettingsFormSection,
    ExternalCategoryBrowser,
    FieldSet,
    FieldLegend,
    FieldDescription,
    Button,
    Input,
    Badge,
    Checkbox,
    Sparkles,
    Link2,
    Download,
    FolderTree,
    RadioGroup,
    RadioGroupItem,
    EmptyState,
    SearchableSelect
  },
  data() {
    return {
      connection: null,
      capabilities: null,
      localCategories: [],
      externalCategories: [],
      isLoading: false,

      importMode: "new",
      importTargetCategoryId: null,
      importCategoryId: "",
      importCategoryName: "",
      importCategoryExternalName: "",
      importExternalFields: [],
      importSelectedFieldKeys: [],
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
    breadcrumbs() {
      return [
        { label: this.$t("marketplace.categoryImportPage.adapters"), to: PAGE_MARKETPLACE },
        {
          label: this.connection?.name || this.$t("marketplace.categoryImportPage.connection"),
          to: `/adapter/${this.connectionId}?tab=mappings`,
        },
        { label: this.$t("marketplace.categoryImportPage.importCategory") },
      ];
    },
    adapterLabel() {
      return this.connection?.adapterKey || this.$t("marketplace.categoryImportPage.adapterGenitive");
    },
    importAttributeFields() {
      return this.importExternalFields.filter((f) => !f.isBaseField);
    },
    importBaseFields() {
      return this.importExternalFields.filter((f) => f.isBaseField);
    },
    allAttributeFieldsSelected() {
      return (
        this.importAttributeFields.length > 0 &&
        this.importSelectedFieldKeys.length === this.importAttributeFields.length
      );
    },
    importButtonDisabled() {
      if (this.isImporting) return true;
      if (!this.importCategoryId) return true;
      if (this.importMode === "existing" && !this.importTargetCategoryId) return true;
      return false;
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
          setPageTitle(`${this.$t("marketplace.categoryImportPage.importShort")} · ${this.connection.name}`);
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
        parentId: this.externalCategorySearch ? undefined : this.currentParentId,
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

    selectExternalCategory(category) {
      if (category.hasChildren) {
        this.navigateToCategory(category);
        return;
      }
      this.importCategoryId = category.id;
      this.importCategoryExternalName = category.name;
      this.fetchExternalCategoryFields();
    },

    async fetchExternalCategoryFields() {
      if (!this.importCategoryId) return;

      this.isLoadingFields = true;
      this.importExternalFields = [];
      this.importSelectedFieldKeys = [];

      await this.getExternalCategoryFields({
        connectionId: this.connectionId,
        externalCategoryId: this.importCategoryId,
        onSuccess: (response) => {
          this.importExternalFields = extractResponseData(response, []);
          this.importSelectedFieldKeys = this.importAttributeFields.map((f) => f.key);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingFields = false;
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
        this.importSelectedFieldKeys = this.importAttributeFields.map((f) => f.key);
      }
    },

    async handleImportCategory() {
      if (!this.importCategoryId) {
        displaySonnerError(this.$t("marketplace.categoryImportPage.selectAdapterCategory"));
        return;
      }

      if (!this.hasImportConfig) {
        displaySonnerError(this.$t("marketplace.categoryImportPage.selectLeafCategory"));
        return;
      }

      if (this.importMode === "existing" && !this.importTargetCategoryId) {
        displaySonnerError(this.$t("marketplace.categoryImportPage.selectExistingCategory"));
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
          selectedFieldKeys:
            this.importSelectedFieldKeys.length > 0
              ? this.importSelectedFieldKeys
              : null,
        },
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          displaySonnerSuccess(result.message || this.$t("marketplace.categoryImportPage.categoryImported"));
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
  <AppPageChrome :breadcrumb-items="breadcrumbs" content-class="pb-4">
    <PageSection
      :loading="isLoading"
      class="!mb-0 flex flex-1 flex-col"
    >
      <div v-if="connection" class="flex flex-1 flex-col gap-4">
        <div class="flex shrink-0 flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div class="min-w-0 space-y-1.5">
            <div class="flex flex-wrap items-center gap-2">
              <h1 class="text-lg font-semibold">{{ $t("marketplace.categoryImportPage.importCategory") }}</h1>
              <Badge variant="outline">{{ connection.name }}</Badge>
            </div>
            <p class="text-sm text-muted-foreground">
              {{ $t("marketplace.categoryImportPage.pageDescription", { adapterLabel }) }}
            </p>
          </div>
          <Button
            class="shrink-0"
            :disabled="importButtonDisabled || !hasImportConfig"
            @click="handleImportCategory"
          >
            <Spinner v-if="isImporting" class="mr-2" />
            <Download v-else class="size-4 mr-2" />
            {{ $t("marketplace.categoryImportPage.import") }}
            <template v-if="importSelectedFieldKeys.length > 0">
              · {{ $t("marketplace.categoryImportPage.attrsAbbrCount", { count: importSelectedFieldKeys.length }) }}
            </template>
          </Button>
        </div>

        <div
          class="grid h-[max(22rem,calc(100svh-var(--app-navbar-height)-12rem))] min-h-[22rem] gap-5 overflow-hidden xl:grid-cols-2"
        >
          <div class="flex min-h-0 min-w-0 flex-col gap-3 overflow-hidden">
            <FieldSet class="m-0 shrink-0 gap-1 border-0 p-0">
              <FieldLegend variant="label" class="mb-0">
                {{ $t("marketplace.categoryImportPage.adapterCategory") }}
              </FieldLegend>
              <FieldDescription>
                {{ $t("marketplace.categoryImportPage.adapterCategoryDescription") }}
              </FieldDescription>
            </FieldSet>

            <ExternalCategoryBrowser
              v-model:search="externalCategorySearch"
              class="min-h-0 flex-1"
              :categories="externalCategories"
              :path="categoryPath"
              :loading="isLoadingCategories"
              :selected-id="importCategoryId"
              :search-placeholder="$t('marketplace.categoryImportPage.searchByNamePlaceholder')"
              :search-aria-label="$t('marketplace.categoryImportPage.findCategories')"
              :empty-title="$t('marketplace.categoryImportPage.categoriesNotFoundTitle')"
              :empty-description="
                isEmptyRemoteRoot && canCreateCategory
                  ? $t('marketplace.categoryImportPage.categoriesNotFoundCreateHint', { adapter: adapterLabel })
                  : $t('marketplace.categoryImportPage.categoriesNotFoundDescription')
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
                    $router.push({
                      path: `/adapter/${connectionId}`,
                      query: { tab: 'mappings', createFromLocal: '1' },
                    })
                  "
                >
                  <FolderTree class="size-4 mr-2" />
                  {{ $t("marketplace.categoryImportPage.createFromLocalCta", { adapter: adapterLabel }) }}
                </Button>
              </template>
            </ExternalCategoryBrowser>
          </div>

          <div class="flex min-h-0 min-w-0 flex-col gap-4 overflow-hidden">
            <div
              v-if="!importCategoryId"
              class="flex min-h-0 w-full flex-1 flex-col rounded-lg border border-dashed"
            >
              <EmptyState
                class="w-full flex-1 py-12"
                :framed="false"
                :title="$t('marketplace.categoryImportPage.selectCategoryTitle')"
                :description="$t('marketplace.categoryImportPage.selectCategoryDescription')"
              >
                <template #icon>
                  <FolderTree />
                </template>
              </EmptyState>
            </div>

            <div
              v-else-if="isLoadingFields"
              class="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 rounded-lg border text-muted-foreground"
            >
              <Spinner size="2xl" />
              <span class="text-sm">{{ $t("marketplace.categoryImportPage.loadingCategoryFields") }}</span>
            </div>

            <template v-else-if="hasImportConfig">
              <SettingsFormSection
                class="!max-w-none shrink-0"
                :title="$t('marketplace.categoryImportPage.whereToImportTitle')"
                :description="$t('marketplace.categoryImportPage.whereToImportDescription')"
              >
                <RadioGroup v-model="importMode" class="gap-4">
                  <div class="space-y-3">
                    <div class="flex items-center gap-3">
                      <RadioGroupItem value="new" id="import-mode-new" />
                      <label
                        for="import-mode-new"
                        class="flex cursor-pointer items-center gap-2 text-sm font-medium"
                      >
                        <Sparkles class="size-4 text-warning" />
                        {{ $t("marketplace.categoryImportPage.createNewCategory") }}
                      </label>
                    </div>
                    <div v-if="importMode === 'new'" class="pl-7">
                      <SettingsFormField :label="$t('marketplace.categoryImportPage.categoryNameLabel')">
                        <Input
                          v-model="importCategoryName"
                          :placeholder="$t('marketplace.categoryImportPage.categoryNamePlaceholder')"
                        />
                      </SettingsFormField>
                    </div>
                  </div>

                  <div class="space-y-3">
                    <div class="flex items-center gap-3">
                      <RadioGroupItem value="existing" id="import-mode-existing" />
                      <label
                        for="import-mode-existing"
                        class="flex cursor-pointer items-center gap-2 text-sm font-medium"
                      >
                        <Link2 class="size-4 text-muted-foreground" />
                        {{ $t("marketplace.categoryImportPage.intoExistingCategory") }}
                      </label>
                    </div>
                    <div v-if="importMode === 'existing'" class="pl-7">
                      <SettingsFormField :label="$t('marketplace.categoryImportPage.localCategoryLabel')">
                        <SearchableSelect
                          :model-value="importTargetCategoryId"
                          :options="localCategoryOptions"
                          :placeholder="$t('marketplace.categoryImportPage.selectCategoryPlaceholder')"
                          :search-placeholder="$t('marketplace.categoryImportPage.searchCategoryPlaceholder')"
                          :empty-text="$t('marketplace.categoryImportPage.categoriesNotFoundTitle')"
                          @update:model-value="importTargetCategoryId = $event"
                        />
                      </SettingsFormField>
                    </div>
                  </div>
                </RadioGroup>

                <p
                  v-if="importBaseFields.length > 0"
                  class="rounded-lg bg-muted/50 px-3 py-2.5 text-sm text-muted-foreground"
                >
                  <span class="font-medium text-foreground">
                    {{ $t("marketplace.categoryImportPage.baseFieldsCount", { count: importBaseFields.length }) }}
                  </span>
                  {{ $t("marketplace.categoryImportPage.baseFieldsHint") }}
                </p>
              </SettingsFormSection>

              <div class="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
                <div class="flex shrink-0 items-start justify-between gap-3">
                  <FieldSet class="m-0 min-w-0 gap-1 border-0 p-0">
                    <FieldLegend variant="label" class="mb-0">
                      {{
                        $t("marketplace.categoryImportPage.attributesForImportTitle", {
                          selected: importSelectedFieldKeys.length,
                          total: importAttributeFields.length,
                        })
                      }}
                    </FieldLegend>
                    <FieldDescription>
                      {{ $t("marketplace.categoryImportPage.attributesForImportDescription") }}
                    </FieldDescription>
                  </FieldSet>
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
                </div>

                <div
                  v-if="importAttributeFields.length === 0"
                  class="flex min-h-0 flex-1 items-center justify-center rounded-lg border border-dashed px-4 text-center text-sm text-muted-foreground"
                >
                  {{ $t("marketplace.categoryImportPage.noAdditionalAttributes") }}
                </div>

                <div
                  v-else
                  class="min-h-0 flex-1 overflow-y-auto rounded-lg border p-1.5"
                >
                  <button
                    v-for="field in importAttributeFields"
                    :key="field.key"
                    type="button"
                    class="flex w-full items-start gap-3 rounded-md px-2.5 py-2 text-left transition-colors hover:bg-accent"
                    @click="toggleFieldSelection(field.key)"
                  >
                    <Checkbox
                      class="mt-0.5"
                      :model-value="importSelectedFieldKeys.includes(field.key)"
                      @update:model-value="toggleFieldSelection(field.key)"
                      @click.stop
                    />
                    <div class="min-w-0 flex-1">
                      <div class="truncate text-sm font-medium">{{ field.name }}</div>
                      <div
                        v-if="field.description"
                        class="truncate text-xs text-muted-foreground"
                      >
                        {{ field.description }}
                      </div>
                    </div>
                    <div class="flex shrink-0 flex-wrap items-center justify-end gap-1.5">
                      <Badge variant="outline" class="text-xs font-normal">
                        {{ field.type }}
                      </Badge>
                      <Badge
                        v-if="field.isRequired"
                        variant="destructive"
                        class="text-xs font-normal"
                      >
                        {{ $t("marketplace.categoryImportPage.requiredAbbr") }}
                      </Badge>
                      <span
                        v-if="field.optionsCount > 0"
                        class="text-xs tabular-nums text-muted-foreground"
                      >
                        {{
                          $t("marketplace.categoryImportPage.optionsAbbrCount", {
                            count: field.optionsCount,
                          })
                        }}
                      </span>
                    </div>
                  </button>
                </div>
              </div>
            </template>
          </div>
        </div>
      </div>
    </PageSection>
  </AppPageChrome>
</template>
