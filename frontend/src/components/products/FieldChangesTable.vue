<script setup>
/**
 * FieldChangesTable — unified before/after comparison table for import previews.
 *
 * Handles:
 * - Single-item with checkboxes (marketplace single import)
 * - Multi-item without checkboxes (bulk import, excel import)
 * - Irreversible field warnings
 *
 * Props:
 *  items: [{ label?, fields: [{ key, label, oldValue, newValue }] }]
 *  selectable: boolean — show per-field checkboxes
 *  excludedFields: Set<string> — v-model for checkbox state
 *  irreversibleFields: string[] — fields that get a warning icon
 *  warning: string — optional warning banner text
 */
import { computed } from "vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { AlertTriangle } from "lucide-vue-next";

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
  props.items.flatMap((item) => item.fields.map((f) => f.key)),
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
</script>

<template>
  <div class="space-y-4">
    <!-- Warning banner -->
    <div
      v-if="warning"
      class="flex items-start gap-2 p-3 rounded-md bg-warning-muted border border-warning/30 text-warning-foreground text-sm"
    >
      <AlertTriangle class="size-4 mt-0.5 shrink-0" />
      <span>{{ warning }}</span>
    </div>

    <!-- Single item: flat table -->
    <template v-if="isSingleItem && items.length === 1">
      <table class="w-full table-fixed text-sm">
        <thead>
          <tr class="border-b">
            <th v-if="selectable" class="w-[40px] py-2 px-2">
              <Checkbox :model-value="allIncluded" @update:model-value="toggleAll" />
            </th>
            <th class="text-left py-2 px-2 font-medium text-muted-foreground" :class="selectable ? 'w-[100px]' : 'w-1/4'">Поле</th>
            <th class="text-left py-2 px-2 font-medium text-muted-foreground">Текущее</th>
            <th class="text-left py-2 px-2 font-medium text-muted-foreground">Новое</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="field in items[0].fields"
            :key="field.key"
            class="border-b last:border-b-0"
          >
            <td v-if="selectable" class="py-2 px-2 align-top">
              <Checkbox
                :model-value="isIncluded(field.key)"
                @update:model-value="toggleField(field.key)"
              />
            </td>
            <td class="py-2 px-2 font-medium align-top">
              <div class="flex items-center gap-1">
                {{ field.label }}
                <span
                  v-if="isIrreversible(field.key)"
                  class="inline-flex items-center text-warning-foreground"
                  title="Необратимые модификаторы"
                >
                  <AlertTriangle class="size-3.5" />
                </span>
              </div>
            </td>
            <td class="py-2 px-3 break-words whitespace-pre-wrap align-top">
              <span class="bg-error/10 text-error-foreground px-1 rounded">{{ field.oldValue ?? "(пусто)" }}</span>
            </td>
            <td class="py-2 px-3 break-words whitespace-pre-wrap align-top">
              <span class="bg-success/10 text-success-foreground px-1 rounded">{{ field.newValue ?? "(пусто)" }}</span>
            </td>
          </tr>
        </tbody>
      </table>
    </template>

    <!-- Multi item: grouped by product -->
    <template v-else>
      <div
        v-for="(item, idx) in items"
        :key="idx"
        class="border rounded-lg overflow-hidden"
      >
        <div v-if="item.label" class="px-3 py-2 bg-muted/50 border-b text-sm font-medium">
          {{ item.label }}
        </div>
        <table class="w-full table-fixed text-sm">
          <thead>
            <tr class="border-b">
              <th class="w-1/4 text-left py-2 px-3 font-medium text-muted-foreground">Поле</th>
              <th class="text-left py-2 px-3 font-medium text-muted-foreground">Текущее</th>
              <th class="text-left py-2 px-3 font-medium text-muted-foreground">Новое</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="field in item.fields"
              :key="field.key"
              class="border-b last:border-b-0"
            >
              <td class="py-2 px-3 text-muted-foreground align-top">{{ field.label }}</td>
              <td class="py-2 px-3 break-words whitespace-pre-wrap align-top">
                <span class="bg-error/10 text-error-foreground px-1 rounded">
                  {{ field.oldValue || "(пусто)" }}
                </span>
              </td>
              <td class="py-2 px-3 break-words whitespace-pre-wrap align-top">
                <span class="bg-success/10 text-success-foreground px-1 rounded">
                  {{ field.newValue || "(пусто)" }}
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="items.length === 0" class="text-center text-sm text-muted-foreground py-8">
        Изменений не обнаружено
      </div>
    </template>
  </div>
</template>
