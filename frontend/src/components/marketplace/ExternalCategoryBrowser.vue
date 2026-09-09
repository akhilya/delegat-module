<script>
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import NavSearchField from "@/components/NavSearchField.vue";
import { EmptyState } from "@/components/ui/empty-state";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import {
  ChevronRight,
  Home,
  Search,
} from "lucide-vue-next";
import { cn } from "@/lib/utils";

const SEARCH_IDLE_MS = 1000;

/**
 * Shared marketplace category tree browser (search + path + scrollable list).
 * Used by create-mapping dialog and category import page.
 * Search emits after 1s idle; Enter / search button run immediately.
 */
export default {
  name: "ExternalCategoryBrowser",
  inheritAttrs: false,
  components: {
    Spinner,
    Button,
    NavSearchField,
    EmptyState,
    InsetGroup,
    InsetGroupItem,
    Breadcrumb,
    BreadcrumbItem,
    BreadcrumbLink,
    BreadcrumbList,
    BreadcrumbPage,
    BreadcrumbSeparator,
    ChevronRight,
    Home,
    Search,
  },
  props: {
    categories: { type: Array, default: () => [] },
    path: { type: Array, default: () => [] },
    search: { type: String, default: "" },
    loading: { type: Boolean, default: false },
    selectedId: { type: [String, Number], default: null },
    searchPlaceholder: { type: String, default: "" },
    searchAriaLabel: { type: String, default: "" },
    emptyTitle: { type: String, default: "" },
    emptyDescription: { type: String, default: "" },
  },
  emits: [
    "update:search",
    "search",
    "select",
    "navigate",
    "path-index",
  ],
  data() {
    return {
      searchIdleTimer: null,
    };
  },
  computed: {
    showPath() {
      return this.path.length > 0 && !this.search;
    },
    rootClass() {
      return cn("flex min-h-0 flex-1 flex-col gap-2", this.$attrs.class);
    },
  },
  beforeUnmount() {
    this.clearSearchIdleTimer();
  },
  methods: {
    clearSearchIdleTimer() {
      if (this.searchIdleTimer != null) {
        clearTimeout(this.searchIdleTimer);
        this.searchIdleTimer = null;
      }
    },
    emitSearchNow() {
      this.clearSearchIdleTimer();
      this.$emit("search");
    },
    onSearchInput(value) {
      this.$emit("update:search", value);
      this.clearSearchIdleTimer();
      // Cleared query → reload tree immediately
      if (!value || !String(value).trim()) {
        this.$emit("search");
        return;
      }
      this.searchIdleTimer = setTimeout(() => {
        this.searchIdleTimer = null;
        this.$emit("search");
      }, SEARCH_IDLE_MS);
    },
    onRowClick(cat) {
      if (cat.hasChildren) this.$emit("navigate", cat);
      else this.$emit("select", cat);
    },
  },
};
</script>

<template>
  <div :class="rootClass">
    <div class="flex shrink-0 gap-2">
      <NavSearchField
        class="min-w-0 flex-1"
        :model-value="search"
        :placeholder="searchPlaceholder"
        @update:model-value="onSearchInput"
        @submit="emitSearchNow"
      />
      <Button
        variant="outline"
        size="icon"
        :aria-label="searchAriaLabel"
        @click="emitSearchNow"
      >
        <Search class="size-4" />
      </Button>
    </div>

    <Breadcrumb v-if="showPath" class="shrink-0 !mb-0">
      <BreadcrumbList>
        <BreadcrumbItem>
          <BreadcrumbLink as-child>
            <Button
              variant="ghost"
              size="icon"
              class="size-7"
              :aria-label="$t('components.breadcrumbs.homeAriaLabel')"
              @click="$emit('path-index', -1)"
            >
              <Home class="size-4" />
            </Button>
          </BreadcrumbLink>
        </BreadcrumbItem>
        <template v-for="(pathItem, index) in path" :key="pathItem.id">
          <BreadcrumbSeparator>
            <ChevronRight class="size-4" />
          </BreadcrumbSeparator>
          <BreadcrumbItem>
            <BreadcrumbLink v-if="index < path.length - 1" as-child>
              <Button
                variant="link"
                class="h-auto max-w-[14rem] truncate px-0 text-muted-foreground font-normal"
                :title="pathItem.name"
                @click="$emit('path-index', index)"
              >
                {{ pathItem.name }}
              </Button>
            </BreadcrumbLink>
            <BreadcrumbPage v-else class="max-w-[16rem] truncate" :title="pathItem.name">
              {{ pathItem.name }}
            </BreadcrumbPage>
          </BreadcrumbItem>
        </template>
      </BreadcrumbList>
    </Breadcrumb>

    <InsetGroup scrollable class="min-h-[12rem] flex-1">
      <div
        v-if="loading"
        class="flex flex-1 items-center justify-center py-10"
      >
        <Spinner size="2xl" class="text-muted-foreground" />
      </div>
      <div
        v-else-if="categories.length === 0"
        class="flex min-h-0 flex-1 items-center justify-center p-4"
      >
        <EmptyState
          class="w-full py-12"
          :framed="false"
          :title="emptyTitle"
          :description="emptyDescription"
        >
          <template #icon>
            <Search />
          </template>
          <slot name="empty-action" />
        </EmptyState>
      </div>
      <div v-else>
        <InsetGroupItem
          v-for="cat in categories"
          :key="cat.id"
          :selected="!cat.hasChildren && selectedId === cat.id"
          :show-chevron="!!cat.hasChildren"
          @click="onRowClick(cat)"
        >
          <p class="truncate">{{ cat.name }}</p>
          <p
            v-if="cat.fullPath"
            class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
          >
            {{ cat.fullPath }}
          </p>
        </InsetGroupItem>
      </div>
    </InsetGroup>
  </div>
</template>
