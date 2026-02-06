<script>
import Page from "@/components/page.vue";
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
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { PAGE_CATEGORY_EDIT } from "@/router";

export default {
  components: {
    Page,
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
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogTrigger,
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
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

    <div class="flex justify-between items-center mb-4">
      <div>
        <h1 class="text-lg font-semibold">Категории</h1>
        <p class="text-sm text-muted-foreground">
          Управление категориями товаров и их атрибутами
        </p>
      </div>

      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            Добавить категорию
          </Button>
        </DialogTrigger>
        <DialogContent>
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
    </div>

    <div class="border rounded-lg h-full w-full grow mb-4 p-4">
      <div v-if="isLoading" class="flex justify-center py-12">
        <Loader2 class="size-8 animate-spin text-muted-foreground" />
      </div>

      <div v-else-if="categories.length === 0" class="flex flex-col items-center justify-center py-12 text-center">
        <FolderOpen class="size-12 mb-4 text-muted-foreground" />
        <h3 class="font-medium mb-1">Нет категорий</h3>
        <p class="text-sm text-muted-foreground">Создайте первую категорию для товаров</p>
      </div>

      <div v-else class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <Card
          v-for="category in categories"
          :key="category.id"
          class="cursor-pointer hover:bg-accent/50 transition-colors group"
        >
          <CardHeader @click="openCategory(category.id)">
            <CardTitle class="flex items-center justify-between">
              <span class="flex items-center gap-2">
                <FolderOpen class="size-5" />
                {{ category.name }}
              </span>
              <ChevronRight class="size-5 text-muted-foreground" />
            </CardTitle>
            <CardDescription v-if="category.description">
              {{ category.description }}
            </CardDescription>
          </CardHeader>
          <CardContent class="flex justify-between items-center">
            <div class="flex items-center gap-4 text-sm text-muted-foreground">
              <span class="flex items-center gap-1">
                <Package class="size-4" />
                {{ category.productsCount || 0 }} товаров
              </span>
              <span>
                {{ category.attributesCount || 0 }} атрибутов
              </span>
            </div>
            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button
                  variant="ghost"
                  size="icon"
                  class="opacity-0 group-hover:opacity-100 transition-opacity"
                  @click.stop
                >
                  <Trash2 class="size-4 text-destructive" />
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Удалить категорию?</AlertDialogTitle>
                  <AlertDialogDescription>
                    Вы уверены что хотите удалить категорию "{{ category.name }}"?
                    Это действие нельзя отменить.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Отмена</AlertDialogCancel>
                  <AlertDialogAction
                    @click="handleDeleteCategory(category.id)"
                    :disabled="isDeleting"
                  >
                    <Loader2 v-if="isDeleting" class="size-4 mr-2 animate-spin" />
                    Удалить
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </CardContent>
        </Card>
      </div>
    </div>
  </Page>
</template>
