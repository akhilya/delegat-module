<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import {
  Banknote,
  AlertTriangle,
  ArrowLeft,
  ArrowLeftRight,
} from "lucide-vue-next";
import { mergeProductFieldSources, pinColor } from "../graphModel.js";
import {
  localizeExternalFieldName,
  localizeStandardSourceName,
} from "../fieldLabels.js";
import {
  PRICE_FIELD_GROUP_HEADER_HEIGHT,
  priceFieldGroupCount,
} from "../graphPriceFields.js";
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
  const primary = fields.value.find((x) => x.fieldKey === f.aliasOf);
  return primary?.label || f.aliasOf;
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

const HEADER = 30;
const ROW = 24;
const topFor = (i) => {
  const groupOffset =
    priceFieldGroupCount(fields.value.slice(0, i + 1)) *
    PRICE_FIELD_GROUP_HEADER_HEIGHT;
  return `${HEADER + i * ROW + groupOffset + ROW / 2}px`;
};

function isGroupStart(index) {
  const group = String(fields.value[index]?.group || "").trim();
  const previous = String(fields.value[index - 1]?.group || "").trim();
  return !!group && group !== previous;
}

function groupLabel(group) {
  const normalized = String(group || "")
    .trim()
    .toLowerCase();
  if (normalized === "prices") return t("marketplace.graph.priceGroupValues");
  if (normalized === "price strategy")
    return t("marketplace.graph.priceGroupStrategy");
  return group;
}
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <div
      :class="['vf-card', 'vf-card--adapter', selected && 'vf-card--selected']"
    >
      <Handle
        v-for="(f, i) in fields"
        :key="f.fieldKey"
        :id="`f:${f.fieldKey}`"
        type="target"
        :position="Position.Left"
        :connectable="!pinHidden(f)"
        :style="{
          top: topFor(i),
          background: pinColor(f.outputType),
          opacity: pinHidden(f) ? 0 : 1,
          pointerEvents: pinHidden(f) ? 'none' : 'auto',
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
        <template v-for="(f, i) in fields" :key="f.fieldKey">
          <div
            v-if="isGroupStart(i)"
            :class="[
              'vf-adapter__group',
              i === 0 && 'vf-adapter__group--first',
            ]"
          >
            {{ groupLabel(f.group) }}
          </div>
          <div
            class="vf-adapter__row cursor-pointer"
            @click.stop="data.openField?.(f.fieldKey)"
          >
            <Tooltip>
              <TooltipTrigger as-child>
                <span class="vf-adapter__label">
                  {{ fieldLabel(f)
                  }}<span v-if="f.isRequired" class="vf-req"> *</span>
                </span>
              </TooltipTrigger>
              <TooltipContent class="max-w-72 whitespace-pre-line">
                {{ fieldTooltip(f) }}
              </TooltipContent>
            </Tooltip>

            <UnitMismatchNotice
              v-if="f.source"
              :warning="data.unitWarnings?.[f.fieldKey]"
            />

            <Tooltip v-if="f.omit">
              <TooltipTrigger as-child>
                <span
                  class="vf-adapter__pill vf-adapter__pill--omit"
                  @click.stop="data.openField?.(f.fieldKey)"
                  >{{ t("marketplace.graph.omitFieldShort") }}</span
                >
              </TooltipTrigger>
              <TooltipContent>
                {{ t("marketplace.graph.omitFieldTitle") }}
              </TooltipContent>
            </Tooltip>

            <Tooltip v-if="f.aliasOf">
              <TooltipTrigger as-child>
                <span
                  class="vf-adapter__pill vf-adapter__pill--alias"
                  @click.stop="data.openField?.(f.fieldKey)"
                >
                  = {{ aliasPrimaryLabel(f) }}
                </span>
              </TooltipTrigger>
              <TooltipContent>
                {{
                  t("marketplace.graph.aliasMergedShortTitle", {
                    primary: aliasPrimaryLabel(f),
                  })
                }}
              </TooltipContent>
            </Tooltip>

            <component
              v-if="f.source"
              :is="
                analysis[f.fieldKey]?.importable ? ArrowLeftRight : ArrowLeft
              "
              :class="[
                'vf-adapter__dir',
                analysis[f.fieldKey]?.importable
                  ? 'vf-adapter__dir--import'
                  : 'vf-adapter__dir--export',
              ]"
            />

            <Tooltip v-if="analysis[f.fieldKey]?.fanOut">
              <TooltipTrigger as-child>
                <AlertTriangle
                  class="size-2.5 shrink-0 cursor-help text-amber-500"
                />
              </TooltipTrigger>
              <TooltipContent>
                {{ t("marketplace.graph.fanOutPriceUsed") }}
              </TooltipContent>
            </Tooltip>

            <Tooltip v-if="f.source">
              <TooltipTrigger as-child>
                <span
                  class="vf-adapter__pill"
                  :style="pillStyle(f.outputType)"
                  @click.stop="data.openField?.(f.fieldKey)"
                  >{{ boundLabel(f) }}</span
                >
              </TooltipTrigger>
              <TooltipContent>
                {{
                  t("marketplace.graph.sourceClickTitle", {
                    label: boundLabel(f),
                  })
                }}
              </TooltipContent>
            </Tooltip>
          </div>
        </template>
      </div>
    </div>
  </TooltipProvider>
</template>
