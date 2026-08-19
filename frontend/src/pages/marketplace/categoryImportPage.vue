<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import ExternalCategoryBrowser from "@/components/marketplace/ExternalCategoryBrowser.vue";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  Download,
  FolderTree,
} from "lucide-vue-next";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_MARKETPLACE, setPageTitle } from "@/router";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";

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
    Download,
    FolderTree,
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
      FIELD_LIMITS,

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
              result.message || this.$t("marketplace.categoryImportPage.categoryImported"),
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
  <AppPageChrome fill-viewport :breadcrumb-items="breadcrumbs">
    <PageSection
      :loading="isLoading"
      class="!mb-0 flex min-h-0 flex-1 flex-col"
    >
      <div v-if="connection" class="flex min-h-0 flex-1 flex-col">
        <div
          class="grid min-h-0 flex-1 gap-5 overflow-hidden xl:grid-cols-2"
        >
          <SettingsFormSection
            class="!mt-0 !max-w-none flex h-full min-h-0 min-w-0 flex-col overflow-hidden"
            content-class="min-h-0 flex-1"
            :title="$t('marketplace.categoryImportPage.adapterCategory')"
            :description="$t('marketplace.categoryImportPage.adapterCategoryDescription')"
          >
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
          </SettingsFormSection>

          <div class="flex min-h-0 min-w-0 flex-col gap-4 overflow-hidden">
            <div
              v-if="!importCategoryId"
              class="flex min-h-0 w-full flex-1 flex-col"
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
              class="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 text-muted-foreground"
            >
              <Spinner size="2xl" />
              <span class="text-sm">{{ $t("marketplace.categoryImportPage.loadingCategoryFields") }}</span>
            </div>

            <template v-else-if="hasImportConfig">
              <SettingsFormSection
                class="!mt-0 !max-w-none shrink-0"
                :title="$t('marketplace.categoryImportPage.whereToImportTitle')"
                :description="$t('marketplace.categoryImportPage.whereToImportDescription')"
              >
                <InsetGroup
                  role="radiogroup"
                  :aria-label="$t('marketplace.categoryImportPage.whereToImportTitle')"
                >
                  <InsetGroupItem
                    role="radio"
                    :aria-checked="importMode === 'new'"
                    :selected="importMode === 'new'"
                    @click="importMode = 'new'"
                  >
                    <p>{{ $t("marketplace.categoryImportPage.createNewCategory") }}</p>
                  </InsetGroupItem>
                  <InsetGroupItem
                    role="radio"
                    :aria-checked="importMode === 'existing'"
                    :selected="importMode === 'existing'"
                    @click="importMode = 'existing'"
                  >
                    <p>{{ $t("marketplace.categoryImportPage.intoExistingCategory") }}</p>
                  </InsetGroupItem>
                </InsetGroup>
                <SettingsFormField
                  v-if="importMode === 'new'"
                  :label="$t('marketplace.categoryImportPage.categoryNameLabel')"
                >
                  <Input
                    v-model="importCategoryName"
                    :placeholder="$t('marketplace.categoryImportPage.categoryNamePlaceholder')"
                  />
                </SettingsFormField>
                <SettingsFormField
                  v-else
                  :label="$t('marketplace.categoryImportPage.localCategoryLabel')"
                >
                  <SearchableSelect
                    :model-value="importTargetCategoryId"
                    :options="localCategoryOptions"
                    :placeholder="$t('marketplace.categoryImportPage.selectCategoryPlaceholder')"
                    :search-placeholder="$t('marketplace.categoryImportPage.searchCategoryPlaceholder')"
                    :empty-text="$t('marketplace.categoryImportPage.categoriesNotFoundTitle')"
                    @update:model-value="importTargetCategoryId = $event"
                  />
                </SettingsFormField>

                <p
                  v-if="importBaseFields.length > 0"
                  class="px-1 text-xs text-muted-foreground"
                >
                  <span class="font-medium text-foreground">
                    {{ $t("marketplace.categoryImportPage.baseFieldsCount", { count: importBaseFields.length }) }}
                  </span>
                  {{ $t("marketplace.categoryImportPage.baseFieldsHint") }}
                </p>
              </SettingsFormSection>

              <SettingsFormSection
                class="!mt-0 !max-w-none flex min-h-0 flex-1 flex-col overflow-hidden"
                content-class="min-h-0 flex-1"
                :title="
                  $t('marketplace.categoryImportPage.attributesForImportTitle', {
                    selected: importSelectedFieldKeys.length,
                    total: importAttributeFields.length,
                  })
                "
                :description="$t('marketplace.categoryImportPage.attributesForImportDescription')"
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
                  {{ $t("marketplace.categoryImportPage.noAdditionalAttributes") }}
                </div>

                <InsetGroup v-else scrollable class="flex-1">
                  <InsetGroupItem
                      v-for="field in importAttributeFields"
                      :key="field.key"
                      as="div"
                      class="cursor-pointer"
                      @click="toggleFieldSelection(field.key)"
                    >
                      <template #leading>
                        <Checkbox
                          :model-value="importSelectedFieldKeys.includes(field.key)"
                          @update:model-value="toggleFieldSelection(field.key)"
                          @click.stop
                        />
                      </template>
                      <p class="truncate">{{ field.name }}</p>
                      <p
                        v-if="field.description"
                        class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
                      >
                        {{ field.description }}
                      </p>
                      <template #trailing>
                        <span class="text-muted-foreground text-xs font-normal">
                          {{ field.type }}
                          <template v-if="field.isRequired">
                            · {{ $t("marketplace.categoryImportPage.requiredAbbr") }}
                          </template>
                          <template v-if="field.optionsCount > 0">
                            ·
                            {{
                              $t("marketplace.categoryImportPage.optionsAbbrCount", {
                                count: field.optionsCount,
                              })
                            }}
                          </template>
                        </span>
                      </template>
                    </InsetGroupItem>
                </InsetGroup>
                <template #footer>
                  <Button
                    class="shrink-0"
                    :disabled="importButtonDisabled"
                    @click="handleImportCategory"
                  >
                    <Spinner v-if="isImporting" class="mr-2" />
                    <Download v-else class="size-4 mr-2" />
                    {{ $t("marketplace.categoryImportPage.import") }}
                    <template v-if="importSelectedFieldKeys.length > 0">
                      · {{ $t("marketplace.categoryImportPage.attrsAbbrCount", { count: importSelectedFieldKeys.length }) }}
                    </template>
                  </Button>
                </template>
              </SettingsFormSection>
            </template>
          </div>
        </div>
      </div>
    </PageSection>
  </AppPageChrome>
</template>
