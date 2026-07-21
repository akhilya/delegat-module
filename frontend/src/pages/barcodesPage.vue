<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { PageHeader } from "@/components/ui/page-header";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import BarcodeCell from "@/components/products/BarcodeCell.vue";
import { http } from "@/main";
import {
  displaySonnerError,
  displaySonnerSuccess,
} from "@/store/sonnerHelper";
import {
  Barcode,
  ChevronLeft,
  ChevronRight,
  Download,
  Loader2,
  Search,
} from "lucide-vue-next";

const PAGE_SIZE = 25;
const EXPORT_PAGE_SIZE = 100;

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Checkbox,
    PageHeader,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    BarcodeCell,
    Barcode,
    ChevronLeft,
    ChevronRight,
    Download,
    Loader2,
    Search,
  },
  data() {
    return {
      items: [],
      totalCount: 0,
      startIndex: 0,
      searchQuery: "",
      searchDraft: "",
      isLoading: false,
      isDownloadingSelected: false,
      isDownloadingAll: false,
      /** @type {Record<string, object>} */
      selection: {},
      breadcrumbs: [{ label: this.$t("pages.barcodesPage.breadcrumb") }],
    };
  },
  computed: {
    selectedItems() {
      return Object.values(this.selection);
    },
    selectedCount() {
      return this.selectedItems.length;
    },
    allPageSelected() {
      if (this.items.length === 0) return false;
      return this.items.every((row) => this.selection[String(row.id)]);
    },
    somePageSelected() {
      if (this.items.length === 0) return false;
      const n = this.items.filter((row) => this.selection[String(row.id)]).length;
      return n > 0 && n < this.items.length;
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
    isBusy() {
      return this.isDownloadingSelected || this.isDownloadingAll;
    },
  },
  methods: {
    unwrap(response) {
      return response?.data?.value ?? response?.data ?? {};
    },

    async fetchPage() {
      this.isLoading = true;
      try {
        const response = await http.get("/products/barcodes", {
          params: {
            searchQuery: this.searchQuery || undefined,
            startIndex: this.startIndex,
            pageSize: PAGE_SIZE,
          },
          cache: false,
        });
        const data = this.unwrap(response);
        this.items = data.items || [];
        this.totalCount = data.totalCount ?? 0;
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.isLoading = false;
      }
    },

    applySearch() {
      this.searchQuery = this.searchDraft.trim();
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

    isSelected(row) {
      return Boolean(this.selection[String(row.id)]);
    },

    toggleRow(row, checked) {
      const key = String(row.id);
      if (checked) {
        this.selection = { ...this.selection, [key]: row };
      } else {
        const next = { ...this.selection };
        delete next[key];
        this.selection = next;
      }
    },

    toggleAllPage(checked) {
      const next = { ...this.selection };
      for (const row of this.items) {
        const key = String(row.id);
        if (checked) next[key] = row;
        else delete next[key];
      }
      this.selection = next;
    },

    clearSelection() {
      this.selection = {};
    },

    async downloadOne(row) {
      if (!row?.barcode) return;
      const { barcodeFormatFor, downloadBarcodePng } = await import(
        "@/lib/barcodeDownload.js"
      );
      await downloadBarcodePng(row.barcode, barcodeFormatFor(row.barcode));
    },

    async downloadSelected() {
      if (this.selectedCount === 0) {
        displaySonnerError(this.$t("pages.barcodesPage.noneSelected"));
        return;
      }
      this.isDownloadingSelected = true;
      try {
        const { downloadBarcodesZip } = await import("@/lib/barcodeDownload.js");
        const added = await downloadBarcodesZip(this.selectedItems, "barcodes-selected.zip");
        if (added === 0) {
          displaySonnerError(this.$t("pages.barcodesPage.downloadEmpty"));
        } else {
          displaySonnerSuccess(this.$t("pages.barcodesPage.downloadedCount", { count: added }));
        }
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.isDownloadingSelected = false;
      }
    },

    async fetchAllBarcodeItems() {
      const all = [];
      let offset = 0;
      let lastPage = false;
      while (!lastPage) {
        const response = await http.get("/products/barcodes", {
          params: {
            searchQuery: this.searchQuery || undefined,
            startIndex: offset,
            pageSize: EXPORT_PAGE_SIZE,
          },
          cache: false,
        });
        const data = this.unwrap(response);
        const chunk = data.items || [];
        all.push(...chunk);
        lastPage = data.lastPage === true || chunk.length === 0;
        offset += EXPORT_PAGE_SIZE;
        if (chunk.length === 0) break;
      }
      return all;
    },

    async downloadAll() {
      this.isDownloadingAll = true;
      try {
        const all = await this.fetchAllBarcodeItems();
        if (all.length === 0) {
          displaySonnerError(this.$t("pages.barcodesPage.downloadEmpty"));
          return;
        }
        const { downloadBarcodesZip } = await import("@/lib/barcodeDownload.js");
        const added = await downloadBarcodesZip(all, "barcodes-all.zip");
        if (added === 0) {
          displaySonnerError(this.$t("pages.barcodesPage.downloadEmpty"));
        } else {
          displaySonnerSuccess(this.$t("pages.barcodesPage.downloadedCount", { count: added }));
        }
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.isDownloadingAll = false;
      }
    },
  },
  mounted() {
    this.fetchPage();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      :title="$t('pages.barcodesPage.title')"
      :description="$t('pages.barcodesPage.description', { count: totalCount })"
    >
      <div class="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          :disabled="isBusy || selectedCount === 0"
          @click="downloadSelected"
        >
          <Loader2 v-if="isDownloadingSelected" class="mr-2 size-4 animate-spin" />
          <Download v-else class="mr-2 size-4" />
          {{ $t("pages.barcodesPage.downloadSelected", { count: selectedCount }) }}
        </Button>
        <Button :disabled="isBusy || totalCount === 0" @click="downloadAll">
          <Loader2 v-if="isDownloadingAll" class="mr-2 size-4 animate-spin" />
          <Download v-else class="mr-2 size-4" />
          {{ $t("pages.barcodesPage.downloadAll") }}
        </Button>
      </div>
    </PageHeader>

    <div class="mb-4 flex flex-wrap items-center gap-3">
      <div class="relative min-w-[220px] flex-1 sm:max-w-sm">
        <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          v-model="searchDraft"
          class="pl-9"
          :placeholder="$t('pages.barcodesPage.searchPlaceholder')"
          @keydown.enter="applySearch"
        />
      </div>
      <Button variant="secondary" @click="applySearch">
        {{ $t("common.search") }}
      </Button>
      <Button
        v-if="selectedCount > 0"
        variant="ghost"
        size="sm"
        @click="clearSelection"
      >
        {{ $t("pages.barcodesPage.clearSelection") }}
      </Button>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="!isLoading && items.length === 0"
      :empty-icon="Barcode"
      :empty-title="$t('pages.barcodesPage.emptyTitle')"
      :empty-description="$t('pages.barcodesPage.emptyDescription')"
    >
      <div class="overflow-hidden rounded-lg border">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead class="w-12">
                  <Checkbox
                    :model-value="allPageSelected"
                    :indeterminate="somePageSelected"
                    @update:model-value="(v) => toggleAllPage(v === true)"
                  />
                </TableHead>
                <TableHead>{{ $t("common.name") }}</TableHead>
                <TableHead class="w-40">SKU</TableHead>
                <TableHead class="w-56">{{ $t("pages.barcodesPage.columnBarcode") }}</TableHead>
                <TableHead class="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in items" :key="row.id">
                <TableCell>
                  <Checkbox
                    :model-value="isSelected(row)"
                    @update:model-value="(v) => toggleRow(row, v === true)"
                  />
                </TableCell>
                <TableCell class="font-medium">
                  <span class="line-clamp-2">{{ row.name }}</span>
                </TableCell>
                <TableCell class="font-mono text-sm text-muted-foreground">
                  {{ row.sku || "-" }}
                </TableCell>
                <TableCell>
                  <BarcodeCell :barcode="row.barcode" />
                </TableCell>
                <TableCell>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8"
                    :title="$t('pages.barcodesPage.downloadOne')"
                    @click="downloadOne(row)"
                  >
                    <Download class="size-4" />
                  </Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div
          v-if="totalCount > 0"
          class="flex items-center justify-between border-t px-3 py-2"
        >
          <p class="text-sm text-muted-foreground">
            {{ $t("pages.barcodesPage.pageRange", { from: pageFrom, to: pageTo, total: totalCount }) }}
          </p>
          <div class="flex items-center gap-1">
            <Button
              variant="outline"
              size="icon"
              class="size-8"
              :disabled="!canPrev || isLoading"
              @click="prevPage"
            >
              <ChevronLeft class="size-4" />
            </Button>
            <Button
              variant="outline"
              size="icon"
              class="size-8"
              :disabled="!canNext || isLoading"
              @click="nextPage"
            >
              <ChevronRight class="size-4" />
            </Button>
          </div>
        </div>
      </div>
    </PageSection>
  </Page>
</template>
