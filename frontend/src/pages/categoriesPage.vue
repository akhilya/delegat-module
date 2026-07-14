<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, FolderOpen, Loader2, Trash2 } from "lucide-vue-next";
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
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Plus,
    FolderOpen,
    Loader2,
    Trash2,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
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
        <DialogContent size="md">
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

      <div class="rounded-lg border overflow-hidden">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead>Название</TableHead>
                <TableHead class="hidden md:table-cell">Описание</TableHead>
                <TableHead class="w-28 text-right">Товары</TableHead>
                <TableHead class="w-32 text-right">Атрибуты</TableHead>
                <TableHead class="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="category in categories"
                :key="category.id"
                class="group cursor-pointer"
                @click="openCategory(category.id)"
              >
                <TableCell class="font-medium max-w-0">
                  <span class="truncate block">{{ category.name }}</span>
                  <span
                    v-if="category.description"
                    class="text-xs text-muted-foreground truncate block md:hidden"
                  >
                    {{ category.description }}
                  </span>
                </TableCell>
                <TableCell class="hidden md:table-cell text-muted-foreground max-w-xs truncate">
                  {{ category.description || "—" }}
                </TableCell>
                <TableCell class="text-right tabular-nums text-muted-foreground">
                  {{ category.productsCount || 0 }}
                </TableCell>
                <TableCell class="text-right tabular-nums text-muted-foreground">
                  {{ category.attributesCount || 0 }}
                </TableCell>
                <TableCell class="px-2">
                  <ConfirmDialog
                    title="Удалить категорию?"
                    :description="`Вы уверены что хотите удалить категорию &quot;${category.name}&quot;? Это действие нельзя отменить.`"
                    confirm-label="Удалить"
                    :loading="isDeleting"
                    @confirm="handleDeleteCategory(category.id)"
                  >
                    <template #trigger>
                      <Button
                        variant="ghost"
                        size="icon"
                        class="size-8 opacity-0 group-hover:opacity-100 transition-opacity"
                        @click.stop
                      >
                        <Trash2 class="size-4 text-destructive" />
                      </Button>
                    </template>
                  </ConfirmDialog>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </PageSection>
  </Page>
</template>
