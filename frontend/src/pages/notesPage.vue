<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { NotebookPen, Search, ChevronLeft, ChevronRight, X } from "lucide-vue-next";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import NoteDetailSheet from "@/components/notes/NoteDetailSheet.vue";

const PAGE_SIZE = 25;
const AUTHOR_UNKNOWN = "__unknown__";
const AUTHOR_ALL = "__all__";
const IMPORTANCE_ALL = "__all__";

export default {
  components: {
    AppPageChrome,
    PageSection,
    Button,
    Input,
    Badge,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    NotebookPen,
    Search,
    ChevronLeft,
    ChevronRight,
    X,
    NoteDetailSheet,
  },
  setup() {
    const { formatDateTime: formatStoreDateTime, loadMemberships } = useStoreDateTime();
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
      breadcrumbs: [{ label: this.$t("pages.notesPage.breadcrumb") }],
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
        { value: "Critical", label: this.$t("components.note.importanceCritical") },
        { value: "Emergency", label: this.$t("components.note.importanceEmergency") },
      ];
    },
    sortOptions() {
      return [
        { value: "createdAt:desc", label: this.$t("pages.notesPage.sortCreatedDesc") },
        { value: "createdAt:asc", label: this.$t("pages.notesPage.sortCreatedAsc") },
        { value: "updatedAt:desc", label: this.$t("pages.notesPage.sortUpdatedDesc") },
        { value: "importance:desc", label: this.$t("pages.notesPage.sortImportanceDesc") },
        { value: "importance:asc", label: this.$t("pages.notesPage.sortImportanceAsc") },
        { value: "product:asc", label: this.$t("pages.notesPage.sortProductAsc") },
        { value: "product:desc", label: this.$t("pages.notesPage.sortProductDesc") },
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

    importanceLabel(value) {
      const found = this.importanceOptions.find((o) => o.value === value);
      return found?.label || value || "—";
    },

    importanceVariant(value) {
      if (value === "Critical" || value === "Emergency") return "destructive";
      if (value === "High") return "outline";
      return "secondary";
    },

    importanceBadgeClass(value) {
      if (value === "High") {
        return "border-warning/40 bg-warning-muted text-warning-foreground";
      }
      return "";
    },

    applySort(value) {
      const [by, dir] = String(value || "createdAt:desc").split(":");
      this.sortBy = by || "createdAt";
      this.sortDesc = dir !== "asc";
      this.startIndex = 0;
      this.fetchPage();
    },

    clearFilters() {
      this.searchQuery = "";
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

    productEditTo(row) {
      return {
        path: `/products/${row.productId}/edit`,
        query: { tab: "notes" },
      };
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
            authorUnknown: this.authorFilter === AUTHOR_UNKNOWN ? true : undefined,
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
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <template #nav-center>
      <div class="relative h-9 w-full">
        <Input
          v-model="searchQuery"
          class="absolute h-full w-full pr-10"
          :placeholder="$t('pages.notesPage.searchPlaceholder')"
          @keypress.enter="applySearch"
        />
        <Button
          class="absolute top-1/2 right-0.5 size-8 -translate-y-1/2"
          variant="link"
          @click="applySearch"
        >
          <Search class="size-4" />
        </Button>
      </div>
    </template>

    <div class="mb-4 flex flex-wrap items-center gap-2">
      <Select
        :model-value="importanceFilter"
        @update:model-value="
          (v) => {
            importanceFilter = v;
            onFilterChange();
          }
        "
      >
        <SelectTrigger class="h-9 w-full sm:w-44">
          <SelectValue :placeholder="$t('pages.notesPage.importanceAll')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="IMPORTANCE_ALL">
            {{ $t("pages.notesPage.importanceAll") }}
          </SelectItem>
          <SelectItem
            v-for="opt in importanceOptions"
            :key="opt.value"
            :value="opt.value"
          >
            {{ opt.label }}
          </SelectItem>
        </SelectContent>
      </Select>

      <Select
        :model-value="authorFilter"
        @update:model-value="
          (v) => {
            authorFilter = v;
            onFilterChange();
          }
        "
      >
        <SelectTrigger class="h-9 w-full sm:w-44">
          <SelectValue :placeholder="$t('pages.notesPage.authorAll')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="AUTHOR_ALL">
            {{ $t("pages.notesPage.authorAll") }}
          </SelectItem>
          <SelectItem v-if="hasUnknownAuthor" :value="AUTHOR_UNKNOWN">
            {{ $t("pages.notesPage.authorUnknown") }}
          </SelectItem>
          <SelectItem
            v-for="author in authors"
            :key="author.id"
            :value="String(author.id)"
          >
            {{ author.userName || `#${author.id}` }}
          </SelectItem>
        </SelectContent>
      </Select>

      <Select :model-value="sortSelectValue" @update:model-value="applySort">
        <SelectTrigger class="h-9 w-full sm:w-52">
          <SelectValue :placeholder="$t('pages.notesPage.sortLabel')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem
            v-for="opt in sortOptions"
            :key="opt.value"
            :value="opt.value"
          >
            {{ opt.label }}
          </SelectItem>
        </SelectContent>
      </Select>

      <Button
        v-if="hasActiveFilters"
        variant="ghost"
        size="sm"
        class="h-9"
        @click="clearFilters"
      >
        <X class="size-4" />
        {{ $t("pages.notesPage.clearFilters") }}
      </Button>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="!isLoading && items.length === 0"
      :empty-icon="NotebookPen"
      :empty-title="emptyTitle"
      :empty-description="emptyDescription"
      class="min-h-[calc(100vh-14rem)]"
    >
      <div class="overflow-hidden rounded-xl border bg-card">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead class="w-36">{{ $t("pages.notesPage.columnImportance") }}</TableHead>
                <TableHead>{{ $t("pages.notesPage.columnNote") }}</TableHead>
                <TableHead class="w-56">{{ $t("pages.notesPage.columnProduct") }}</TableHead>
                <TableHead class="w-40">{{ $t("pages.notesPage.columnAuthor") }}</TableHead>
                <TableHead class="w-44">{{ $t("pages.notesPage.columnCreated") }}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="row in items"
                :key="row.id"
                class="cursor-pointer"
                @click="openNote(row)"
              >
                <TableCell>
                  <Badge
                    :variant="importanceVariant(row.importance)"
                    :class="importanceBadgeClass(row.importance)"
                  >
                    {{ importanceLabel(row.importance) }}
                  </Badge>
                </TableCell>
                <TableCell>
                  <p class="line-clamp-2 whitespace-pre-wrap text-sm">
                    {{ row.text || $t("common.emDash") }}
                  </p>
                </TableCell>
                <TableCell>
                  <RouterLink
                    :to="productEditTo(row)"
                    class="block font-medium text-foreground hover:underline"
                    @click.stop
                  >
                    <span class="line-clamp-1">{{ row.productName }}</span>
                  </RouterLink>
                  <p
                    v-if="row.productSku"
                    class="mt-0.5 font-mono text-xs text-muted-foreground tabular-nums"
                  >
                    {{ row.productSku }}
                  </p>
                </TableCell>
                <TableCell class="text-sm text-muted-foreground">
                  {{ row.authorUserName || $t("pages.notesPage.authorUnknown") }}
                </TableCell>
                <TableCell class="text-sm text-muted-foreground tabular-nums">
                  {{ formatStoreDateTime(row.createdAt) || $t("common.emDash") }}
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div
          v-if="totalCount > 0"
          class="flex items-center justify-between border-t px-3 py-2"
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
            <Button
              variant="outline"
              size="icon"
              class="size-8"
              :disabled="!canPrev"
              @click="prevPage"
            >
              <ChevronLeft class="size-4" />
            </Button>
            <Button
              variant="outline"
              size="icon"
              class="size-8"
              :disabled="!canNext"
              @click="nextPage"
            >
              <ChevronRight class="size-4" />
            </Button>
          </div>
        </div>
      </div>
    </PageSection>

    <NoteDetailSheet
      v-model:open="sheetOpen"
      :note="selectedNote"
      @updated="onNoteUpdated"
      @deleted="onNoteDeleted"
    />
  </AppPageChrome>
</template>
