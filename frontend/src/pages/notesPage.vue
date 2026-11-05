<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import PageListToolbar from "@/components/PageListToolbar.vue";
import FilterMenuGroup from "@/components/FilterMenuGroup.vue";
import {
  NotebookPen,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
} from "lucide-vue-next";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import NoteDetailSheet from "@/components/notes/NoteDetailSheet.vue";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import {
  noteImportanceClass,
  noteImportanceLabel,
  noteListCaption,
  notePreviewTitle,
} from "@/lib/notesUi";

const PAGE_SIZE = 25;
const AUTHOR_UNKNOWN = "__unknown__";
const AUTHOR_ALL = "__all__";
const IMPORTANCE_ALL = "__all__";

export default {
  components: {
    AppPageChrome,
    PageSection,
    Button,
    PageListToolbar,
    FilterMenuGroup,
    InsetGroup,
    InsetGroupItem,
    NotebookPen,
    ChevronLeft,
    ChevronRight,
    ChevronsLeft,
    ChevronsRight,
    NoteDetailSheet,
  },
  setup() {
    const { formatDateTime: formatStoreDateTime, loadMemberships } =
      useStoreDateTime();
    loadMemberships();
    return { formatStoreDateTime };
  },
  data() {
    return {
      items: [],
      authors: [],
      hasUnknownAuthor: false,
      totalCount: 0,
      startIndex: 0,
      searchQuery: "",
      importanceFilter: IMPORTANCE_ALL,
      authorFilter: AUTHOR_ALL,
      sortBy: "createdAt",
      sortDesc: true,
      isLoading: false,
      sheetOpen: false,
      selectedNote: null,
      IMPORTANCE_ALL,
      AUTHOR_ALL,
      AUTHOR_UNKNOWN,
    };
  },
  computed: {
    importanceOptions() {
      return [
        { value: "Low", label: this.$t("components.note.importanceLow") },
        { value: "Medium", label: this.$t("components.note.importanceMedium") },
        { value: "High", label: this.$t("components.note.importanceHigh") },
        {
          value: "Critical",
          label: this.$t("components.note.importanceCritical"),
        },
        {
          value: "Emergency",
          label: this.$t("components.note.importanceEmergency"),
        },
      ];
    },
    authorOptions() {
      return [
        { value: AUTHOR_ALL, label: this.$t("pages.notesPage.authorAll") },
        ...(this.hasUnknownAuthor
          ? [
              {
                value: AUTHOR_UNKNOWN,
                label: this.$t("pages.notesPage.authorUnknown"),
              },
            ]
          : []),
        ...this.authors.map((author) => ({
          value: String(author.id),
          label: author.userName || `#${author.id}`,
        })),
      ];
    },
    sortOptions() {
      return [
        {
          value: "createdAt:desc",
          label: this.$t("pages.notesPage.sortCreatedDesc"),
        },
        {
          value: "createdAt:asc",
          label: this.$t("pages.notesPage.sortCreatedAsc"),
        },
        {
          value: "updatedAt:desc",
          label: this.$t("pages.notesPage.sortUpdatedDesc"),
        },
        {
          value: "importance:desc",
          label: this.$t("pages.notesPage.sortImportanceDesc"),
        },
        {
          value: "importance:asc",
          label: this.$t("pages.notesPage.sortImportanceAsc"),
        },
        {
          value: "product:asc",
          label: this.$t("pages.notesPage.sortProductAsc"),
        },
        {
          value: "product:desc",
          label: this.$t("pages.notesPage.sortProductDesc"),
        },
      ];
    },
    sortSelectValue() {
      return `${this.sortBy}:${this.sortDesc ? "desc" : "asc"}`;
    },
    hasActiveFilters() {
      return Boolean(
        this.searchQuery.trim() ||
        this.importanceFilter !== IMPORTANCE_ALL ||
        this.authorFilter !== AUTHOR_ALL ||
        this.sortBy !== "createdAt" ||
        this.sortDesc !== true,
      );
    },
    activeFilterCount() {
      return (
        Number(this.importanceFilter !== IMPORTANCE_ALL) +
        Number(this.authorFilter !== AUTHOR_ALL) +
        Number(this.sortSelectValue !== "createdAt:desc")
      );
    },
    pageFrom() {
      if (this.totalCount === 0) return 0;
      return this.startIndex + 1;
    },
    pageTo() {
      return Math.min(this.startIndex + PAGE_SIZE, this.totalCount);
    },
    canPrev() {
      return this.startIndex > 0;
    },
    canNext() {
      return this.startIndex + PAGE_SIZE < this.totalCount;
    },
    currentPage() {
      return Math.floor(this.startIndex / PAGE_SIZE) + 1;
    },
    lastPage() {
      return Math.max(1, Math.ceil(this.totalCount / PAGE_SIZE) || 1);
    },
    emptyTitle() {
      return this.hasActiveFilters
        ? this.$t("pages.notesPage.emptyFilteredTitle")
        : this.$t("pages.notesPage.emptyTitle");
    },
    emptyDescription() {
      return this.hasActiveFilters
        ? this.$t("pages.notesPage.emptyFilteredDescription")
        : this.$t("pages.notesPage.emptyDescription");
    },
  },
  methods: {
    ...mapActions(["fetchNotes"]),

    noteTitle(row) {
      return notePreviewTitle(row, this.$t("common.emDash"));
    },

    importanceLabel(value) {
      return noteImportanceLabel(value, this.$t.bind(this));
    },

    importanceClass(value) {
      return noteImportanceClass(value);
    },

    noteCaption(row) {
      return noteListCaption(row, {
        formatDateTime: this.formatStoreDateTime,
        t: this.$t.bind(this),
      });
    },

    applySort(value) {
      const [by, dir] = String(value || "createdAt:desc").split(":");
      this.sortBy = by || "createdAt";
      this.sortDesc = dir !== "asc";
      this.startIndex = 0;
      this.fetchPage();
    },

    clearFilters() {
      this.importanceFilter = IMPORTANCE_ALL;
      this.authorFilter = AUTHOR_ALL;
      this.sortBy = "createdAt";
      this.sortDesc = true;
      this.startIndex = 0;
      this.fetchPage();
    },

    applySearch() {
      this.searchQuery = (this.searchQuery || "").trim();
      this.startIndex = 0;
      this.fetchPage();
    },

    onFilterChange() {
      this.startIndex = 0;
      this.fetchPage();
    },

    prevPage() {
      if (!this.canPrev) return;
      this.startIndex = Math.max(0, this.startIndex - PAGE_SIZE);
      this.fetchPage();
    },

    nextPage() {
      if (!this.canNext) return;
      this.startIndex += PAGE_SIZE;
      this.fetchPage();
    },

    goToPage(page) {
      const next = Math.min(Math.max(1, page), this.lastPage);
      this.startIndex = (next - 1) * PAGE_SIZE;
      this.fetchPage();
    },

    openNote(row) {
      this.selectedNote = { ...row };
      this.sheetOpen = true;
    },

    onNoteUpdated(patch) {
      const idx = this.items.findIndex((n) => n.id === patch.id);
      if (idx >= 0) {
        this.items[idx] = {
          ...this.items[idx],
          text: patch.text,
          importance: patch.importance,
          updatedAt: new Date().toISOString(),
        };
      }
      if (this.selectedNote?.id === patch.id) {
        this.selectedNote = {
          ...this.selectedNote,
          text: patch.text,
          importance: patch.importance,
          updatedAt: new Date().toISOString(),
        };
      }
    },

    onNoteDeleted(id) {
      this.sheetOpen = false;
      this.selectedNote = null;
      this.items = this.items.filter((n) => n.id !== id);
      this.totalCount = Math.max(0, this.totalCount - 1);
      if (this.items.length === 0 && this.startIndex > 0) {
        this.startIndex = Math.max(0, this.startIndex - PAGE_SIZE);
      }
      this.fetchPage();
    },

    async fetchPage() {
      this.isLoading = true;
      try {
        await this.fetchNotes({
          params: {
            search: this.searchQuery || undefined,
            importance:
              this.importanceFilter !== IMPORTANCE_ALL
                ? this.importanceFilter
                : undefined,
            authorUserId:
              this.authorFilter !== AUTHOR_ALL &&
              this.authorFilter !== AUTHOR_UNKNOWN
                ? Number(this.authorFilter)
                : undefined,
            authorUnknown:
              this.authorFilter === AUTHOR_UNKNOWN ? true : undefined,
            sortBy: this.sortBy,
            sortDesc: this.sortDesc,
            startIndex: this.startIndex,
            pageSize: PAGE_SIZE,
          },
          onSuccess: (response) => {
            const data = response?.data?.value ?? response?.data ?? {};
            this.items = data.items || [];
            this.totalCount = data.totalCount ?? 0;
            this.authors = data.authors || [];
            this.hasUnknownAuthor = Boolean(data.hasUnknownAuthor);
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } finally {
        this.isLoading = false;
      }
    },
  },
  mounted() {
    this.fetchPage();
  },
};
</script>

<template>
  <AppPageChrome fill-viewport>
    <template #nav-center>
      <PageListToolbar
        v-model="searchQuery"
        :placeholder="$t('pages.notesPage.searchPlaceholder')"
        :active-filter-count="activeFilterCount"
        @submit="applySearch"
        @reset="clearFilters"
      >
        <template #filters>
          <FilterMenuGroup
            :model-value="importanceFilter"
            :label="$t('pages.notesPage.columnImportance')"
            :options="[
              {
                value: IMPORTANCE_ALL,
                label: $t('pages.notesPage.importanceAll'),
              },
              ...importanceOptions,
            ]"
            @update:model-value="
              (v) => {
                importanceFilter = v;
                onFilterChange();
              }
            "
          />
          <FilterMenuGroup
            :model-value="authorFilter"
            :label="$t('pages.notesPage.columnAuthor')"
            :options="authorOptions"
            @update:model-value="
              (v) => {
                authorFilter = v;
                onFilterChange();
              }
            "
          />
          <FilterMenuGroup
            :model-value="sortSelectValue"
            :label="$t('pages.notesPage.sortLabel')"
            :options="sortOptions"
            @update:model-value="applySort"
          />
        </template>
      </PageListToolbar>
    </template>

    <div class="flex min-h-0 flex-1 flex-col">
      <PageSection
        class="flex min-h-0 flex-1 flex-col !mb-0"
        :loading="isLoading"
        :empty="!isLoading && items.length === 0"
        :empty-icon="NotebookPen"
        :empty-title="emptyTitle"
        :empty-description="emptyDescription"
      >
        <div class="flex min-h-0 flex-1 flex-col gap-3">
          <InsetGroup scrollable>
            <InsetGroupItem
              v-for="row in items"
              :key="row.id"
              show-chevron
              :aria-label="
                $t('pages.notesPage.openNoteAria', {
                  product:
                    row.productName || $t('pages.notesPage.sheetTitleFallback'),
                })
              "
              @click="openNote(row)"
            >
              <template #leading>
                <NotebookPen class="size-4" />
              </template>
              <p class="truncate">{{ noteTitle(row) }}</p>
              <p
                class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
              >
                {{ noteCaption(row) }}
              </p>
              <template #trailing>
                <span
                  class="max-w-28 truncate text-xs"
                  :class="importanceClass(row.importance)"
                >
                  {{ importanceLabel(row.importance) }}
                </span>
              </template>
            </InsetGroupItem>
          </InsetGroup>

          <div
            v-if="totalCount > 0"
            class="flex shrink-0 flex-wrap items-center justify-between gap-2 px-1 py-1"
          >
            <p class="text-sm text-muted-foreground tabular-nums">
              {{
                $t("pages.notesPage.pageRange", {
                  from: pageFrom,
                  to: pageTo,
                  total: totalCount,
                })
              }}
            </p>
            <div class="flex items-center gap-1">
              <p class="mr-2 text-sm text-muted-foreground tabular-nums">
                {{ currentPage }} / {{ lastPage }}
              </p>
              <Button
                variant="outline"
                size="sm"
                class="h-8 w-8 p-0"
                :disabled="!canPrev || isLoading"
                :aria-label="$t('pages.notesPage.firstPage')"
                @click="goToPage(1)"
              >
                <ChevronsLeft class="size-4" />
              </Button>
              <Button
                variant="outline"
                size="sm"
                class="h-8 w-8 p-0"
                :disabled="!canPrev || isLoading"
                :aria-label="$t('pages.notesPage.prevPage')"
                @click="prevPage"
              >
                <ChevronLeft class="size-4" />
              </Button>
              <Button
                variant="outline"
                size="sm"
                class="h-8 w-8 p-0"
                :disabled="!canNext || isLoading"
                :aria-label="$t('pages.notesPage.nextPage')"
                @click="nextPage"
              >
                <ChevronRight class="size-4" />
              </Button>
              <Button
                variant="outline"
                size="sm"
                class="h-8 w-8 p-0"
                :disabled="!canNext || isLoading"
                :aria-label="$t('pages.notesPage.lastPage')"
                @click="goToPage(lastPage)"
              >
                <ChevronsRight class="size-4" />
              </Button>
            </div>
          </div>
        </div>
      </PageSection>
    </div>

    <NoteDetailSheet
      v-model:open="sheetOpen"
      :note="selectedNote"
      @updated="onNoteUpdated"
      @deleted="onNoteDeleted"
    />
  </AppPageChrome>
</template>
