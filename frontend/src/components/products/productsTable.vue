<script>
// ProductsTable - Reusable data table with row selection, column visibility, and drag-and-drop column reordering.
// Actions column is rendered outside <table> as a sticky-right div for reliable positioning with table-fixed.
import { ref, computed, watch, onMounted, onUpdated, onBeforeUnmount } from "vue";
import { FlexRender, getCoreRowModel, useVueTable } from "@tanstack/vue-table";
import { Loader2, GripVertical, ArrowUp, ArrowDown, ArrowUpDown } from "lucide-vue-next";
import { getSortIndex } from "@/lib/productSort";

export default {
  components: {
    FlexRender,
    Loader2,
    GripVertical,
    ArrowUp,
    ArrowDown,
    ArrowUpDown,
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
  },
  emits: ["update:rowSelection", "update:columnVisibility", "update:columnOrder", "sort", "row-click"],
  setup(props, { emit }) {
    const localRowSelection = ref(props.rowSelection);
    const localColumnVisibility = ref(props.columnVisibility);
    const localColumnOrder = ref(props.columnOrder);

    // Drag state
    const draggedColumnId = ref(null);
    const dragOverColumnId = ref(null);

    // Scroll shadow — show inset shadow on actions column when content is scrollable
    const scrollContainerEl = ref(null);
    const canScrollRight = ref(false);

    function checkScroll() {
      const el = scrollContainerEl.value;
      if (!el) { canScrollRight.value = false; return; }
      canScrollRight.value = el.scrollWidth - el.scrollLeft - el.clientWidth > 1;
    }

    let scrollResizeObserver = null;

    // Row height sync — query DOM directly for reliable height measurement
    const tableEl = ref(null);
    const rowHeights = ref([]);
    let resizeObserver = null;

    function syncHeights() {
      if (!tableEl.value) return;
      const rows = tableEl.value.querySelectorAll('tbody > tr');
      const newHeights = Array.from(rows).map(tr => tr.getBoundingClientRect().height);
      // Only update ref if heights actually changed — prevents onUpdated infinite loop
      const old = rowHeights.value;
      if (newHeights.length !== old.length || newHeights.some((h, i) => h !== old[i])) {
        rowHeights.value = newHeights;
      }
    }

    function setupObserver() {
      if (resizeObserver) resizeObserver.disconnect();
      if (!tableEl.value) return;
      const rows = tableEl.value.querySelectorAll('tbody > tr');
      resizeObserver = new ResizeObserver(syncHeights);
      rows.forEach(tr => resizeObserver.observe(tr));
    }

    // Scroll shadow — attach/detach listeners when the scroll container appears/disappears (v-if/v-else)
    watch(scrollContainerEl, (el, oldEl) => {
      if (oldEl) oldEl.removeEventListener('scroll', checkScroll);
      scrollResizeObserver?.disconnect();
      if (el) {
        el.addEventListener('scroll', checkScroll);
        scrollResizeObserver = new ResizeObserver(checkScroll);
        scrollResizeObserver.observe(el);
        checkScroll();
      } else {
        canScrollRight.value = false;
      }
    }, { flush: 'post' });

    onMounted(() => {
      syncHeights();
      setupObserver();
    });

    // After each DOM update — recalculate heights (observer re-setup only when data changes)
    onUpdated(() => {
      syncHeights();
    });

    // Re-setup observer when rows change (new/removed rows)
    watch(() => props.data, () => {
      setupObserver();
    }, { flush: 'post' });

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
      resizeObserver?.disconnect();
      scrollContainerEl.value?.removeEventListener('scroll', checkScroll);
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
  <div class="flex flex-col flex-1 min-h-0">
    <div v-if="isLoading" class="flex-1 flex items-center justify-center">
      <Loader2 class="text-primary size-4 animate-spin" />
    </div>
    <div v-else ref="scrollContainerEl" class="flex-1 min-h-0 overflow-auto">
      <div class="flex min-w-full w-max">
        <!-- Data table -->
        <table ref="tableEl" class="table-fixed grow shrink-0 text-sm border-separate border-spacing-0">
          <thead class="sticky top-0 z-10 bg-card">
            <tr class="bg-card transition-colors">
              <th
                v-for="header in table.getHeaderGroups()[0]?.headers"
                :key="header.id"
                :style="{ width: header.getSize() + 'px' }"
                :draggable="canDragColumn(header.column.id)"
                :class="[
                  'h-9 px-2 text-left align-middle font-medium text-muted-foreground whitespace-nowrap bg-card border-b group/th',
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
              </th>
            </tr>
          </thead>
          <tbody class="[&_tr:last-child>td]:border-0">
            <template v-if="table.getRowModel().rows?.length">
              <tr
                v-for="row in table.getRowModel().rows"
                :key="row.id"
                :data-state="row.getIsSelected() ? 'selected' : undefined"
                class="hover:bg-muted/50 data-[state=selected]:bg-muted transition-colors cursor-pointer group"
                @click="onRowClick(row, $event)"
              >
                <td
                  v-for="cell in row.getVisibleCells()"
                  :key="cell.id"
                  :style="{ width: cell.column.getSize() + 'px' }"
                  class="px-2 py-1 align-middle whitespace-nowrap border-b"
                >
                  <FlexRender
                    :render="cell.column.columnDef.cell"
                    :props="cell.getContext()"
                  />
                </td>
              </tr>
            </template>
            <template v-else>
              <tr class="transition-colors">
                <td :colspan="columns.length" class="h-32 text-center p-2 align-middle border-b">
                  {{ $t("products.productsTable.noResults") }}
                </td>
              </tr>
            </template>
          </tbody>
        </table>

        <!-- Actions column (OUTSIDE table, sticky right) -->
        <div :class="['sticky right-0 z-10 shrink-0 flex flex-col bg-card transition-shadow duration-200', canScrollRight ? 'shadow-[-6px_0_8px_-2px_rgba(0,0,0,0.1)]' : '']">
          <!-- Header: ColumnSelector slot -->
          <div class="sticky top-0 z-30 h-9 flex items-center px-1 sm:px-3 bg-card border-b">
            <slot name="headerActions" />
          </div>
          <!-- Row action buttons -->
          <template v-if="table.getRowModel().rows?.length">
            <div
              v-for="(row, i) in table.getRowModel().rows"
              :key="'action-' + row.id"
              :style="{ height: rowHeights[i] ? rowHeights[i] + 'px' : 'auto' }"
              class="border-b"
              :class="row.getIsSelected() ? 'bg-muted' : ''"
            >
              <slot name="rowActions" :row="row" />
            </div>
          </template>
        </div>
      </div>
    </div>
  </div>
</template>
