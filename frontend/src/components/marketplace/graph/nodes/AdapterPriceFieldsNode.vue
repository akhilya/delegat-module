<script setup>
import { computed, nextTick, ref, watch } from "vue";
import { Handle, Position, useVueFlow } from "@vue-flow/core";
import {
  Banknote,
  AlertTriangle,
  ArrowLeft,
  ArrowLeftRight,
} from "lucide-vue-next";
import { ChevronRight } from "@/lib/icons.js";
import { Button } from "@/components/ui/button";
import { InsetGroupLabel } from "@/components/ui/inset-group";
import { mergeProductFieldSources, pinColor } from "../graphModel.js";
import {
  localizeExternalFieldName,
  localizeStandardSourceName,
} from "../fieldLabels.js";
import { t } from "@/i18n";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import UnitMismatchNotice from "../UnitMismatchNotice.vue";

const props = defineProps({
  id: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const fields = computed(() => props.data.fields || []);
const analysis = computed(() => props.data.analysis || {});

function aliasPrimaryLabel(f) {
  const primary = fields.value.find(
    (x) => x.fieldKey === (f.aliasOf || f.repeatOf),
  );
  return primary?.label || f.aliasOf || f.repeatOf;
}

const avail = computed(() => props.data.availableSources || {});
const productFields = computed(() => mergeProductFieldSources(avail.value));
const categoryAttributes = computed(() => avail.value.categoryAttributes || []);
const storeAttributes = computed(() => avail.value.storeAttributes || []);

function boundLabel(f) {
  const s = f.source;
  if (!s) return "";
  if (s.type === "groupName") return t("marketplace.graph.groupNameLabel");
  if (s.type === "productField")
    return localizeStandardSourceName(
      s.field,
      productFields.value.find((x) => x.key === s.field)?.displayName ||
        s.field,
    );
  if (s.type === "attribute")
    return (
      categoryAttributes.value.find((x) => x.id === s.attributeId)?.name ||
      t("marketplace.graph.attributeFallback", { id: s.attributeId })
    );
  if (s.type === "storeAttribute")
    return (
      storeAttributes.value.find((x) => x.id === s.attributeId)?.name ||
      t("marketplace.graph.attributeFallback", { id: s.attributeId })
    );
  return "";
}

function pillStyle(type) {
  const c = pinColor(type);
  return { background: `${c}1f`, borderColor: `${c}47`, color: c };
}

function fieldTooltip(f) {
  const parts = [];
  if (f.group) parts.push(groupLabel(f.group));
  if (f.description) parts.push(String(f.description).trim());
  if (f.unit) parts.push(t("marketplace.graph.unitLabel", { unit: f.unit }));
  parts.push(t("marketplace.graph.keyLabel", { key: f.fieldKey }));
  return parts.filter(Boolean).join("\n");
}

function fieldLabel(f) {
  const base = localizeExternalFieldName(f.fieldKey, f.label || f.fieldKey);
  if (
    f.unit &&
    !String(base).toLowerCase().includes(String(f.unit).toLowerCase())
  ) {
    return `${base}, ${f.unit}`;
  }
  return base;
}

function pinHidden(f) {
  return !!(f.source || f.aliasOf || f.omit);
}

// Consecutive runs of same-group fields become collapsible sections, mirroring
// AdapterFieldsNode; ungrouped runs render their fields without a header.
const collapsed = ref(new Set(["__hidden"]));
const { updateNodeInternals } = useVueFlow();
const sections = computed(() => {
  const out = [];
  const hidden = [];
  for (const field of fields.value) {
    if (field.omit || field.aliasOf || field.repeatOf) {
      hidden.push(field);
      continue;
    }
    const key = String(field.group || "").trim();
    const last = out[out.length - 1];
    if (last && last.key === key) last.items.push(field);
    else out.push({ key, items: [field] });
  }
  if (hidden.length) out.push({ key: "__hidden", items: hidden });
  return out;
});
const rows = computed(() => {
  const result = [];
  for (const section of sections.value) {
    if (section.key) result.push({ key: section.key, header: true });
    if (!section.key || !collapsed.value.has(section.key)) {
      for (const field of section.items)
        result.push({ key: field.fieldKey, field });
    }
  }
  return result;
});
const fieldRows = computed(() => {
  const positions = new Map();
  rows.value.forEach((row, index) => {
    if (!row.header) positions.set(row.field.fieldKey, index);
  });
  for (const section of sections.value) {
    if (!section.key) continue;
    const index = rows.value.findIndex(
      (r) => r.header && r.key === section.key,
    );
    for (const field of section.items)
      if (!positions.has(field.fieldKey)) positions.set(field.fieldKey, index);
  }
  return positions;
});
function toggleSection(key) {
  const next = new Set(collapsed.value);
  next.has(key) ? next.delete(key) : next.add(key);
  collapsed.value = next;
}
watch(rows, () => nextTick(() => updateNodeInternals([props.id])), {
  flush: "post",
});

const HEADER = 34;
const ROW = 28;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;

function groupLabel(group) {
  const normalized = String(group || "")
    .trim()
    .toLowerCase();
  if (normalized === "__hidden") return t("marketplace.graph.hiddenFields");
  if (normalized === "prices") return t("marketplace.graph.priceGroupValues");
  if (normalized === "price strategy")
    return t("marketplace.graph.priceGroupStrategy");
  return group;
}
</script>

<template>
  <TooltipProvider :delay-duration="150">
    <div
      :class="['vf-card', 'vf-card--adapter', selected && 'vf-card--selected']"
    >
      <Handle
        v-for="f in fields"
        :key="f.fieldKey"
        :id="`f:${f.fieldKey}`"
        type="target"
        :position="Position.Left"
        :connectable="
          !pinHidden(f) &&
          rows.some((row) => row.field?.fieldKey === f.fieldKey)
        "
        :style="{
          top: topFor(fieldRows.get(f.fieldKey) ?? 0),
          background: pinColor(f.outputType),
          opacity:
            pinHidden(f) ||
            !rows.some((row) => row.field?.fieldKey === f.fieldKey)
              ? 0
              : 1,
          pointerEvents:
            pinHidden(f) ||
            !rows.some((row) => row.field?.fieldKey === f.fieldKey)
              ? 'none'
              : 'auto',
        }"
      />

      <div class="vf-adapter__head">
        <Banknote class="size-3.5 text-primary" />
        <span class="vf-card__kind">{{
          t("marketplace.graph.priceHeader")
        }}</span>
        <span class="vf-pin-label">{{ fields.length }}</span>
      </div>

      <div class="vf-adapter__list">
        <template
          v-for="row in rows"
          :key="row.header ? `group:${row.key}` : `field:${row.key}`"
        >
          <Button
            v-if="row.header"
            variant="ghost"
            class="nodrag h-7 w-full justify-start rounded-none px-2"
            :aria-expanded="!collapsed.has(row.key)"
            @click="toggleSection(row.key)"
          >
            <ChevronRight
              class="size-3 shrink-0 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] motion-reduce:transition-none"
              :class="{ 'rotate-90': !collapsed.has(row.key) }"
            />
            <InsetGroupLabel class="mb-0 truncate">{{
              groupLabel(row.key)
            }}</InsetGroupLabel>
          </Button>
          <div
            v-else
            class="vf-adapter__row cursor-pointer"
            @click.stop="data.openField?.(row.field.fieldKey)"
          >
            <Tooltip>
              <TooltipTrigger as-child>
                <span class="vf-adapter__info">
                  <span class="vf-adapter__label">
                    {{ fieldLabel(row.field) }}
                    <span v-if="row.field.isRequired" class="vf-req"> *</span>
                  </span>
                  <span v-if="row.field.unit" class="vf-adapter__unit">
                    {{ row.field.unit }}
                  </span>
                </span>
              </TooltipTrigger>
              <TooltipContent class="max-w-72 whitespace-pre-line">
                {{ fieldTooltip(row.field) }}
              </TooltipContent>
            </Tooltip>

            <span class="vf-adapter__mid">
              <Tooltip v-if="row.field.source">
                <TooltipTrigger as-child>
                  <component
                    :is="
                      analysis[row.field.fieldKey]?.importable
                        ? ArrowLeftRight
                        : ArrowLeft
                    "
                    :class="[
                      'vf-adapter__dir',
                      analysis[row.field.fieldKey]?.importable
                        ? 'vf-adapter__dir--import'
                        : 'vf-adapter__dir--export',
                    ]"
                  />
                </TooltipTrigger>
                <TooltipContent>
                  {{
                    analysis[row.field.fieldKey]?.importable
                      ? t("marketplace.graph.importExportTitle")
                      : t("marketplace.graph.exportOnlyTitle")
                  }}
                </TooltipContent>
              </Tooltip>

              <UnitMismatchNotice
                v-if="row.field.source"
                :warning="data.unitWarnings?.[row.field.fieldKey]"
              />

              <Tooltip v-if="analysis[row.field.fieldKey]?.fanOut">
                <TooltipTrigger as-child>
                  <AlertTriangle
                    class="size-2.5 shrink-0 cursor-help text-amber-500"
                  />
                </TooltipTrigger>
                <TooltipContent>
                  {{ t("marketplace.graph.fanOutPriceUsed") }}
                </TooltipContent>
              </Tooltip>
            </span>

            <span class="vf-adapter__pills">
              <Tooltip v-if="row.field.omit">
                <TooltipTrigger as-child>
                  <span
                    class="vf-adapter__pill vf-adapter__pill--omit"
                    @click.stop="data.openField?.(row.field.fieldKey)"
                    >{{ t("marketplace.graph.omitFieldShort") }}</span
                  >
                </TooltipTrigger>
                <TooltipContent>
                  {{ t("marketplace.graph.omitFieldTitle") }}
                </TooltipContent>
              </Tooltip>

              <Tooltip v-if="row.field.repeatOf">
                <TooltipTrigger as-child>
                  <span
                    class="vf-adapter__pill vf-adapter__pill--alias"
                    @click.stop="data.openField?.(row.field.fieldKey)"
                  >
                    ≈ {{ aliasPrimaryLabel(row.field) }}
                  </span>
                </TooltipTrigger>
                <TooltipContent>
                  {{
                    t("marketplace.graph.repeatOfTitle", {
                      primary: aliasPrimaryLabel(row.field),
                    })
                  }}
                </TooltipContent>
              </Tooltip>

              <Tooltip v-if="row.field.aliasOf">
                <TooltipTrigger as-child>
                  <span
                    class="vf-adapter__pill vf-adapter__pill--alias"
                    @click.stop="data.openField?.(row.field.fieldKey)"
                  >
                    = {{ aliasPrimaryLabel(row.field) }}
                  </span>
                </TooltipTrigger>
                <TooltipContent>
                  {{
                    t("marketplace.graph.aliasMergedShortTitle", {
                      primary: aliasPrimaryLabel(row.field),
                    })
                  }}
                </TooltipContent>
              </Tooltip>

              <Tooltip v-if="row.field.source">
                <TooltipTrigger as-child>
                  <span
                    class="vf-adapter__pill"
                    :style="pillStyle(row.field.outputType)"
                    @click.stop="data.openField?.(row.field.fieldKey)"
                    >{{ boundLabel(row.field) }}</span
                  >
                </TooltipTrigger>
                <TooltipContent>
                  {{
                    t("marketplace.graph.sourceClickTitle", {
                      label: boundLabel(row.field),
                    })
                  }}
                </TooltipContent>
              </Tooltip>
            </span>
          </div>
        </template>
      </div>
    </div>
  </TooltipProvider>
</template>
