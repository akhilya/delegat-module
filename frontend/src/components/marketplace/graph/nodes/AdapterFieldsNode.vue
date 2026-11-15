<script setup>
import { computed, ref, watch, nextTick } from "vue";
import { Handle, Position, useVueFlow } from "@vue-flow/core";
import {
  Target,
  AlertTriangle,
  ArrowLeft,
  ArrowLeftRight,
  Plus,
} from "@/lib/icons.js";
import { ChevronRight } from "@/lib/icons.js";
import { Button } from "@/components/ui/button";
import { InsetGroupLabel } from "@/components/ui/inset-group";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import {
  ADD_ADAPTER_FIELD_HANDLE,
  mergeProductFieldSources,
  pinColor,
} from "../graphModel.js";
import {
  localizeExternalFieldName,
  localizeStandardSourceName,
} from "../fieldLabels.js";
import { t } from "@/i18n";
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

function aliasTitle(f) {
  const primary = aliasPrimaryLabel(f);
  return t("marketplace.graph.aliasMergedTitle", { primary });
}

function fanOutTitle(f) {
  if (f.source) {
    return t("marketplace.graph.fanOutSourceUsed");
  }
  return t("marketplace.graph.fanOutImportDisabled");
}

function sourceTitle(f) {
  return t("marketplace.graph.sourceClickTitle", { label: boundLabel(f) });
}

/** Tooltip for adapter field: description, unit, key. */
function fieldTooltip(f) {
  const parts = [];
  if (f.description) parts.push(String(f.description).trim());
  if (f.unit) parts.push(t("marketplace.graph.unitLabel", { unit: f.unit }));
  if (f.minValue != null || f.maxValue != null) {
    const range = [
      f.minValue != null
        ? t("marketplace.graph.rangeFrom", { value: f.minValue })
        : null,
      f.maxValue != null
        ? t("marketplace.graph.rangeTo", { value: f.maxValue })
        : null,
    ]
      .filter(Boolean)
      .join(" ");
    if (range) parts.push(range);
  }
  if (f.maxLength != null) {
    parts.push(t("marketplace.graph.maxLengthLabel", { max: f.maxLength }));
  }
  if (f.maxValues != null) {
    parts.push(t("marketplace.graph.maxValuesLabel", { max: f.maxValues }));
  }
  if (f.allowCustomValues) {
    parts.push(t("marketplace.graph.allowCustomValuesLabel"));
  }
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

const collapsed = ref(new Set(["__hidden"]));
const onlyUnconnected = ref(false);
const { updateNodeInternals } = useVueFlow();
const sections = computed(() => {
  const groups = new Map();
  for (const field of fields.value) {
    const key =
      field.omit || field.aliasOf || field.repeatOf
        ? "__hidden"
        : field.group || "__other";
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(field);
  }
  return [...groups].map(([key, items]) => ({ key, items }));
});
const rows = computed(() => {
  const result = [];
  for (const section of sections.value) {
    const isCollapsed = collapsed.value.has(section.key);
    const items = isCollapsed
      ? []
      : section.items.filter(
          (field) =>
            !onlyUnconnected.value ||
            (!field.source && !analysis.value[field.fieldKey]?.connected),
        );
    // Expanded sections emptied by the "unconnected only" filter get no
    // orphan header row.
    if (!isCollapsed && !items.length) continue;
    result.push({ key: section.key, header: true });
    for (const field of items) result.push({ key: field.fieldKey, field });
  }
  return result;
});
const fieldRows = computed(() => {
  const positions = new Map();
  rows.value.forEach((row, index) => {
    if (!row.header) positions.set(row.field.fieldKey, index);
  });
  for (const section of sections.value) {
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
function sectionLabel(key) {
  return key === "__hidden"
    ? t("marketplace.graph.hiddenFields")
    : key === "__other"
      ? t("marketplace.graph.otherFields")
      : key;
}
watch(rows, () => nextTick(() => updateNodeInternals([props.id])), {
  flush: "post",
});
const HEADER = 30;
const ROW = 24;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;
</script>

<template>
  <TooltipProvider :delay-duration="300">
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

      <Handle
        :id="ADD_ADAPTER_FIELD_HANDLE"
        type="target"
        :position="Position.Left"
        :style="{
          top: topFor(rows.length),
          background: pinColor('any'),
        }"
      />

      <div class="vf-adapter__head">
        <Target class="size-3.5 text-primary" />
        <span class="vf-card__kind">{{
          t("marketplace.graph.adapterHeader")
        }}</span>
        <span class="vf-pin-label">{{ fields.length }}</span>
        <Button
          :variant="onlyUnconnected ? 'secondary' : 'ghost'"
          size="sm"
          class="nodrag ml-auto h-6 px-1 text-xs"
          :aria-pressed="onlyUnconnected"
          @click.stop="onlyUnconnected = !onlyUnconnected"
          >{{ t("marketplace.graph.unconnectedOnly") }}</Button
        >
      </div>

      <div class="vf-adapter__list">
        <div
          v-if="!rows.length"
          class="px-2 py-1 text-xs text-muted-foreground"
        >
          {{ t("marketplace.graph.allFieldsConnected") }}
        </div>
        <template
          v-for="row in rows"
          :key="row.header ? `group:${row.key}` : `field:${row.key}`"
        >
          <Button
            v-if="row.header"
            variant="ghost"
            class="nodrag h-6 w-full justify-start rounded-none px-2"
            :aria-expanded="!collapsed.has(row.key)"
            @click.stop="toggleSection(row.key)"
          >
            <ChevronRight
              class="size-3 shrink-0 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] motion-reduce:transition-none"
              :class="{ 'rotate-90': !collapsed.has(row.key) }"
            />
            <InsetGroupLabel class="mb-0 truncate">{{
              sectionLabel(row.key)
            }}</InsetGroupLabel>
          </Button>
          <div
            v-else
            class="vf-adapter__row cursor-pointer"
            @click.stop="data.openField?.(row.field.fieldKey)"
          >
            <span class="vf-adapter__info">
              <Tooltip>
                <TooltipTrigger as-child>
                  <span class="vf-adapter__label">
                    {{ fieldLabel(row.field) }}
                    <span v-if="row.field.isRequired" class="vf-req"> *</span>
                  </span>
                </TooltipTrigger>
                <TooltipContent class="max-w-72 whitespace-pre-line">
                  {{ fieldTooltip(row.field) }}
                </TooltipContent>
              </Tooltip>
              <span v-if="row.field.unit" class="vf-adapter__unit">
                {{ row.field.unit }}
              </span>
            </span>

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
                  {{ fanOutTitle(row.field) }}
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
                  {{ aliasTitle(row.field) }}
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
                  {{ sourceTitle(row.field) }}
                </TooltipContent>
              </Tooltip>
            </span>
          </div>
        </template>
        <Tooltip>
          <TooltipTrigger as-child>
            <div
              class="vf-adapter__row vf-adapter__row--add cursor-pointer"
              @click.stop="data.addCustomField?.()"
            >
              <Plus class="size-3 shrink-0 text-muted-foreground" />
              <span class="vf-adapter__label text-muted-foreground">{{
                t("marketplace.graph.addCustomField")
              }}</span>
            </div>
          </TooltipTrigger>
          <TooltipContent>
            {{ t("marketplace.graph.addCustomFieldHint") }}
          </TooltipContent>
        </Tooltip>
      </div>
    </div>
  </TooltipProvider>
</template>
