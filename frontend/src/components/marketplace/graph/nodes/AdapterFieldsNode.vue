<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import { Target, AlertTriangle, ArrowLeft, ArrowLeftRight } from "lucide-vue-next";
import { pinColor } from "../graphModel.js";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";

const props = defineProps({
  id: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const fields = computed(() => props.data.fields || []);
const analysis = computed(() => props.data.analysis || {});
const avail = computed(() => props.data.availableSources || {});
const productFields = computed(() => avail.value.productFields || []);
const categoryAttributes = computed(() => avail.value.categoryAttributes || []);
const storeAttributes = computed(() => avail.value.storeAttributes || []);

function boundLabel(f) {
  const s = f.source;
  if (!s) return "";
  if (s.type === "groupName") return "Название группы";
  if (s.type === "productField")
    return productFields.value.find((x) => x.key === s.field)?.displayName || s.field;
  if (s.type === "attribute")
    return categoryAttributes.value.find((x) => x.id === s.attributeId)?.name || `атрибут #${s.attributeId}`;
  if (s.type === "storeAttribute")
    return storeAttributes.value.find((x) => x.id === s.attributeId)?.name || `атрибут #${s.attributeId}`;
  return "";
}

function pillStyle(type) {
  const c = pinColor(type);
  return { background: `${c}1f`, borderColor: `${c}47`, color: c };
}

const HEADER = 30;
const ROW = 24;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;
</script>

<template>
  <div :class="['vf-card', 'vf-card--adapter', selected && 'vf-card--selected']">
    <template v-for="(f, i) in fields" :key="f.fieldKey">
      <Handle
        v-if="!f.source"
        :id="`f:${f.fieldKey}`"
        type="target"
        :position="Position.Left"
        :style="{ top: topFor(i), background: pinColor(f.outputType) }"
      />
    </template>

    <div class="vf-adapter__head">
      <Target class="size-3.5 text-primary" />
      <span class="vf-card__kind">Поля адаптера</span>
      <span class="vf-pin-label">{{ fields.length }}</span>
    </div>

    <div class="vf-adapter__list">
      <div v-for="f in fields" :key="f.fieldKey" class="vf-adapter__row cursor-pointer" @click.stop="data.openField?.(f.fieldKey)">
        <span class="vf-adapter__label" :title="f.fieldKey">
          {{ f.label || f.fieldKey }}<span v-if="f.isRequired" class="vf-req"> *</span>
        </span>

        <!-- Direction indicator for pill-bound fields only (wire fields show direction on the canvas edge) -->
        <component
          v-if="f.source"
          :is="analysis[f.fieldKey]?.importable ? ArrowLeftRight : ArrowLeft"
          :class="['vf-adapter__dir', analysis[f.fieldKey]?.importable ? 'vf-adapter__dir--import' : 'vf-adapter__dir--export']"
        />

        <!-- Fan-out warning -->
        <TooltipProvider v-if="analysis[f.fieldKey]?.fanOut" :delay-duration="200">
          <Tooltip>
            <TooltipTrigger as-child>
              <AlertTriangle class="size-2.5 shrink-0 cursor-help text-amber-500" />
            </TooltipTrigger>
            <TooltipContent side="top" class="max-w-52 text-xs">
              {{
                f.source
                  ? "Этот источник уже используется другим полем — возможен конфликт данных"
                  : "Импорт отключён: этот источник уже используется несколькими полями адаптера"
              }}
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>

        <!-- Direct-binding pill -->
        <TooltipProvider v-if="f.source" :delay-duration="400">
          <Tooltip>
            <TooltipTrigger as-child>
              <span
                class="vf-adapter__pill"
                :style="pillStyle(f.outputType)"
                @click.stop="data.openField?.(f.fieldKey)"
              >{{ boundLabel(f) }}</span>
            </TooltipTrigger>
            <TooltipContent side="top" class="text-xs">
              <div class="font-medium">{{ boundLabel(f) }}</div>
              <div class="text-muted-foreground">Нажмите для настройки</div>
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
      </div>
    </div>
  </div>
</template>
