<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import {
  Tag,
  Store,
  Database,
  Type,
  Boxes,
  HardDrive,
  FileText,
  Hash,
  ToggleLeft,
  List,
  ListChecks,
  Link,
  Files,
  ArrowRight,
} from "lucide-vue-next";
import { pinColor, pinTypeLabel } from "../graphModel.js";
import NodePreview from "./NodePreview.vue";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { t } from "@/i18n";

const props = defineProps({
  id: String,
  type: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const META = computed(() => ({
  attribute: { icon: Tag, kind: t("marketplace.graph.nodeKinds.attribute") },
  storeAttribute: {
    icon: Store,
    kind: t("marketplace.graph.nodeKinds.storeAttribute"),
  },
  productField: {
    icon: Database,
    kind: t("marketplace.graph.nodeKinds.productField"),
  },
  static: { icon: Type, kind: t("marketplace.graph.nodeKinds.static") },
  groupName: { icon: Boxes, kind: t("marketplace.graph.nodeKinds.groupName") },
  storeFileUrl: {
    icon: HardDrive,
    kind: t("marketplace.graph.nodeKinds.storeFileUrl"),
  },
  storeFileContent: {
    icon: FileText,
    kind: t("marketplace.graph.nodeKinds.storeFileContent"),
  },
  productFileByName: {
    icon: Link,
    kind: t("marketplace.graph.nodeKinds.productFileByName"),
  },
  productFileContentByName: {
    icon: FileText,
    kind: t("marketplace.graph.nodeKinds.productFileContentByName"),
  },
  productFilesByExtension: {
    icon: Files,
    kind: t("marketplace.graph.nodeKinds.productFilesByExtension"),
  },
}));

// Typed literal nodes are backed by `static` + valueType - label/icon them by their type.
const LITERAL = computed(() => ({
  number: { icon: Hash, kind: t("marketplace.graph.literalKinds.number") },
  boolean: {
    icon: ToggleLeft,
    kind: t("marketplace.graph.literalKinds.boolean"),
  },
  array: { icon: List, kind: t("marketplace.graph.literalKinds.array") },
  string: { icon: Type, kind: t("marketplace.graph.literalKinds.string") },
}));

const info = computed(() => {
  if (props.type === "static") {
    if (props.data.isAdapterValue)
      return {
        icon: ListChecks,
        kind: t("marketplace.graph.nodeKinds.adapterValue"),
      };
    return (
      LITERAL.value[props.data.valueType] || {
        icon: Type,
        kind: t("marketplace.graph.nodeKinds.static"),
      }
    );
  }
  return META.value[props.type] || META.value.static;
});
const outType = computed(() => props.data.valueType || "any");
const collapsed = computed(() => !!props.data.collapsed);
</script>

<template>
  <TooltipProvider :delay-duration="150">
    <div
      :class="[
        'vf-card',
        'vf-card--source',
        selected && 'vf-card--selected',
        collapsed && 'vf-card--collapsed',
      ]"
    >
      <div class="vf-card__head">
        <component :is="info.icon" class="size-3.5 text-muted-foreground" />
        <Tooltip :disabled="!data.label">
          <TooltipTrigger as-child>
            <span class="vf-card__kind">{{
              collapsed ? data.label || info.kind : info.kind
            }}</span>
          </TooltipTrigger>
          <TooltipContent>
            {{ data.label }}
          </TooltipContent>
        </Tooltip>
        <Tooltip
          v-if="
            ['productField', 'attribute', 'storeAttribute'].includes(type) &&
            data.importable === false
          "
        >
          <TooltipTrigger as-child>
            <ArrowRight
              class="ml-auto size-3 shrink-0 text-muted-foreground/60"
            />
          </TooltipTrigger>
          <TooltipContent>
            {{ t("marketplace.graph.exportOnlyTitle") }}
          </TooltipContent>
        </Tooltip>
      </div>
      <template v-if="!collapsed">
        <Tooltip :disabled="!data.label">
          <TooltipTrigger as-child>
            <div class="vf-card__title">
              {{ data.label || "-" }}
            </div>
          </TooltipTrigger>
          <TooltipContent>
            {{ data.label }}
          </TooltipContent>
        </Tooltip>
        <div class="vf-card__pin vf-card__pin--right">
          <span class="vf-pin-label">{{ pinTypeLabel(outType) }}</span>
          <span class="vf-dot" :style="{ background: pinColor(outType) }" />
        </div>
        <NodePreview :value="data.previewValue" />
      </template>
      <Handle
        id="out"
        type="source"
        :position="Position.Right"
        :style="{ background: pinColor(outType) }"
      />
    </div>
  </TooltipProvider>
</template>
