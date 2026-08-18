<script setup>
/**
 * FieldChangesTable — before/after comparison for import previews.
 * Catalog Workbench: neutral structure, semantic tint only on changed spans.
 *
 * items: [{ label?, fields: [{ key, label, oldValue, newValue }] }]
 */
import { computed } from "vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { AlertTriangle } from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import FieldDiffValue from "@/components/products/FieldDiffValue.vue";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

const props = defineProps({
  items: { type: Array, required: true },
  selectable: { type: Boolean, default: false },
  excludedFields: { type: Set, default: () => new Set() },
  irreversibleFields: { type: Array, default: () => [] },
  warning: { type: String, default: null },
});

const emit = defineEmits(["update:excludedFields"]);

const isSingleItem = computed(() => props.items.length <= 1);

const allFieldKeys = computed(() =>
  props.items.flatMap((item) => (item.fields || []).map((f) => f.key)),
);

const allIncluded = computed(
  () =>
    allFieldKeys.value.length > 0 &&
    allFieldKeys.value.every((k) => !props.excludedFields.has(k)),
);

function isIncluded(key) {
  return !props.excludedFields.has(key);
}

function toggleField(key) {
  const next = new Set(props.excludedFields);
  if (next.has(key)) next.delete(key);
  else next.add(key);
  emit("update:excludedFields", next);
}

function toggleAll() {
  if (allIncluded.value) {
    emit("update:excludedFields", new Set(allFieldKeys.value));
  } else {
    emit("update:excludedFields", new Set());
  }
}

function isIrreversible(key) {
  return props.irreversibleFields.includes(key);
}

function isExcludedRow(key) {
  return props.selectable && props.excludedFields.has(key);
}
</script>

<template>
  <div class="space-y-4">
    <div
      v-if="warning"
      class="flex items-start gap-2 rounded-md border border-warning/30 bg-warning-muted px-3 py-2.5 text-sm text-warning-foreground"
    >
      <AlertTriangle class="mt-0.5 size-4 shrink-0" />
      <span>{{ warning }}</span>
    </div>

    <!-- Single product -->
    <template v-if="isSingleItem && items.length === 1">
      <p class="text-xs text-muted-foreground">{{ $t("fieldChangesTable.diffHint") }}</p>
      <div class="overflow-hidden rounded-md border border-border">
        <Table>
          <TableHeader>
            <TableRow class="hover:bg-transparent border-border bg-muted/40">
              <TableHead
                v-if="selectable"
                class="w-10 px-3 py-2.5"
              >
                <Checkbox
                  :model-value="allIncluded"
                  @update:model-value="toggleAll"
                />
              </TableHead>
              <TableHead
                class="px-3 py-2.5 text-xs font-medium tracking-tight"
                :class="selectable ? 'w-[8.5rem]' : 'w-[9.5rem]'"
              >
                {{ $t("fieldChangesTable.fieldHeader") }}
              </TableHead>
              <TableHead class="min-w-0 px-3 py-2.5 text-xs font-medium tracking-tight">
                <span class="inline-flex items-center gap-1.5">
                  <span class="size-1.5 rounded-full bg-muted-foreground/50" aria-hidden="true" />
                  {{ $t("fieldChangesTable.currentHeader") }}
                </span>
              </TableHead>
              <TableHead class="min-w-0 px-3 py-2.5 text-xs font-medium tracking-tight">
                <span class="inline-flex items-center gap-1.5">
                  <span class="size-1.5 rounded-full bg-success" aria-hidden="true" />
                  {{ $t("fieldChangesTable.newHeader") }}
                </span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow
              v-for="field in items[0].fields"
              :key="field.key"
              class="border-border last:border-b-0"
              :class="isExcludedRow(field.key) ? 'opacity-45' : ''"
            >
              <TableCell v-if="selectable" class="px-3 py-3 align-top whitespace-normal">
                <Checkbox
                  :model-value="isIncluded(field.key)"
                  @update:model-value="toggleField(field.key)"
                />
              </TableCell>
              <TableCell class="px-3 py-3 align-top whitespace-normal">
                <div class="flex items-start gap-1.5">
                  <span class="text-sm font-medium leading-snug text-foreground">
                    {{ field.label }}
                  </span>
                  <span
                    v-if="isIrreversible(field.key)"
                    class="mt-0.5 inline-flex text-warning-foreground"
                    :title="$t('fieldChangesTable.irreversibleModifiers')"
                  >
                    <AlertTriangle class="size-3.5" />
                  </span>
                </div>
              </TableCell>
              <TableCell class="whitespace-normal border-l border-border bg-muted/20 px-3 py-3 align-top">
                <FieldDiffValue
                  side="old"
                  :old-value="field.oldValue"
                  :new-value="field.newValue"
                  show-whitespace-note
                />
              </TableCell>
              <TableCell class="whitespace-normal border-l border-border px-3 py-3 align-top">
                <FieldDiffValue
                  side="new"
                  :old-value="field.oldValue"
                  :new-value="field.newValue"
                />
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </template>

    <!-- Multi product -->
    <template v-else>
      <p v-if="items.length > 0" class="text-xs text-muted-foreground">
        {{ $t("fieldChangesTable.diffHint") }}
      </p>
      <div
        v-for="(item, idx) in items"
        :key="idx"
        class="overflow-hidden rounded-md border border-border"
      >
        <div
          v-if="item.label"
          class="border-b border-border bg-muted/40 px-3 py-2 text-sm font-medium tracking-tight text-foreground"
        >
          {{ item.label }}
        </div>
        <Table>
          <TableHeader>
            <TableRow class="hover:bg-transparent border-border bg-muted/20">
              <TableHead class="w-[9.5rem] px-3 py-2 text-xs font-medium">
                {{ $t("fieldChangesTable.fieldHeader") }}
              </TableHead>
              <TableHead class="min-w-0 px-3 py-2 text-xs font-medium">
                <span class="inline-flex items-center gap-1.5">
                  <span class="size-1.5 rounded-full bg-muted-foreground/50" aria-hidden="true" />
                  {{ $t("fieldChangesTable.currentHeader") }}
                </span>
              </TableHead>
              <TableHead class="min-w-0 px-3 py-2 text-xs font-medium">
                <span class="inline-flex items-center gap-1.5">
                  <span class="size-1.5 rounded-full bg-success" aria-hidden="true" />
                  {{ $t("fieldChangesTable.newHeader") }}
                </span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow
              v-for="field in item.fields"
              :key="field.key"
              class="border-border last:border-b-0"
            >
              <TableCell class="px-3 py-3 align-top text-sm text-muted-foreground whitespace-normal">
                {{ field.label }}
              </TableCell>
              <TableCell class="whitespace-normal border-l border-border bg-muted/20 px-3 py-3 align-top">
                <FieldDiffValue
                  side="old"
                  :old-value="field.oldValue"
                  :new-value="field.newValue"
                  show-whitespace-note
                />
              </TableCell>
              <TableCell class="whitespace-normal border-l border-border px-3 py-3 align-top">
                <FieldDiffValue
                  side="new"
                  :old-value="field.oldValue"
                  :new-value="field.newValue"
                />
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>

      <EmptyState
        v-if="items.length === 0"
        size="sm"
        :title="$t('fieldChangesTable.noChangesTitle')"
        :description="$t('fieldChangesTable.noChangesDescription')"
      />
    </template>
  </div>
</template>
