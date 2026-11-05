<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, FolderOpen, Trash2 } from "lucide-vue-next";
import { PAGE_PRODUCTS } from "@/router";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
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
import ListSelectAllRow from "@/components/lists/ListSelectAllRow.vue";
import ListBulkActions from "@/components/lists/ListBulkActions.vue";
import { Checkbox } from "@/components/ui/checkbox";
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogCancel,
} from "@/components/ui/alert-dialog";
import { http } from "@/main";
import { canAct, PERMS } from "@/lib/permissions";
import { getCachedMe } from "@/lib/meCache";

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    Button,
    Input,
    SettingsFormField,
    FieldGroup,
    InsetGroup,
    InsetGroupItem,
    Plus,
    Trash2,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    ConfirmDialog,
    ListSelectAllRow,
    ListBulkActions,
    Checkbox,
    AlertDialog,
    AlertDialogContent,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogCancel,
  },
  data() {
    return {
      categories: [],
      emptyListIcon: FolderOpen,
      isLoading: false,
      isCreating: false,
      isDeleting: false,
      newCategoryName: "",
      newCategoryDescription: "",
      showCreateDialog: false,
      categoryToDelete: null,
      selectedIds: [],
      showBulkDeleteDialog: false,
      FIELD_LIMITS,
      PAGE_PRODUCTS,
    };
  },
  computed: {
    canManageCatalog() {
      return canAct(getCachedMe(), PERMS.CatalogManage);
    },
    selectionState() {
      return this.categories.length > 0 &&
        this.selectedIds.length === this.categories.length
        ? true
        : this.selectedIds.length
          ? "indeterminate"
          : false;
    },
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
      const nameIssue = fieldLimitIssue(
        this.newCategoryName,
        FIELD_LIMITS.categoryName,
      );
      if (nameIssue === "empty") {
        displaySonnerError(this.$t("pages.categoriesPage.nameRequired"));
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.categoryName }),
        );
        return;
      }
      const descIssue = fieldLimitIssue(
        this.newCategoryDescription,
        FIELD_LIMITS.categoryDescription,
        { required: false },
      );
      if (descIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.categoryDescription }),
        );
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
      if (this.isDeleting) return;
      this.isDeleting = true;
      await this.deleteCategory({
        id: categoryId,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.categoriesPage.deleted"));
          this.categories = this.categories.filter(
            (category) => category.id !== categoryId,
          );
          this.selectedIds = this.selectedIds.filter((id) => id !== categoryId);
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

    toggleCategory(categoryId, checked) {
      if (this.isDeleting) return;
      this.selectedIds =
        checked === true
          ? [...new Set([...this.selectedIds, categoryId])]
          : this.selectedIds.filter((id) => id !== categoryId);
    },

    selectAllCategories(checked) {
      if (!this.isDeleting)
        this.selectedIds =
          checked === true
            ? this.categories.map((category) => category.id)
            : [];
    },

    async handleBulkDelete() {
      if (
        !this.canManageCatalog ||
        this.isDeleting ||
        !this.selectedIds.length ||
        this.selectedIds.length > 1000
      )
        return;
      const categoryIds = [...this.selectedIds];
      this.isDeleting = true;
      try {
        await http.post(
          "/categories/bulk-delete",
          { categoryIds },
          { cache: false },
        );
        const deleted = new Set(categoryIds);
        this.categories = this.categories.filter(
          (category) => !deleted.has(category.id),
        );
        this.selectedIds = this.selectedIds.filter((id) => !deleted.has(id));
        this.showBulkDeleteDialog = false;
        displaySonnerSuccess(
          this.$t("pages.categoriesPage.deletedCount", {
            count: categoryIds.length,
          }),
        );
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.isDeleting = false;
      }
    },
  },
  mounted() {
    this.fetchCategories();
  },
};
</script>

