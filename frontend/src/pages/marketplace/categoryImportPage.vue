<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  ArrowLeft,
  Loader2,
  ChevronRight,
  ChevronLeft,
  Home,
  Search,
  Sparkles,
  Link2,
  Download,
} from "lucide-vue-next";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import Separator from "@/components/ui/separator/Separator.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { PAGE_MARKETPLACE, PAGE_MARKETPLACE_CONNECTION } from "@/router";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Label,
    Badge,
    Checkbox,
    ArrowLeft,
    Loader2,
    ChevronRight,
    ChevronLeft,
    Home,
    Search,
    Sparkles,
    Link2,
    Download,
    Card,
    CardContent,
    CardHeader,
    CardTitle,
    RadioGroup,
    RadioGroupItem,
    Separator,
    SearchableSelect,
  },
  data() {
    return {
      connection: null,
      localCategories: [],
      externalCategories: [],
      isLoading: false,

      // Import state
      importMode: "new",
      importTargetCategoryId: null,
      importCategoryId: "",
      importCategoryName: "",
      importCategoryExternalName: "",
      importExternalFields: [],
      importSelectedFieldKeys: [],
      isLoadingFields: false,
      isImporting: false,

      // Category navigation
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
        { label: this.connection?.name || "Подключение", to: `/adapter/${this.connectionId}` },
        { label: "Импорт категории" },
      ];
    },
    importAttributeFields() {
      return this.importExternalFields.filter(f => !f.isBaseField);
    },
    importBaseFields() {
      return this.importExternalFields.filter(f => f.isBaseField);
    },
    allAttributeFieldsSelected() {
      return this.importAttributeFields.length > 0 &&
        this.importSelectedFieldKeys.length === this.importAttributeFields.length;
    },
    importButtonDisabled() {
      if (this.isImporting) return true;
      if (!this.importCategoryId) return true;
      if (this.importMode === "existing" && !this.importTargetCategoryId) return true;
      return false;
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
      await Promise.all([
        this.fetchConnection(),
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
      this.currentParentId = this.categoryPath.length > 0
        ? this.categoryPath[this.categoryPath.length - 1].id
        : null;
      await this.searchExternalCategories();
    },

    selectExternalCategory(category) {
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
          this.importSelectedFieldKeys = this.importAttributeFields.map(f => f.key);
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
        this.importSelectedFieldKeys = this.importAttributeFields.map(f => f.key);
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
          localCategoryName: this.importMode === "new" ? (this.importCategoryName || null) : null,
          localCategoryId: this.importMode === "existing" ? parseInt(this.importTargetCategoryId) : null,
          selectedFieldKeys: this.importSelectedFieldKeys.length > 0 ? this.importSelectedFieldKeys : null,
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

    <PageSection :loading="isLoading" class="flex flex-col space-y-4">
    <div class="flex flex-col flex-1 space-y-4">
      <!-- Header -->
      <div class="flex items-center gap-4">
        <Button variant="ghost" size="icon" @click="$router.push(`/adapter/${connectionId}?tab=mappings`)">
          <ArrowLeft class="size-4" />
        </Button>
        <div>
          <h1 class="text-lg font-semibold">Импорт категории</h1>
          <p class="text-sm text-muted-foreground">{{ connection?.name }}</p>
        </div>
      </div>

      <!-- Card: Category browser -->
      <Card>
        <CardHeader>
          <CardTitle>Категория адаптера</CardTitle>
        </CardHeader>
        <CardContent class="space-y-4">
          <!-- Search -->
          <div class="flex gap-2">
            <Input
              v-model="externalCategorySearch"
              placeholder="Поиск по названию..."
              @keyup.enter="searchExternalCategories"
            />
            <Button variant="outline" size="icon" @click="searchExternalCategories">
              <Search class="size-4" />
            </Button>
          </div>

          <!-- Breadcrumb path -->
          <div v-if="categoryPath.length > 0 && !externalCategorySearch" class="flex items-center gap-1 text-sm flex-wrap">
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
              <ChevronRight v-if="index < categoryPath.length - 1" class="size-3 text-muted-foreground" />
            </template>
          </div>

          <!-- Back button -->
          <Button
            v-if="categoryPath.length > 0 && !externalCategorySearch"
            variant="outline"
            size="sm"
            @click="navigateBack"
            class="w-full justify-start"
          >
            <ChevronLeft class="size-4 mr-2" />
            Назад
          </Button>

          <!-- Category list -->
          <div class="border rounded-lg max-h-64 overflow-y-auto">
            <div v-if="isLoadingCategories" class="flex justify-center py-8">
              <Loader2 class="size-6 animate-spin text-muted-foreground" />
            </div>
            <div v-else-if="externalCategories.length === 0" class="text-center py-8 text-muted-foreground text-sm">
              Категории не найдены
            </div>
            <div
              v-else
              v-for="cat in externalCategories"
              :key="cat.id"
              class="p-2 hover:bg-accent cursor-pointer border-b last:border-b-0 flex items-center justify-between"
              :class="{ 'bg-accent': importCategoryId === cat.id }"
              @click="cat.hasChildren && !externalCategorySearch ? navigateToCategory(cat) : selectExternalCategory(cat)"
            >
              <div class="flex-1 min-w-0">
                <div class="font-medium truncate">{{ cat.name }}</div>
                <div v-if="cat.fullPath" class="text-xs text-muted-foreground truncate">{{ cat.fullPath }}</div>
              </div>
              <ChevronRight v-if="cat.hasChildren && !externalCategorySearch" class="size-4 text-muted-foreground flex-shrink-0 ml-2" />
            </div>
          </div>

          <!-- Manual ID input -->
          <div class="space-y-2">
            <Label>ID категории адаптера</Label>
            <Input v-model="importCategoryId" placeholder="Выберите категорию выше или введите ID" />
          </div>
        </CardContent>
      </Card>

      <!-- Loading fields spinner -->
      <div v-if="isLoadingFields" class="flex items-center justify-center py-6 gap-2 text-muted-foreground">
        <Loader2 class="size-5 animate-spin" />
        <span class="text-sm">Загрузка полей категории...</span>
      </div>

      <!-- Card: Import settings (shown when fields loaded) -->
      <template v-if="importExternalFields.length > 0 && !isLoadingFields">
        <Card>
          <CardHeader>
            <CardTitle>Настройки импорта</CardTitle>
          </CardHeader>
          <CardContent class="space-y-4">
            <!-- Mode selector -->
            <div class="space-y-3">
              <Label>Куда импортировать</Label>
              <RadioGroup v-model="importMode" class="space-y-3">
                <div class="flex items-center gap-3">
                  <RadioGroupItem value="new" id="import-mode-new" />
                  <Label for="import-mode-new" class="flex items-center gap-2 cursor-pointer">
                    <Sparkles class="size-4 text-amber-500" />
                    Создать новую категорию
                  </Label>
                </div>
                <div v-if="importMode === 'new'" class="ml-8">
                  <Input v-model="importCategoryName" placeholder="Название (оставьте пустым для автоматического)" />
                </div>

                <div class="flex items-center gap-3">
                  <RadioGroupItem value="existing" id="import-mode-existing" />
                  <Label for="import-mode-existing" class="flex items-center gap-2 cursor-pointer">
                    <Link2 class="size-4 text-blue-500" />
                    В существующую категорию
                  </Label>
                </div>
                <div v-if="importMode === 'existing'" class="ml-8">
                  <SearchableSelect
                    :model-value="importTargetCategoryId"
                    @update:model-value="importTargetCategoryId = $event"
                    :options="localCategories.map(c => ({ value: c.id, label: c.name + ' (' + c.attributesCount + ' атр.)' }))"
                    placeholder="Выберите категорию..."
                    search-placeholder="Поиск категории..."
                    empty-text="Категории не найдены"
                  />
                </div>
              </RadioGroup>
            </div>

            <!-- Base fields info -->
            <div v-if="importBaseFields.length > 0" class="text-sm text-muted-foreground bg-muted/50 rounded-lg p-3">
              <span class="font-medium">{{ importBaseFields.length }} базовых полей</span>
              (SKU, название, описание, цена, остатки, изображения) будут автоматически связаны с полями товара.
            </div>
          </CardContent>
        </Card>

        <!-- Card: Attributes to import -->
        <Card>
          <CardHeader>
            <div class="flex items-center justify-between">
              <CardTitle>Атрибуты для импорта ({{ importSelectedFieldKeys.length }} / {{ importAttributeFields.length }})</CardTitle>
              <Button variant="ghost" size="sm" @click="toggleAllFields" class="text-xs h-7">
                {{ allAttributeFieldsSelected ? 'Снять все' : 'Выбрать все' }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div class="border rounded-lg max-h-80 overflow-y-auto divide-y">
              <div
                v-for="field in importAttributeFields"
                :key="field.key"
                class="flex items-center gap-3 px-3 py-2 hover:bg-accent/50 cursor-pointer"
                @click="toggleFieldSelection(field.key)"
              >
                <Checkbox
                  :model-value="importSelectedFieldKeys.includes(field.key)"
                  @update:model-value="toggleFieldSelection(field.key)"
                  @click.stop
                />
                <div class="flex-1 min-w-0">
                  <div class="text-sm font-medium truncate">{{ field.name }}</div>
                  <div v-if="field.description" class="text-xs text-muted-foreground truncate">{{ field.description }}</div>
                </div>
                <div class="flex items-center gap-1.5 shrink-0">
                  <Badge variant="outline" class="text-xs">{{ field.type }}</Badge>
                  <Badge v-if="field.isRequired" variant="destructive" class="text-xs">обяз.</Badge>
                  <span v-if="field.optionsCount > 0" class="text-xs text-muted-foreground">
                    {{ field.optionsCount }} опц.
                  </span>
                </div>
              </div>
              <div v-if="importAttributeFields.length === 0" class="text-center py-6 text-muted-foreground text-sm">
                Нет дополнительных атрибутов
              </div>
            </div>
          </CardContent>
        </Card>
      </template>

      <!-- Import button -->
      <div v-if="importExternalFields.length > 0 && !isLoadingFields" class="flex justify-end">
        <Button @click="handleImportCategory" :disabled="importButtonDisabled" size="lg">
          <Loader2 v-if="isImporting" class="size-4 mr-2 animate-spin" />
          <Download v-else class="size-4 mr-2" />
          Импортировать
          <template v-if="importSelectedFieldKeys.length > 0">
            ({{ importSelectedFieldKeys.length }} атр.)
          </template>
        </Button>
      </div>
    </div>
    </PageSection>
  </Page>
</template>
