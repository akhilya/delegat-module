<script setup>
/**
 * Renders one side of a field diff with Catalog Workbench tokens.
 * Semantic tint only on changed spans — whitespace shown as ↵ · → when changed.
 */
import { computed } from "vue";
import { buildFieldDiff, displayFieldValue, splitForDisplay } from "@/lib/fieldDiff";
import { t } from "@/i18n";

const props = defineProps({
  /** 'old' | 'new' */
  side: { type: String, required: true },
  oldValue: { type: [String, Number, Boolean, Object], default: null },
  newValue: { type: [String, Number, Boolean, Object], default: null },
  /** When true, parent already showed a whitespace-only note for this row. */
  showWhitespaceNote: { type: Boolean, default: false },
});

const emptyLabel = computed(() => t("fieldChangesTable.emptyValue"));

const oldText = computed(() =>
  displayFieldValue(props.oldValue, emptyLabel.value),
);
const newText = computed(() =>
  displayFieldValue(props.newValue, emptyLabel.value),
);

const diff = computed(() => buildFieldDiff(oldText.value, newText.value));

const parts = computed(() =>
  props.side === "old" ? diff.value.oldParts : diff.value.newParts,
);

const isEmpty = computed(() => {
  const raw = props.side === "old" ? props.oldValue : props.newValue;
  return raw === null || raw === undefined || raw === "";
});

function partClass(type) {
  if (type === "remove") {
    return "rounded-[2px] bg-error-muted text-foreground";
  }
  if (type === "add") {
    return "rounded-[2px] bg-success-muted text-foreground";
  }
  return "";
}

function wsClass(type) {
  const base =
    "inline-flex min-w-[0.65em] justify-center rounded-[2px] px-0.5 font-mono text-[0.7em] leading-none opacity-90";
  if (type === "remove") return `${base} bg-error-muted text-destructive`;
  if (type === "add") return `${base} bg-success-muted text-success-foreground`;
  return `${base} text-muted-foreground`;
}
</script>

<template>
  <div class="space-y-1.5">
    <p
      v-if="side === 'old' && showWhitespaceNote && diff.whitespaceOnly"
      class="text-[11px] font-medium tracking-tight text-muted-foreground"
    >
      {{ t("fieldChangesTable.whitespaceOnlyDiff") }}
    </p>
    <div
      class="max-h-48 min-w-0 overflow-auto whitespace-pre-wrap break-words font-normal text-foreground [overflow-wrap:anywhere]"
      :class="isEmpty ? 'text-muted-foreground italic' : ''"
    >
      <template v-for="(part, i) in parts" :key="i">
        <span :class="partClass(part.type)">
          <template
            v-for="(seg, j) in splitForDisplay(part.text, part.type !== 'equal')"
            :key="j"
          >
            <span
              v-if="seg.kind === 'ws'"
              :class="wsClass(part.type)"
              :title="seg.raw"
            >{{ seg.display }}</span>
            <template v-else>{{ seg.display }}</template>
          </template>
        </span>
      </template>
    </div>
  </div>
</template>
