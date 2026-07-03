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
  Link,
  Files,
  ArrowRight,
} from "lucide-vue-next";
import { pinColor, pinTypeLabel } from "../graphModel.js";
import NodePreview from "./NodePreview.vue";

const props = defineProps({
  id: String,
  type: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const META = {
  attribute: { icon: Tag, kind: "Атрибут категории" },
  storeAttribute: { icon: Store, kind: "Атрибут магазина" },
  productField: { icon: Database, kind: "Поле товара" },
  static: { icon: Type, kind: "Значение" },
  groupName: { icon: Boxes, kind: "Группа товара" },
  storeFileUrl: { icon: HardDrive, kind: "Файл магазина · ссылка" },
  storeFileContent: { icon: FileText, kind: "Файл магазина · текст" },
  productFileByName: { icon: Link, kind: "Файл товара · ссылка" },
  productFileContentByName: { icon: FileText, kind: "Файл товара · текст" },
  productFilesByExtension: { icon: Files, kind: "Файлы товара · расширение" },
};

// Typed literal nodes are backed by `static` + valueType — label/icon them by their type.
const LITERAL = {
  number: { icon: Hash, kind: "Число" },
  boolean: { icon: ToggleLeft, kind: "Логическое" },
  array: { icon: List, kind: "Список" },
  string: { icon: Type, kind: "Текст" },
};

const info = computed(() => {
  if (props.type === "static")
    return LITERAL[props.data.valueType] || { icon: Type, kind: "Значение" };
  return META[props.type] || META.static;
});
const outType = computed(() => props.data.valueType || "any");
const collapsed = computed(() => !!props.data.collapsed);
</script>

<template>
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
      <span class="vf-card__kind" :title="data.label">{{
        collapsed ? data.label || info.kind : info.kind
      }}</span>
      <ArrowRight
        v-if="type === 'productField' && data.importable === false"
        class="ml-auto size-3 shrink-0 text-muted-foreground/60"
        title="Только экспорт"
      />
    </div>
    <template v-if="!collapsed">
      <div class="vf-card__title" :title="data.label">
        {{ data.label || "—" }}
      </div>
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
</template>
