<script>
import { EmptyState } from "@/components/ui/empty-state";
// ProductsTable - Reusable data table with row selection, column visibility, and drag-and-drop column reordering.
// Actions column is rendered outside <table> as a sticky-right div for reliable positioning with table-fixed.
import { ref, computed, watch, onMounted, onUpdated, onBeforeUnmount } from "vue";
import { FlexRender, getCoreRowModel, useVueTable } from "@tanstack/vue-table";
import {
  GripVertical,
  ArrowUp,
  ArrowDown,
  ArrowUpDown,
} from "lucide-vue-next";
import {
  ContextMenu,
  ContextMenuTrigger,
} from "@/components/ui/context-menu";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { getSortIndex } from "@/lib/productSort";

export default {
  components: {
    EmptyState,
    FlexRender,
    GripVertical,
    ArrowUp,
    ArrowDown,
    ArrowUpDown,
    ContextMenu,
    ContextMenuTrigger,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
  },
  props: {
    columns: {
      type: Array,
      required: true,
    },
    data: {
      type: Array,
      required: true,
    },
    isLoading: Boolean,
    columnVisibility: {
      type: Object,
      default: () => ({}),
    },
    rowSelection: {
      type: Object,
      default: () => ({}),
    },
    columnOrder: {
      type: Array,
      default: () => [],
    },
    sortColumns: {
      type: Array,
      default: () => [],
    },
    sortableColumns: {
      type: Array,
      default: () => [],
    },
    startIndex: {
      type: Number,
      default: 0,
    },
    connections: {
      type: Array,
      default: () => [],
    },
    /** When true, Adapters column is visible but non-interactive (e.g. trash). */
    adaptersDisabled: {
      type: Boolean,
      default: false,
    },
  },
  emits: ["update:rowSelection", "update:columnVisibility", "update:columnOrder", "sort", "row-click", "adapters-changed"],
  setup(props, { emit }) {
    const localRowSelection = ref(props.rowSelection);
    const localColumnVisibility = ref(props.columnVisibility);
    const localColumnOrder = ref(props.columnOrder);

    // Drag state
    const draggedColumnId = ref(null);
    const dragOverColumnId = ref(null);

    // Scroll shadow - show inset shadow on actions column when content is scrollable
    const scrollContainerEl = ref(null);
    const canScrollRight = ref(false);

    function checkScroll() {
      const el = scrollContainerEl.value;
      if (!el) {
        canScrollRight.value = false;
        return;
      }
      canScrollRight.value = el.scrollWidth - el.scrollLeft - el.clientWidth > 1;
    }

    let scrollResizeObserver = null;

    // Row height sync - query DOM directly for reliable height measurement
    const tableEl = ref(null);
    const rowHeights = ref([]);
    let resizeObserver = null;
    let syncRaf = null;

    function tableDom() {
      const el = tableEl.value;
      if (!el) return null;
      return el.nodeType === 1 ? el : el.$el;
    }

    function syncHeights() {
      const table = tableDom();
      if (!table) return;
      const rows = table.querySelectorAll("tbody tr");
      const newHeights = Array.from(rows).map((tr) =>
        Math.round(tr.getBoundingClientRect().height),
      );
      // Only update ref if heights actually changed - prevents onUpdated infinite loop
      const old = rowHeights.value;
      if (newHeights.length !== old.length || newHeights.some((h, i) => h !== old[i])) {
        rowHeights.value = newHeights;
      }
    }

    function scheduleSyncHeights() {
      if (syncRaf != null) return;
      syncRaf = requestAnimationFrame(() => {
        syncRaf = null;
        syncHeights();
        checkScroll();
      });
    }

    function setupObserver() {
      if (resizeObserver) resizeObserver.disconnect();
      const table = tableDom();
      if (!table) return;
      // Observe the table once - per-row observers reflow the main thread on every panel resize.
      resizeObserver = new ResizeObserver(scheduleSyncHeights);
      resizeObserver.observe(table);
    }

    // Scroll shadow - attach/detach listeners when the scroll container appears/disappears (v-if/v-else)
    watch(scrollContainerEl, (el, oldEl) => {
      if (oldEl) oldEl.removeEventListener("scroll", checkScroll);
      scrollResizeObserver?.disconnect();
      if (el) {
        el.addEventListener("scroll", checkScroll);
        scrollResizeObserver = new ResizeObserver(checkScroll);
        scrollResizeObserver.observe(el);
        checkScroll();
      } else {
        canScrollRight.value = false;
      }
    }, { flush: "post" });

    const contextMenuRow = ref(null);
    const onRowContextMenu = (row) => {
      contextMenuRow.value = row;
    };

    onMounted(() => {
      syncHeights();
      setupObserver();
    });

    // After each DOM update - recalculate heights (observer re-setup only when data changes)
    onUpdated(() => {
      scheduleSyncHeights();
    });

    // Re-setup observer when rows change (new/removed rows)
    watch(() => props.data, () => {
      contextMenuRow.value = null;
      setupObserver();
      scheduleSyncHeights();
    }, { flush: "post" });

    // Sync external props to local state
    watch(
      () => props.rowSelection,
      (val) => {
        localRowSelection.value = val;
      },
      { deep: true },
    );

    watch(
      () => props.columnVisibility,
      (val) => {
        localColumnVisibility.value = val;
      },
      { deep: true },
    );

    watch(
      () => props.columnOrder,
      (val) => {
        localColumnOrder.value = val;
      },
      { deep: true },
    );

    const table = useVueTable({
      get data() {
        return props.data;
      },
      get columns() {
        return props.columns;
      },
      getCoreRowModel: getCoreRowModel(),
      enableRowSelection: true,
      meta: {
        get startIndex() {
          return props.startIndex;
        },
        get connections() {
          return props.connections;
        },
        get adaptersDisabled() {
          return props.adaptersDisabled;
        },
        onAdaptersChanged() {
          emit("adapters-changed");
        },
      },
      state: {
        get rowSelection() {
          return localRowSelection.value;
        },
        get columnVisibility() {
          return localColumnVisibility.value;
        },
        get columnOrder() {
          return localColumnOrder.value;
        },
      },
      onRowSelectionChange: (updaterOrValue) => {
        const newValue =
          typeof updaterOrValue === "function"
            ? updaterOrValue(localRowSelection.value)
            : updaterOrValue;
        localRowSelection.value = newValue;
        emit("update:rowSelection", newValue);
      },
      onColumnVisibilityChange: (updaterOrValue) => {
        const newValue =
          typeof updaterOrValue === "function"
            ? updaterOrValue(localColumnVisibility.value)
            : updaterOrValue;
        localColumnVisibility.value = newValue;
        emit("update:columnVisibility", newValue);
      },
      onColumnOrderChange: (updaterOrValue) => {
        const newValue =
          typeof updaterOrValue === "function"
            ? updaterOrValue(localColumnOrder.value)
            : updaterOrValue;
        localColumnOrder.value = newValue;
        emit("update:columnOrder", newValue);
      },
      getRowId: (row) => String(row.id),
    });

    // Compute selected product IDs
    const selectedIds = computed(() => {
      return Object.keys(localRowSelection.value)
        .filter((key) => localRowSelection.value[key])
        .map((id) => parseInt(id, 10));
    });

    onBeforeUnmount(() => {
      if (syncRaf != null) cancelAnimationFrame(syncRaf);
      resizeObserver?.disconnect();
      scrollContainerEl.value?.removeEventListener("scroll", checkScroll);
      scrollResizeObserver?.disconnect();
    });

    // Check if a column can be dragged (not select or editAction)
    const canDragColumn = (columnId) => {
      return columnId !== "select";
    };

    // Check if a column is sortable
    const isSortable = (columnId) => {
      return props.sortableColumns.includes(columnId);
    };

    const onHeaderClick = (columnId, event) => {
      if (isSortable(columnId)) {
        emit("sort", columnId, event);
      }
    };

    const getColumnSort = (columnId) => {
      const index = getSortIndex(props.sortColumns, columnId);
      if (index < 0) return null;
      return { ...props.sortColumns[index], index };
    };

    const onRowClick = (row, event) => {
      const target = event.target;
      const interactive = target.closest('button, a, input, [role="checkbox"], [role="menuitem"], [data-slot="dropdown-menu"]');
      if (interactive) return;
      emit("row-click", row.original);
    };

    // Native drag & drop handlers
    const onDragStart = (e, columnId) => {
      if (!canDragColumn(columnId)) {
        e.preventDefault();
        return;
      }
      draggedColumnId.value = columnId;
      e.dataTransfer.effectAllowed = "move";
      e.dataTransfer.setData("text/plain", columnId);
    };

    const onDragOver = (e, columnId) => {
      if (!canDragColumn(columnId) || !draggedColumnId.value) return;
      e.preventDefault();
      dragOverColumnId.value = columnId;
    };

    const onDragLeave = () => {
      dragOverColumnId.value = null;
    };

    const onDrop = (e, targetColumnId) => {
      e.preventDefault();
      if (!draggedColumnId.value || !canDragColumn(targetColumnId)) return;

      const headers = table.getHeaderGroups()[0]?.headers || [];
      const currentOrder = headers.map((h) => h.column.id);

      const fromIndex = currentOrder.indexOf(draggedColumnId.value);
      const toIndex = currentOrder.indexOf(targetColumnId);

      if (fromIndex !== -1 && toIndex !== -1 && fromIndex !== toIndex) {
        const newOrder = [...currentOrder];
        const [removed] = newOrder.splice(fromIndex, 1);
        newOrder.splice(toIndex, 0, removed);

        localColumnOrder.value = newOrder;
        emit("update:columnOrder", newOrder);
      }

      draggedColumnId.value = null;
      dragOverColumnId.value = null;
    };

    const onDragEnd = () => {
      draggedColumnId.value = null;
      dragOverColumnId.value = null;
    };

    return {
      table,
      selectedIds,
      canDragColumn,
      isSortable,
      getColumnSort,
      onHeaderClick,
      onRowClick,
      contextMenuRow,
      onRowContextMenu,
      draggedColumnId,
      dragOverColumnId,
      onDragStart,
      onDragOver,
      onDragLeave,
      onDrop,
      onDragEnd,
      tableEl,
      rowHeights,
      scrollContainerEl,
      canScrollRight,
    };
  },
};
</script>