<template>
  <AppPageChrome>
    <div class="flex w-full max-w-xl flex-col">
      <div class="mb-6 flex flex-wrap items-center justify-start gap-2">
        <Dialog v-model:open="showCreateDialog">
          <DialogTrigger as-child>
            <Button>
              <Plus class="size-4 mr-2" />
              {{ $t("pages.categoriesPage.addCategory") }}
            </Button>
          </DialogTrigger>
          <DialogContent
            size="md"
            class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
          >
            <DialogHeader variant="bar">
              <DialogTitle>{{
                $t("pages.categoriesPage.newCategoryTitle")
              }}</DialogTitle>
              <DialogDescription>
                {{ $t("pages.categoriesPage.newCategoryDescription") }}
              </DialogDescription>
            </DialogHeader>
            <form
              id="new-category-form"
              class="min-h-0 flex-1 overflow-y-auto px-6 py-5"
              @submit.prevent="handleCreateCategory"
            >
              <FieldGroup class="gap-5">
                <SettingsFormField
                  :label="$t('common.name')"
                  label-for="new-category-name"
                >
                  <Input
                    id="new-category-name"
                    v-model="newCategoryName"
                    :placeholder="$t('pages.categoriesPage.namePlaceholder')"
                    :maxlength="FIELD_LIMITS.categoryName"
                  />
                </SettingsFormField>
                <SettingsFormField
                  :label="$t('common.description')"
                  label-for="new-category-description"
                >
                  <Input
                    id="new-category-description"
                    v-model="newCategoryDescription"
                    :placeholder="
                      $t('pages.categoriesPage.descriptionPlaceholder')
                    "
                    :maxlength="FIELD_LIMITS.categoryDescription"
                  />
                </SettingsFormField>
              </FieldGroup>
            </form>
            <DialogFooter variant="bar">
              <DialogClose as-child>
                <Button type="button" variant="outline">{{
                  $t("common.cancel")
                }}</Button>
              </DialogClose>
              <Button
                type="submit"
                form="new-category-form"
                :disabled="isCreating"
              >
                <Spinner v-if="isCreating" class="mr-2" />
                {{ $t("common.create") }}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>

      <PageSection
        :loading="isLoading"
        :empty="categories.length === 0"
        :empty-icon="emptyListIcon"
        :empty-title="$t('pages.categoriesPage.emptyTitle')"
        :empty-description="$t('pages.categoriesPage.emptyDescription')"
      >
        <template #empty-action>
          <Button @click="showCreateDialog = true">
            <Plus class="size-4 mr-2" />
            {{ $t("pages.categoriesPage.addCategory") }}
          </Button>
        </template>

        <InsetGroup>
          <ListSelectAllRow
            v-if="canManageCatalog"
            :model-value="selectionState"
            :disabled="isDeleting"
            @update:model-value="selectAllCategories"
          />
          <InsetGroupItem
            v-for="category in categories"
            :key="category.id"
            show-chevron
            @click="openCategory(category.id)"
          >
            <template v-if="canManageCatalog" #leading>
              <span @click.stop
                ><Checkbox
                  :model-value="selectedIds.includes(category.id)"
                  :disabled="isDeleting"
                  :aria-label="category.name"
                  @update:model-value="toggleCategory(category.id, $event)"
              /></span>
            </template>
            <p class="truncate">{{ category.name }}</p>
            <p
              class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
            >
              <template v-if="category.description">
                {{ category.description }}
                ·
              </template>
              {{
                $t("pages.categoriesPage.rowMeta", {
                  products: category.productsCount || 0,
                  attributes: category.attributesCount || 0,
                })
              }}
            </p>
            <template v-if="canManageCatalog" #trailing>
              <ConfirmDialog
                :title="$t('pages.categoriesPage.deleteTitle')"
                :description="
                  $t('pages.categoriesPage.deleteDescription', {
                    name: category.name,
                  })
                "
                :confirm-label="$t('common.delete')"
                :loading="isDeleting"
                @confirm="handleDeleteCategory(category.id)"
              >
                <template #trigger>
                  <Button
                    variant="ghost"
                    reveal-on-row-hover
                    :disabled="isDeleting"
                    size="icon"
                    class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                    :aria-label="$t('common.delete')"
                    @click.stop
                  >
                    <Trash2 class="size-4" />
                  </Button>
                </template>
              </ConfirmDialog>
            </template>
          </InsetGroupItem>
        </InsetGroup>
        <ListBulkActions
          v-if="canManageCatalog"
          class="mt-3"
          :count="selectedIds.length"
          :busy="isDeleting"
          :disabled="selectedIds.length > 1000"
          @clear="selectedIds = []"
          @delete="showBulkDeleteDialog = true"
        />
        <p
          v-if="selectedIds.length > 1000"
          role="alert"
          class="mt-2 px-1 text-xs text-destructive"
        >
          {{ $t("components.listSelection.limit", { count: 1000 }) }}
        </p>
      </PageSection>

      <AlertDialog
        :open="showBulkDeleteDialog"
        @update:open="!isDeleting && (showBulkDeleteDialog = $event)"
      >
        <AlertDialogContent
          @escape-key-down="isDeleting && $event.preventDefault()"
        >
          <AlertDialogHeader>
            <AlertDialogTitle>{{
              $t("pages.categoriesPage.bulkDeleteTitle")
            }}</AlertDialogTitle>
            <AlertDialogDescription>{{
              $t("pages.categoriesPage.bulkDeleteDescription", {
                count: selectedIds.length,
              })
            }}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel :disabled="isDeleting">{{
              $t("common.cancel")
            }}</AlertDialogCancel>
            <Button
              variant="destructive"
              size="lg"
              :disabled="isDeleting"
              @click="handleBulkDelete"
              ><Spinner v-if="isDeleting" class="mr-2" />{{
                $t("common.delete")
              }}</Button
            >
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  </AppPageChrome>
</template>
