<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, FolderOpen, Loader2, ChevronRight, Package, Trash2, ShoppingBag } from "lucide-vue-next";
import { PAGE_PRODUCTS } from "@/router";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import { PageHeader } from "@/components/ui/page-header";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import {
  Card,
} from "@/components/ui/card";
import { PAGE_CATEGORY_EDIT } from "@/router";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Plus,
    FolderOpen,
    Loader2,
    ChevronRight,
    Package,
    Trash2,
    ShoppingBag,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    PageHeader,
    ConfirmDialog,
    Card,
  },
  data() {
    return {
      categories: [],
      isLoading: false,
      isCreating: false,
      isDeleting: false,
      newCategoryName: "",
      newCategoryDescription: "",
      showCreateDialog: false,
      categoryToDelete: null,
      breadcrumbs: [{ label: "Категории" }],
      PAGE_PRODUCTS,
    };
  },
  methods: {
    ...mapActions(["getAllCategories", "createCategory", "deleteCategory"]),

    async fetchCategories() {
      this.isLoading = true;
      await this.getAllCategories({
        onSuccess: (response) => {
          this.categories = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async handleCreateCategory() {
      if (!this.newCategoryName.trim()) {
        displaySonnerError("Введите название категории");
        return;
      }

      this.isCreating = true;
      await this.createCategory({
        name: this.newCategoryName.trim(),
        description: this.newCategoryDescription.trim() || null,
        onSuccess: (response) => {
          displaySonnerSuccess("Категория создана");
          this.newCategoryName = "";
          this.newCategoryDescription = "";
          this.showCreateDialog = false;
          // Navigate to edit page
          this.$router.push(response.data.value.next);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async handleDeleteCategory(categoryId) {
      this.isDeleting = true;
      await this.deleteCategory({
        id: categoryId,
        onSuccess: () => {
          displaySonnerSuccess("Категория удалена");
          this.fetchCategories();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeleting = false;
      this.categoryToDelete = null;
    },

    openCategory(categoryId) {
      this.$router.push(`/categories/${categoryId}/edit`);
    },
  },
  mounted() {
    this.fetchCategories();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader title="Категории" description="Управление категориями товаров и их атрибутами">
      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            Добавить категорию
          </Button>
        </DialogTrigger>
        <DialogContent class="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Новая категория</DialogTitle>
            <DialogDescription>
              Введите название и описание для новой категории
            </DialogDescription>
          </DialogHeader>
          <div class="py-4 space-y-4">
            <Input
              v-model="newCategoryName"
              placeholder="Название категории"
              @keyup.enter="handleCreateCategory"
            />
            <Input
              v-model="newCategoryDescription"
              placeholder="Описание (необязательно)"
            />
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button variant="outline">Отмена</Button>
            </DialogClose>
            <Button @click="handleCreateCategory" :disabled="isCreating">
              <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
              Создать
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </PageHeader>

    <PageSection
      :loading="isLoading"
      :empty="categories.length === 0"
      :empty-icon="FolderOpen"
      empty-title="Нет категорий"
      empty-description="Создайте первую категорию для товаров"
    >
      <template #empty-action>
        <Button @click="showCreateDialog = true">
          <Plus class="size-4 mr-2" />
          Добавить категорию
        </Button>
      </template>

      <Card class="p-0 gap-0 overflow-hidden">
        <ul class="divide-y divide-border/60">
          <li
            v-for="category in categories"
            :key="category.id"
            class="flex items-center gap-4 px-5 py-3.5 hover:bg-muted/40 cursor-pointer group transition-colors"
            @click="openCategory(category.id)"
          >
            <FolderOpen class="size-5 text-muted-foreground shrink-0" />
            <div class="min-w-0 flex-1">
              <div class="font-medium truncate">{{ category.name }}</div>
              <div v-if="category.description" class="text-sm text-muted-foreground truncate">{{ category.description }}</div>
            </div>
            <div class="flex items-center gap-4 text-sm text-muted-foreground shrink-0 tabular-nums">
              <span class="flex items-center gap-1.5" title="Товаров в категории">
                <Package class="size-4" />
                {{ category.productsCount || 0 }}
              </span>
              <span :title="`${category.attributesCount || 0} атрибутов`">
                {{ category.attributesCount || 0 }} атр.
              </span>
            </div>
            <ConfirmDialog
              title="Удалить категорию?"
              :description="`Вы уверены что хотите удалить категорию &quot;${category.name}&quot;? Это действие нельзя отменить.`"
              confirm-label="Удалить"
              :loading="isDeleting"
              @confirm="handleDeleteCategory(category.id)"
            >
              <template #trigger>
                <Button variant="ghost" size="icon" class="opacity-0 group-hover:opacity-100 transition-opacity shrink-0" @click.stop>
                  <Trash2 class="size-4 text-destructive" />
                </Button>
              </template>
            </ConfirmDialog>
            <ChevronRight class="size-4 text-muted-foreground/60 shrink-0" />
          </li>
        </ul>
      </Card>
    </PageSection>
  </Page>
</template>