<template>
  <div class="flex w-full flex-col">
    <EmptyState
      v-if="isLoading"
      loading
      :framed="false"
      class="min-h-[12rem]"
    />
    <div v-else ref="scrollContainerEl" class="overflow-x-auto">
      <div class="flex h-max min-w-full w-max items-start">
        <!-- Data table -->
        <Table
          ref="tableEl"
          :contain="false"
          class="table-fixed h-max grow shrink-0 self-start border-separate border-spacing-0"
        >
          <TableHeader class="sticky top-0 z-10 bg-card">
            <TableRow class="bg-card hover:bg-transparent">
              <TableHead
                v-for="header in table.getHeaderGroups()[0]?.headers"
                :key="header.id"
                :style="{ width: header.getSize() + 'px' }"
                :draggable="canDragColumn(header.column.id)"
                :class="[
                  'h-9 px-2 bg-card border-b group/th',
                  canDragColumn(header.column.id) ? 'cursor-grab active:cursor-grabbing' : '',
                  dragOverColumnId === header.column.id ? 'bg-muted/50' : '',
                  isSortable(header.column.id) ? 'select-none' : '',
                ]"
                @dragstart="onDragStart($event, header.column.id)"
                @dragover="onDragOver($event, header.column.id)"
                @dragleave="onDragLeave"
                @drop="onDrop($event, header.column.id)"
                @dragend="onDragEnd"
              >
                <div
                  class="flex items-center gap-1 min-w-0 overflow-hidden"
                  :class="isSortable(header.column.id) ? 'cursor-pointer hover:text-foreground' : ''"
                  :title="isSortable(header.column.id) ? $t('products.productsTable.sortHeaderHint') : undefined"
                  @click="onHeaderClick(header.column.id, $event)"
                >
                  <GripVertical
                    v-if="canDragColumn(header.column.id)"
                    class="size-3 shrink-0 text-muted-foreground/50 hover:text-muted-foreground invisible group-hover/th:visible"
                  />
                  <span class="truncate">
                    <FlexRender
                      v-if="!header.isPlaceholder"
                      :render="header.column.columnDef.header"
                      :props="header.getContext()"
                    />
                  </span>
                  <template v-if="isSortable(header.column.id)">
                    <span
                      v-if="getColumnSort(header.column.id)"
                      class="inline-flex items-center gap-0.5 shrink-0"
                    >
                      <span
                        v-if="sortColumns.length > 1"
                        class="text-[10px] leading-none font-semibold tabular-nums text-muted-foreground"
                      >
                        {{ getColumnSort(header.column.id).index + 1 }}
                      </span>
                      <ArrowDown
                        v-if="getColumnSort(header.column.id).desc"
                        class="size-3.5 text-foreground"
                      />
                      <ArrowUp v-else class="size-3.5 text-foreground" />
                    </span>
                    <ArrowUpDown v-else class="size-3.5 shrink-0 opacity-30" />
                  </template>
                </div>
              </TableHead>
            </TableRow>
          </TableHeader>
            <ContextMenu>
              <ContextMenuTrigger
                as-child
                :disabled="!table.getRowModel().rows?.length"
              >
                <TableBody class="[&_tr:last-child]:border-b-0 [&_tr:last-child>td]:border-b-0">
                  <template v-if="table.getRowModel().rows?.length">
                    <TableRow
                      v-for="row in table.getRowModel().rows"
                      :key="row.id"
                      :data-state="row.getIsSelected() ? 'selected' : undefined"
                      class="h-[4.25rem] cursor-pointer"
                      @click="onRowClick(row, $event)"
                      @contextmenu="onRowContextMenu(row)"
                    >
                      <TableCell
                        v-for="cell in row.getVisibleCells()"
                        :key="cell.id"
                        :style="{ width: cell.column.getSize() + 'px' }"
                        class="h-[4.25rem] overflow-hidden px-3 py-0 align-middle border-b"
                      >
                        <FlexRender
                          :render="cell.column.columnDef.cell"
                          :props="cell.getContext()"
                        />
                      </TableCell>
                    </TableRow>
                  </template>
                  <TableRow v-else class="hover:bg-transparent">
                    <TableCell :colspan="columns.length" class="h-32 text-center p-2 align-middle border-b whitespace-normal">
                      {{ $t("products.productsTable.noResults") }}
                    </TableCell>
                  </TableRow>
                </TableBody>
              </ContextMenuTrigger>
              <slot
                v-if="contextMenuRow"
                name="rowContextMenu"
                :row="contextMenuRow"
              />
            </ContextMenu>
        </Table>

        <!-- Actions column (OUTSIDE table, sticky right) -->
        <div :class="['sticky right-0 z-10 shrink-0 self-start flex flex-col bg-card transition-shadow duration-200', canScrollRight ? 'shadow-[-6px_0_8px_-2px_rgba(0,0,0,0.1)]' : '']">
          <!-- Header: ColumnSelector slot -->
          <div class="sticky top-0 z-30 h-9 box-border flex items-center px-1 sm:px-3 bg-card border-b">
            <slot name="headerActions" />
          </div>
          <!-- Row action buttons -->
          <template v-if="table.getRowModel().rows?.length">
            <div
              v-for="(row, i) in table.getRowModel().rows"
              :key="'action-' + row.id"
              :style="{ height: rowHeights[i] != null ? rowHeights[i] + 'px' : 'auto' }"
              class="box-border shrink-0"
              :class="[
                i === table.getRowModel().rows.length - 1 ? '' : 'border-b',
                row.getIsSelected() ? 'bg-muted' : '',
              ]"
            >
              <slot name="rowActions" :row="row" />
            </div>
          </template>
        </div>
      </div>
    </div>
  </div>
</template>
