<script>
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { EmptyState } from "@/components/ui/empty-state";
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

/**
 * Shared marketplace category tree browser (search + path + scrollable list).
 * Used by create-mapping dialog and category import page.
 */
export default {
  name: "ExternalCategoryBrowser",
  inheritAttrs: false,
  components: {
    Spinner,
    Button,
    Input,
    EmptyState,
    Breadcrumb,
    BreadcrumbItem,
    BreadcrumbLink,
    BreadcrumbList,
    BreadcrumbPage,
    BreadcrumbSeparator,
    ChevronRight,
    Home,
    Search
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
  computed: {
    showPath() {
      return this.path.length > 0 && !this.search;
    },
    rootClass() {
      return cn("flex min-h-0 flex-1 flex-col gap-2", this.$attrs.class);
    },
  },
  methods: {
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
      <div class="relative min-w-0 flex-1">
        <Search
          class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
        />
        <Input
          :model-value="search"
          :placeholder="searchPlaceholder"
          class="pl-9"
          @update:model-value="$emit('update:search', $event)"
          @keyup.enter="$emit('search')"
        />
      </div>
      <Button
        variant="outline"
        size="icon"
        :aria-label="searchAriaLabel"
        @click="$emit('search')"
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

    <div
      class="flex min-h-[12rem] flex-1 flex-col overflow-hidden rounded-lg border"
    >
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
          :title="emptyTitle"
          :description="emptyDescription"
        >
          <template #icon>
            <Search />
          </template>
          <slot name="empty-action" />
        </EmptyState>
      </div>
      <div v-else class="min-h-0 flex-1 overflow-y-auto p-1.5">
        <button
          v-for="cat in categories"
          :key="cat.id"
          type="button"
          class="flex w-full items-center justify-between rounded-md px-2.5 py-2 text-left transition-colors hover:bg-accent"
          :class="{
            'bg-accent': !cat.hasChildren && selectedId === cat.id,
          }"
          @click="onRowClick(cat)"
        >
          <div class="min-w-0 flex-1">
            <div class="truncate text-sm font-medium">{{ cat.name }}</div>
            <div
              v-if="cat.fullPath"
              class="truncate text-xs text-muted-foreground"
            >
              {{ cat.fullPath }}
            </div>
          </div>
          <ChevronRight
            v-if="cat.hasChildren"
            class="ml-2 size-4 shrink-0 text-muted-foreground"
          />
        </button>
      </div>
    </div>
  </div>
</template>
