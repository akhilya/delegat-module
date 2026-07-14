<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  Loader2,
  ChevronRight,
  ChevronLeft,
  Home,
  Search,
  Sparkles,
  Link2,
  Download,
  FolderTree,
  ArrowRight,
} from "lucide-vue-next";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_MARKETPLACE, setPageTitle } from "@/router";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    SettingsFormField,
    SettingsFormSection,
    Button,
    Input,
    Badge,
    Checkbox,
    Loader2,
    ChevronRight,
    ChevronLeft,
    Home,
    Search,
    Sparkles,
    Link2,
    Download,
    FolderTree,
    ArrowRight,
    RadioGroup,
    RadioGroupItem,
    EmptyState,
    SearchableSelect,
  },
  data() {
    return {
      connection: null,
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
        { label: "Адаптеры", to: PAGE_MARKETPLACE },
        {
          label: this.connection?.name || "Подключение",
          to: `/adapter/${this.connectionId}?tab=mappings`,
        },
        { label: "Импорт категории" },
      ];
    },
    adapterLabel() {
      return this.connection?.adapterKey || "адаптера";
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
    selectedCategoryLabel() {
      return this.importCategoryExternalName || this.importCategoryId || null;
    },
    localCategoryOptions() {
      return this.localCategories.map((c) => ({
        value: c.id,
        label: `${c.name} (${c.attributesCount} атр.)`,
      }));
    },
  },
  methods: {
    ...mapActions([
      "getConnection",
      "getLocalCategories",
      "getExternalCategories",
      "getExternalCategoryFields",
      "importCategory",
    ]),

    async fetchInitialData() {
      this.isLoading = true;
      await Promise.all([this.fetchConnection(), this.fetchLocalCategories()]);
      await this.loadRootCategories();
      this.isLoading = false;
    },

    async fetchConnection() {
      await this.getConnection({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.connection = extractResponseData(response);
          setPageTitle(`Импорт · ${this.connection.name}`);
        },
        onError: (error) => {
          displaySonnerError(error);
          this.$router.push(PAGE_MARKETPLACE);
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

    async navigateBack() {
      if (this.categoryPath.length === 0) return;
      this.externalCategorySearch = "";
      this.categoryPath.pop();
      this.currentParentId =
        this.categoryPath.length > 0
          ? this.categoryPath[this.categoryPath.length - 1].id
          : null;
      await this.searchExternalCategories();
    },

    selectExternalCategory(category) {
      this.importCategoryId = category.id;
      this.importCategoryExternalName = category.name;
      this.fetchExternalCategoryFields();
    },

    onManualCategoryIdChange() {
      if (!this.importCategoryId) {
        this.importCategoryExternalName = "";
        this.importExternalFields = [];
        this.importSelectedFieldKeys = [];
        return;
      }
      this.importCategoryExternalName = "";
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
        displaySonnerError("Выберите категорию адаптера");
        return;
      }

      if (this.importMode === "existing" && !this.importTargetCategoryId) {
        displaySonnerError("Выберите существующую категорию");
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
          displaySonnerSuccess(result.message || "Категория импортирована");
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
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageSection :loading="isLoading" class="!min-h-0 flex flex-col">
      <div v-if="connection" class="flex flex-col gap-6">
        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div class="min-w-0 space-y-2">
            <div class="flex flex-wrap items-center gap-2">
              <h1 class="text-lg font-semibold">Импорт категории</h1>
              <Badge variant="outline">{{ connection.name }}</Badge>
            </div>
            <p class="text-sm text-muted-foreground">
              Выберите категорию {{ adapterLabel }}, настройте поля и импортируйте её в локальный каталог.
            </p>
          </div>
          <Button
            class="shrink-0"
            :disabled="importButtonDisabled || !hasImportConfig"
            @click="handleImportCategory"
          >
            <Loader2 v-if="isImporting" class="size-4 mr-2 animate-spin" />
            <Download v-else class="size-4 mr-2" />
            Импортировать
            <template v-if="importSelectedFieldKeys.length > 0">
              · {{ importSelectedFieldKeys.length }} атр.
            </template>
          </Button>
        </div>

        <div
          v-if="selectedCategoryLabel"
          class="flex flex-wrap items-center gap-2 rounded-lg border border-primary/20 bg-primary/5 px-3 py-2 text-sm"
        >
          <FolderTree class="size-4 shrink-0 text-muted-foreground" />
          <span class="text-muted-foreground">Источник:</span>
          <span class="font-medium">{{ selectedCategoryLabel }}</span>
          <ArrowRight class="size-4 text-muted-foreground" />
          <span class="text-muted-foreground">Локальный каталог</span>
        </div>

        <div class="grid gap-4 xl:grid-cols-2 xl:gap-5">
          <SettingsFormSection
            class="!max-w-none min-w-0"
            title="Категория адаптера"
            description="Найдите категорию в дереве или укажите её ID вручную."
          >
            <div class="flex gap-2">
              <div class="relative min-w-0 flex-1">
                <Search
                  class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                />
                <Input
                  v-model="externalCategorySearch"
                  placeholder="Поиск по названию..."
                  class="pl-9"
                  @keyup.enter="searchExternalCategories"
                />
              </div>
              <Button
                variant="outline"
                size="icon"
                aria-label="Найти категории"
                @click="searchExternalCategories"
              >
                <Search class="size-4" />
              </Button>
            </div>

            <div
              v-if="categoryPath.length > 0 && !externalCategorySearch"
              class="flex flex-wrap items-center gap-1 text-sm"
            >
              <Button variant="ghost" size="sm" class="h-7 px-2" @click="navigateToPathIndex(-1)">
                <Home class="size-3" />
              </Button>
              <ChevronRight class="size-3 text-muted-foreground" />
              <template v-for="(pathItem, index) in categoryPath" :key="pathItem.id">
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-7 px-2 text-xs"
                  :disabled="index === categoryPath.length - 1"
                  @click="navigateToPathIndex(index)"
                >
                  {{ pathItem.name }}
                </Button>
                <ChevronRight
                  v-if="index < categoryPath.length - 1"
                  class="size-3 text-muted-foreground"
                />
              </template>
            </div>

            <Button
              v-if="categoryPath.length > 0 && !externalCategorySearch"
              variant="outline"
              size="sm"
              class="justify-start"
              @click="navigateBack"
            >
              <ChevronLeft class="size-4 mr-2" />
              Назад
            </Button>

            <div
              class="flex h-[min(20rem,calc(100vh-18rem))] max-h-80 min-h-48 flex-col overflow-hidden rounded-lg border"
            >
              <div
                v-if="isLoadingCategories"
                class="flex min-h-0 flex-1 items-center justify-center"
              >
                <Loader2 class="size-6 animate-spin text-muted-foreground" />
              </div>
              <div
                v-else-if="externalCategories.length === 0"
                class="flex min-h-0 flex-1 items-center justify-center p-4"
              >
                <EmptyState
                  size="sm"
                  title="Категории не найдены"
                  description="Измените поисковый запрос или поднимитесь на уровень выше."
                >
                  <template #icon>
                    <Search />
                  </template>
                </EmptyState>
              </div>
              <div v-else class="min-h-0 flex-1 overflow-y-auto p-1.5">
                <button
                  v-for="cat in externalCategories"
                  :key="cat.id"
                  type="button"
                  class="flex w-full items-center justify-between rounded-md px-2.5 py-2 text-left transition-colors hover:bg-accent"
                  :class="{ 'bg-accent': importCategoryId === cat.id }"
                  @click="
                    cat.hasChildren && !externalCategorySearch
                      ? navigateToCategory(cat)
                      : selectExternalCategory(cat)
                  "
                >
                  <div class="min-w-0 flex-1">
                    <div class="truncate text-sm font-medium">{{ cat.name }}</div>
                    <div
                      v-if="cat.fullPath"
                      class="truncate text-xs text-muted-foreground"
                    >
                      {{ cat.fullPath }}
                    </div>
                  </div>
                  <ChevronRight
                    v-if="cat.hasChildren && !externalCategorySearch"
                    class="ml-2 size-4 shrink-0 text-muted-foreground"
                  />
                </button>
              </div>
            </div>

            <SettingsFormField
              label="ID категории"
              label-for="import-category-id"
            >
              <Input
                id="import-category-id"
                v-model="importCategoryId"
                placeholder="Выберите категорию выше или введите ID"
                @change="onManualCategoryIdChange"
              />
              <template #description>
                Можно указать ID вручную, если категории нет в списке.
              </template>
            </SettingsFormField>
          </SettingsFormSection>

          <div class="flex min-w-0 flex-col gap-5">
            <div
              v-if="!importCategoryId"
              class="flex min-h-48 items-center justify-center rounded-lg border border-dashed py-8"
            >
              <EmptyState
                size="sm"
                title="Выберите категорию"
                description="Сначала укажите категорию адаптера слева — здесь появятся настройки импорта."
              >
                <template #icon>
                  <FolderTree />
                </template>
              </EmptyState>
            </div>

            <div
              v-else-if="isLoadingFields"
              class="flex min-h-48 flex-col items-center justify-center gap-2 rounded-lg border py-8 text-muted-foreground"
            >
              <Loader2 class="size-6 animate-spin" />
              <span class="text-sm">Загрузка полей категории...</span>
            </div>

            <template v-else-if="hasImportConfig">
              <SettingsFormSection
                class="!max-w-none"
                title="Куда импортировать"
                description="Создайте новую локальную категорию или дополните существующую."
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
                        Создать новую категорию
                      </label>
                    </div>
                    <div v-if="importMode === 'new'" class="pl-7">
                      <SettingsFormField label="Название категории">
                        <Input
                          v-model="importCategoryName"
                          placeholder="Оставьте пустым — возьмём название из адаптера"
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
                        В существующую категорию
                      </label>
                    </div>
                    <div v-if="importMode === 'existing'" class="pl-7">
                      <SettingsFormField label="Локальная категория">
                        <SearchableSelect
                          :model-value="importTargetCategoryId"
                          :options="localCategoryOptions"
                          placeholder="Выберите категорию..."
                          search-placeholder="Поиск категории..."
                          empty-text="Категории не найдены"
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
                    {{ importBaseFields.length }} базовых полей
                  </span>
                  — SKU, название, описание, цена, остатки и изображения — будут
                  автоматически связаны с полями товара.
                </p>
              </SettingsFormSection>

              <SettingsFormSection
                class="!max-w-none"
                :title="`Атрибуты для импорта (${importSelectedFieldKeys.length} / ${importAttributeFields.length})`"
                description="Выберите дополнительные поля категории, которые нужно перенести."
              >
                <div class="flex justify-end">
                  <Button variant="ghost" size="sm" @click="toggleAllFields">
                    {{ allAttributeFieldsSelected ? "Снять все" : "Выбрать все" }}
                  </Button>
                </div>

                <div
                  v-if="importAttributeFields.length === 0"
                  class="rounded-lg border border-dashed px-4 py-8 text-center text-sm text-muted-foreground"
                >
                  Нет дополнительных атрибутов для импорта
                </div>

                <div
                  v-else
                  class="max-h-80 overflow-y-auto rounded-lg border p-1.5"
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
                        обяз.
                      </Badge>
                      <span
                        v-if="field.optionsCount > 0"
                        class="text-xs tabular-nums text-muted-foreground"
                      >
                        {{ field.optionsCount }} опц.
                      </span>
                    </div>
                  </button>
                </div>
              </SettingsFormSection>
            </template>
          </div>
        </div>
      </div>
    </PageSection>
  </Page>
</template>