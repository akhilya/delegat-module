<script>
// ProductsTable - Reusable data table with row selection, column visibility, and drag-and-drop column reordering.
import { ref, watch, computed } from "vue";
import { FlexRender, getCoreRowModel, useVueTable } from "@tanstack/vue-table";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Loader2, GripVertical, ArrowUp, ArrowDown, ArrowUpDown } from "lucide-vue-next";

export default {
  components: {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
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
    sortBy: {
      type: String,
      default: null,
    },
    sortDesc: {
      type: Boolean,
      default: true,
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
  emits: ["update:rowSelection", "update:columnVisibility", "update:columnOrder", "sort"],
  setup(props, { emit }) {
    const localRowSelection = ref(props.rowSelection);
    const localColumnVisibility = ref(props.columnVisibility);
    const localColumnOrder = ref(props.columnOrder);

    // Drag state
    const draggedColumnId = ref(null);
    const dragOverColumnId = ref(null);

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

    // Check if a column can be dragged (not select or editAction)
    const canDragColumn = (columnId) => {
      return columnId !== "select" && columnId !== "editAction";
    };

    // Check if a column is sortable
    const isSortable = (columnId) => {
      return props.sortableColumns.includes(columnId);
    };

    const onHeaderClick = (columnId) => {
      if (isSortable(columnId)) {
        emit("sort", columnId);
      }
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
      onHeaderClick,
      draggedColumnId,
      dragOverColumnId,
      onDragStart,
      onDragOver,
      onDragLeave,
      onDrop,
      onDragEnd,
    };
  },
};
</script>

<template>
  <div class="flex flex-col flex-1 min-h-0">
    <div v-if="isLoading" class="flex-1 flex items-center justify-center">
      <Loader2 class="text-primary size-4 animate-spin" />
    </div>
    <div v-else class="flex-1 min-h-0 flex flex-col [&>[data-slot=table-container]]:flex-1 [&>[data-slot=table-container]]:min-h-0">
      <Table class="table-fixed min-w-full">
        <TableHeader class="sticky top-0 z-10 shadow-[inset_0_-1px_0_0_hsl(var(--border))]">
          <TableRow class="bg-background">
            <TableHead
              v-for="header in table.getHeaderGroups()[0]?.headers"
              :key="header.id"
              :style="{ width: header.getSize() + 'px' }"
              :draggable="canDragColumn(header.column.id)"
              :class="[
                'h-9 px-2 font-medium text-muted-foreground bg-background',
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
                class="flex items-center gap-1"
                :class="isSortable(header.column.id) ? 'cursor-pointer hover:text-foreground' : ''"
                @click="onHeaderClick(header.column.id)"
              >
                <GripVertical
                  v-if="canDragColumn(header.column.id)"
                  class="size-3 shrink-0 text-muted-foreground/50 hover:text-muted-foreground"
                />
                <FlexRender
                  v-if="!header.isPlaceholder && header.column.id !== 'editAction'"
                  :render="header.column.columnDef.header"
                  :props="header.getContext()"
                />
                <template v-if="isSortable(header.column.id)">
                  <ArrowDown v-if="sortBy === header.column.id && sortDesc" class="size-3.5 shrink-0 text-foreground" />
                  <ArrowUp v-else-if="sortBy === header.column.id && !sortDesc" class="size-3.5 shrink-0 text-foreground" />
                  <ArrowUpDown v-else class="size-3.5 shrink-0 opacity-30" />
                </template>
                <!-- Slot for header actions (Column selector button) -->
                <slot v-if="header.column.id === 'editAction'" name="headerActions" />
              </div>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <template v-if="table.getRowModel().rows?.length">
            <TableRow
              v-for="row in table.getRowModel().rows"
              :key="row.id"
              :data-state="row.getIsSelected() ? 'selected' : undefined"
              class="group hover:bg-muted/50 data-[state=selected]:bg-muted"
            >
              <TableCell
                v-for="cell in row.getVisibleCells()"
                :key="cell.id"
                :style="{ width: cell.column.getSize() + 'px' }"
                class="px-2 py-1 align-middle"
              >
                <FlexRender
                  :render="cell.column.columnDef.cell"
                  :props="cell.getContext()"
                />
              </TableCell>
            </TableRow>
          </template>
          <template v-else>
            <TableRow>
              <TableCell :colspan="columns.length" class="h-32 text-center">
                Нет результатов
              </TableCell>
            </TableRow>
          </template>
        </TableBody>
      </Table>
    </div>
  </div>
</template>
