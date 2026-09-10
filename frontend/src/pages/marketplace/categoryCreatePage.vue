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
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { FolderTree } from "lucide-vue-next";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { EmptyState } from "@/components/ui/empty-state";
import {
  PAGE_MARKETPLACE,
  setPageTitle,
  setBreadcrumbParentLabel,
} from "@/router";

const ATTRIBUTE_TYPE_LABEL = {
  0: "text",
  1: "number",
  2: "boolean",
  3: "select",
  4: "multiselect",
  5: "barcode",
};

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
    Textarea,
    Checkbox,
    Switch,
    FolderTree,
    EmptyState,
    SearchableSelect,
  },
  data() {
    return {
      connection: null,
      capabilities: null,
      localCategories: [],
      attributes: [],
      isLoading: false,
      isLoadingAttributes: false,
      isCreating: false,

      localCategoryId: null,
      categoryName: "",
      categoryDescription: "",
      mappingName: "",
      selectedAttributeKeys: [],
      skipAutoMapping: false,

      externalCategorySearch: "",
      currentParentId: null,
      categoryPath: [],
      externalCategories: [],
      isLoadingCategories: false,
    };
  },
  computed: {
    connectionId() {
      return parseInt(this.$route.params.connId);
    },
    adapterLabel() {
      return (
        this.connection?.name ||
        this.connection?.adapterKey ||
        this.$t("marketplace.categoryCreatePage.adapterGenitive")
      );
    },
    localCategoryOptions() {
      return this.localCategories.map((c) => ({
        value: c.id,
        label: `${c.name} (${this.$t("marketplace.categoryCreatePage.attrsAbbrCount", { count: c.attributesCount || 0 })})`,
      }));
    },
    canCreateCategory() {
      return !!this.capabilities?.capabilities?.createCategory;
    },
    allAttributesSelected() {
      return (
        this.attributes.length > 0 &&
        this.selectedAttributeKeys.length === this.attributes.length
      );
    },
    parentCaption() {
      if (this.categoryPath.length === 0) {
        return this.$t("marketplace.categoryCreatePage.parentRoot");
      }
      return this.categoryPath.map((p) => p.name).join(" / ");
    },
    createDisabled() {
      if (this.isCreating || !this.canCreateCategory) return true;
      if (!this.localCategoryId) return true;
      if (!String(this.categoryName || "").trim()) return true;
      return false;
    },
  },
  methods: {
    ...mapActions([
      "getConnection",
      "getCapabilities",
      "getLocalCategories",
      "getCategoryAttributes",
      "getExternalCategories",
      "createExternalCategoryMapping",
    ]),

    attributeTypeLabel(type) {
      return ATTRIBUTE_TYPE_LABEL[type] || String(type ?? "text");
    },

    optionsCount(attribute) {
      if (!attribute?.options) return 0;
      try {
        const parsed = JSON.parse(attribute.options);
        return Array.isArray(parsed) ? parsed.length : 0;
      } catch {
        return 0;
      }
    },

    async fetchInitialData() {
      this.isLoading = true;
      await Promise.all([
        this.fetchConnection(),
        this.fetchCapabilities(),
        this.fetchLocalCategories(),
      ]);
      const query = this.$route.query;
      const localId = Number(query.localCategoryId);
      if (Number.isFinite(localId) && localId > 0) {
        this.localCategoryId = localId;
        await this.loadAttributes(localId);
      }
      this.currentParentId = query.parentId ? String(query.parentId) : null;
      await this.loadCategories(this.currentParentId);
      this.isLoading = false;
    },

    async fetchConnection() {
      await this.getConnection({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.connection = extractResponseData(response);
          setPageTitle(
            `${this.$t("marketplace.categoryCreatePage.createShort")} · ${this.connection.name}`,
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
        onError: (error) => displaySonnerError(error),
      });
    },

    async onLocalCategoryChange(id) {
      this.localCategoryId = id;
      this.categoryName = "";
      this.categoryDescription = "";
      this.mappingName = "";
      this.selectedAttributeKeys = [];
      this.attributes = [];
      if (id) await this.loadAttributes(id);
    },

    async loadAttributes(categoryId) {
      this.isLoadingAttributes = true;
      await this.getCategoryAttributes({
        categoryId,
        onSuccess: (response) => {
          const list = extractResponseData(response, []);
          this.attributes = [...list].sort(
            (a, b) => (a.sortOrder || 0) - (b.sortOrder || 0),
          );
          const local = this.localCategories.find((c) => c.id === categoryId);
          if (!this.categoryName) this.categoryName = local?.name || "";
          this.selectedAttributeKeys = this.attributes
            .map((a) => a.key)
            .filter(Boolean);
        },
        onError: (error) => displaySonnerError(error),
      });
      this.isLoadingAttributes = false;
    },

    async loadCategories(parentId) {
      this.isLoadingCategories = true;
      await this.getExternalCategories({
        connectionId: this.connectionId,
        parentId: parentId || null,
        search: this.externalCategorySearch || null,
        onSuccess: (response) => {
          this.externalCategories = extractResponseData(response, []);
        },
        onError: (error) => displaySonnerError(error),
      });
      this.isLoadingCategories = false;
    },

    async loadRootCategories() {
      this.currentParentId = null;
      this.categoryPath = [];
      await this.loadCategories(null);
    },

    async navigateToCategory(category) {
      if (!category?.id) return;
      this.categoryPath = [
        ...this.categoryPath,
        { id: category.id, name: category.name },
      ];
      this.currentParentId = category.id;
      await this.loadCategories(category.id);
    },

    async navigateToPathIndex(index) {
      if (index < 0) {
        await this.loadRootCategories();
        return;
      }
      this.categoryPath = this.categoryPath.slice(0, index + 1);
      const node = this.categoryPath[index];
      this.currentParentId = node?.id || null;
      await this.loadCategories(this.currentParentId);
    },

    searchExternalCategories() {
      this.loadCategories(this.currentParentId);
    },

    toggleAttribute(key) {
      if (!key) return;
      if (this.selectedAttributeKeys.includes(key)) {
        this.selectedAttributeKeys = this.selectedAttributeKeys.filter(
          (k) => k !== key,
        );
      } else {
        this.selectedAttributeKeys = [...this.selectedAttributeKeys, key];
      }
    },

    toggleAllAttributes() {
      if (this.allAttributesSelected) {
        this.selectedAttributeKeys = [];
        return;
      }
      this.selectedAttributeKeys = this.attributes
        .map((a) => a.key)
        .filter(Boolean);
    },

    async handleCreate() {
      if (this.createDisabled) return;
      this.isCreating = true;
      await this.createExternalCategoryMapping({
        connectionId: this.connectionId,
        data: {
          localCategoryId: this.localCategoryId,
          externalCategoryName: this.categoryName.trim(),
          description: this.categoryDescription.trim() || null,
          parentExternalCategoryId: this.currentParentId,
          mappingName: this.mappingName.trim() || null,
          selectedAttributeKeys: this.selectedAttributeKeys,
          skipAutoMapping: this.skipAutoMapping,
        },
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          displaySonnerSuccess(
            this.$t("marketplace.categoryCreatePage.created", {
              adapter: this.adapterLabel,
            }),
          );
          const mappingId = data.id;
          if (mappingId) {
            this.$router.push(
              `/adapter/${this.connectionId}/categories/${mappingId}`,
            );
          } else {
            this.$router.push(
              `/adapter/${this.connectionId}?tab=mappings`,
            );
          }
        },
        onError: (error) => displaySonnerError(error),
      });
      this.isCreating = false;
    },
  },
  watch: {
    canCreateCategory(value) {
      if (this.isLoading) return;
      if (this.capabilities && !value) {
        displaySonnerError(
          this.$t("marketplace.categoryCreatePage.unsupported", {
            adapter: this.adapterLabel,
          }),
        );
        this.$router.replace(`/adapter/${this.connectionId}?tab=mappings`);
      }
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
      <div v-if="connection" class="flex min-h-0 flex-1 flex-col">
        <EmptyState
          v-if="capabilities && !canCreateCategory"
          class="flex-1 py-12"
          :framed="false"
          :title="$t('marketplace.categoryCreatePage.unsupportedTitle')"
          :description="
            $t('marketplace.categoryCreatePage.unsupported', {
              adapter: adapterLabel,
            })
          "
        >
          <template #icon>
            <FolderTree />
          </template>
        </EmptyState>

        <div
          v-else
          class="grid min-h-0 flex-1 gap-5 overflow-hidden xl:grid-cols-2"
        >
          <div class="flex min-h-0 min-w-0 flex-col gap-4 overflow-hidden">
            <SettingsFormSection
              class="!mt-0 !max-w-none shrink-0"
              :title="$t('marketplace.categoryCreatePage.localTitle')"
              :description="$t('marketplace.categoryCreatePage.localDescription')"
            >
              <SettingsFormField
                :label="$t('marketplace.categoryCreatePage.localCategoryLabel')"
              >
                <SearchableSelect
                  :model-value="localCategoryId"
                  :options="localCategoryOptions"
                  :placeholder="
                    $t('marketplace.categoryCreatePage.selectCategoryPlaceholder')
                  "
                  :search-placeholder="
                    $t('marketplace.categoryCreatePage.searchCategoryPlaceholder')
                  "
                  :empty-text="
                    $t('marketplace.categoryCreatePage.categoriesNotFound')
                  "
                  @update:model-value="onLocalCategoryChange"
                />
              </SettingsFormField>
              <SettingsFormField
                :label="$t('marketplace.categoryCreatePage.nameLabel')"
              >
                <Input
                  v-model="categoryName"
                  :placeholder="
                    $t('marketplace.categoryCreatePage.namePlaceholder')
                  "
                />
              </SettingsFormField>
              <SettingsFormField
                :label="$t('marketplace.categoryCreatePage.descriptionLabel')"
              >
                <Textarea
                  v-model="categoryDescription"
                  :placeholder="
                    $t('marketplace.categoryCreatePage.descriptionPlaceholder')
                  "
                  class="min-h-20"
                />
              </SettingsFormField>
              <SettingsFormField
                :label="$t('marketplace.categoryCreatePage.mappingNameLabel')"
                :description="
                  $t('marketplace.categoryCreatePage.mappingNameHint')
                "
              >
                <Input
                  v-model="mappingName"
                  :placeholder="
                    $t('marketplace.categoryCreatePage.mappingNamePlaceholder')
                  "
                />
              </SettingsFormField>
            </SettingsFormSection>

            <SettingsFormSection
              class="!mt-0 !max-w-none flex min-h-0 flex-1 flex-col overflow-hidden"
              content-class="min-h-0 flex-1"
              :title="
                $t('marketplace.categoryCreatePage.parentTitle', {
                  adapter: adapterLabel,
                })
              "
              :description="$t('marketplace.categoryCreatePage.parentDescription')"
            >
              <p class="px-1 text-xs text-muted-foreground">
                {{ $t("marketplace.categoryCreatePage.parentCurrent") }}
                <span class="font-medium text-foreground">{{
                  parentCaption
                }}</span>
              </p>
              <ExternalCategoryBrowser
                v-model:search="externalCategorySearch"
                class="min-h-0 flex-1"
                :categories="externalCategories"
                :path="categoryPath"
                :loading="isLoadingCategories"
                :selected-id="currentParentId"
                :search-placeholder="
                  $t('marketplace.categoryCreatePage.searchByNamePlaceholder')
                "
                :search-aria-label="
                  $t('marketplace.categoryCreatePage.findCategories')
                "
                :empty-title="
                  $t('marketplace.categoryCreatePage.categoriesNotFound')
                "
                :empty-description="
                  $t('marketplace.categoryCreatePage.parentEmptyHint')
                "
                @search="searchExternalCategories"
                @select="navigateToCategory"
                @navigate="navigateToCategory"
                @path-index="navigateToPathIndex"
              />
            </SettingsFormSection>
          </div>

          <div class="flex min-h-0 min-w-0 flex-col gap-4 overflow-hidden">
            <SettingsFormSection
              class="!mt-0 !max-w-none flex min-h-0 flex-1 flex-col overflow-hidden"
              content-class="min-h-0 flex-1"
              :title="
                $t('marketplace.categoryCreatePage.fieldsTitle', {
                  selected: selectedAttributeKeys.length,
                  total: attributes.length,
                })
              "
              :description="$t('marketplace.categoryCreatePage.fieldsDescription')"
            >
              <template #actions>
                <Button
                  v-if="attributes.length > 0"
                  variant="ghost"
                  size="sm"
                  class="shrink-0"
                  @click="toggleAllAttributes"
                >
                  {{
                    allAttributesSelected
                      ? $t("marketplace.categoryCreatePage.deselectAll")
                      : $t("marketplace.categoryCreatePage.selectAll")
                  }}
                </Button>
              </template>

              <div
                v-if="!localCategoryId"
                class="flex min-h-0 flex-1 items-center justify-center px-4 text-center text-sm text-muted-foreground"
              >
                {{ $t("marketplace.categoryCreatePage.pickLocalFirst") }}
              </div>
              <div
                v-else-if="isLoadingAttributes"
                class="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 text-muted-foreground"
              >
                <Spinner size="2xl" />
              </div>
              <div
                v-else-if="attributes.length === 0"
                class="flex min-h-0 flex-1 items-center justify-center px-4 text-center text-sm text-muted-foreground"
              >
                {{ $t("marketplace.categoryCreatePage.noAttributes") }}
              </div>
              <InsetGroup v-else scrollable class="flex-1">
                <InsetGroupItem
                  v-for="attribute in attributes"
                  :key="attribute.key || attribute.id"
                  as="div"
                  class="cursor-pointer"
                  @click="toggleAttribute(attribute.key)"
                >
                  <template #leading>
                    <Checkbox
                      :model-value="
                        selectedAttributeKeys.includes(attribute.key)
                      "
                      @update:model-value="toggleAttribute(attribute.key)"
                      @click.stop
                    />
                  </template>
                  <p class="truncate">{{ attribute.name }}</p>
                  <p
                    class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
                  >
                    {{ attribute.key }}
                    <template v-if="attribute.group">
                      · {{ attribute.group }}
                    </template>
                  </p>
                  <template #trailing>
                    <span class="text-muted-foreground text-xs font-normal">
                      {{ attributeTypeLabel(attribute.type) }}
                      <template v-if="attribute.isRequired">
                        ·
                        {{ $t("marketplace.categoryCreatePage.requiredAbbr") }}
                      </template>
                      <template v-if="attribute.showInShop">
                        ·
                        {{ $t("marketplace.categoryCreatePage.shopAbbr") }}
                      </template>
                      <template v-if="optionsCount(attribute) > 0">
                        ·
                        {{
                          $t("marketplace.categoryCreatePage.optionsAbbrCount", {
                            count: optionsCount(attribute),
                          })
                        }}
                      </template>
                    </span>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </SettingsFormSection>

            <SettingsFormSection
              class="!mt-0 !max-w-none shrink-0"
              :title="$t('marketplace.categoryCreatePage.optionsTitle')"
            >
              <InsetGroup>
                <InsetGroupItem as="div">
                  <p>{{ $t("marketplace.categoryCreatePage.skipAutoMap") }}</p>
                  <p class="text-muted-foreground mt-0.5 text-xs font-normal">
                    {{ $t("marketplace.categoryCreatePage.skipAutoMapHint") }}
                  </p>
                  <template #trailing>
                    <Switch v-model="skipAutoMapping" @click.stop />
                  </template>
                </InsetGroupItem>
              </InsetGroup>
              <template #footer>
                <Button
                  class="shrink-0"
                  :disabled="createDisabled"
                  @click="handleCreate"
                >
                  <Spinner v-if="isCreating" class="mr-2" />
                  <FolderTree v-else class="size-4 mr-2" />
                  {{
                    isCreating
                      ? $t("marketplace.categoryCreatePage.creating")
                      : $t("marketplace.categoryCreatePage.create", {
                          adapter: adapterLabel,
                        })
                  }}
                  <template v-if="selectedAttributeKeys.length > 0">
                    ·
                    {{
                      $t("marketplace.categoryCreatePage.attrsAbbrCount", {
                        count: selectedAttributeKeys.length,
                      })
                    }}
                  </template>
                </Button>
              </template>
            </SettingsFormSection>
          </div>
        </div>
      </div>
    </PageSection>
  </AppPageChrome>
</template>
