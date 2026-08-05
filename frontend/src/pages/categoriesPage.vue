<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
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
    AppPageChrome,
    PageSection,
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
      breadcrumbs: [{ label: this.$t("pages.categoriesPage.breadcrumb") }],
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
        displaySonnerError(this.$t("pages.categoriesPage.nameRequired"));
        return;
      }

      this.isCreating = true;
      await this.createCategory({
        name: this.newCategoryName.trim(),
        description: this.newCategoryDescription.trim() || null,
        onSuccess: (response) => {
          displaySonnerSuccess(this.$t("pages.categoriesPage.created"));
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
          displaySonnerSuccess(this.$t("pages.categoriesPage.deleted"));
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
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <div class="mb-4 flex flex-wrap items-center justify-end gap-2">
      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            {{ $t("pages.categoriesPage.addCategory") }}
          </Button>
        </DialogTrigger>
        <DialogContent size="md">
          <DialogHeader>
            <DialogTitle>{{ $t("pages.categoriesPage.newCategoryTitle") }}</DialogTitle>
            <DialogDescription>
              {{ $t("pages.categoriesPage.newCategoryDescription") }}
            </DialogDescription>
          </DialogHeader>
          <div class="py-4 space-y-4">
            <Input
              v-model="newCategoryName"
              :placeholder="$t('pages.categoriesPage.namePlaceholder')"
              @keyup.enter="handleCreateCategory"
            />
            <Input
              v-model="newCategoryDescription"
              :placeholder="$t('pages.categoriesPage.descriptionPlaceholder')"
            />
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button variant="outline">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button @click="handleCreateCategory" :disabled="isCreating">
              <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
              {{ $t("common.create") }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="categories.length === 0"
      :empty-icon="FolderOpen"
      :empty-title="$t('pages.categoriesPage.emptyTitle')"
      :empty-description="$t('pages.categoriesPage.emptyDescription')"
    >
      <template #empty-action>
        <Button @click="showCreateDialog = true">
          <Plus class="size-4 mr-2" />
          {{ $t("pages.categoriesPage.addCategory") }}
        </Button>
      </template>

      <div class="rounded-lg border overflow-hidden">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead>{{ $t("common.name") }}</TableHead>
                <TableHead class="hidden md:table-cell">{{ $t("common.description") }}</TableHead>
                <TableHead class="w-28 text-right">{{ $t("pages.categoriesPage.productsHeader") }}</TableHead>
                <TableHead class="w-32 text-right">{{ $t("pages.categoriesPage.attributesHeader") }}</TableHead>
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
                  {{ category.description || $t("common.emDash") }}
                </TableCell>
                <TableCell class="text-right tabular-nums text-muted-foreground">
                  {{ category.productsCount || 0 }}
                </TableCell>
                <TableCell class="text-right tabular-nums text-muted-foreground">
                  {{ category.attributesCount || 0 }}
                </TableCell>
                <TableCell class="px-2">
                  <ConfirmDialog
                    :title="$t('pages.categoriesPage.deleteTitle')"
                    :description="$t('pages.categoriesPage.deleteDescription', { name: category.name })"
                    :confirm-label="$t('common.delete')"
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
  </AppPageChrome>
</template>
