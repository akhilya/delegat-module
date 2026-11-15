import { createApp, h } from "vue";
import "./src/style.css";
import "./src/components/marketplace/graph/MappingCanvas.vue?vue&type=style&index=0&lang.css";
import "@vue-flow/core/dist/style.css";
import "@vue-flow/core/dist/theme-default.css";
import { VueFlow } from "@vue-flow/core";
import AdapterFieldsNode from "./src/components/marketplace/graph/nodes/AdapterFieldsNode.vue";
import { i18n } from "./src/i18n";

const fields = [
  { fieldKey: "name", label: "Название товара", outputType: "string", group: "Общая информация", isRequired: true, source: { type: "productField", field: "name" } },
  { fieldKey: "desc", label: "Описание", outputType: "string", group: "Общая информация", source: { type: "productField", field: "description" } },
  { fieldKey: "color", label: "Цвет", outputType: "string", group: "Характеристики", isRequired: true },
  { fieldKey: "material", label: "Материал", outputType: "string", group: "Характеристики", description: "Основной материал изделия" },
  { fieldKey: "weight", label: "Вес", outputType: "number", unit: "г", group: "Характеристики" },
  { fieldKey: "season", label: "Сезон", outputType: "select", group: "Характеристики" },
  { fieldKey: "video", label: "Видео", outputType: "file", group: "Медиа" },
  { fieldKey: "pdf", label: "Инструкция PDF", outputType: "file", group: "Медиа" },
  { fieldKey: "sku_dup", label: "Артикул (дубль)", outputType: "string", group: "Прочее", aliasOf: "sku_main" },
  { fieldKey: "legacy", label: "Устаревшее поле", outputType: "string", group: "Прочее", omit: true },
  { fieldKey: "ungrouped1", label: "Поле без группы", outputType: "string" },
];

const analysis = {
  color: { connected: true },
};
const nodes = [
  {
    id: "src",
    type: "default",
    position: { x: -260, y: 160 },
    data: { label: "Источник" },
  },
  {
    id: "adapter-fields",
    type: "adapterFields",
    position: { x: 60, y: 40 },
    data: {
      fields,
      analysis,
      unitWarnings: {},
      availableSources: { categoryAttributes: [], storeAttributes: [] },
      openField: (k) => console.log("openField", k),
      addCustomField: () => console.log("addCustomField"),
    },
  },
];

const app = createApp({
  render: () =>
    h(VueFlow, {
      nodes,
      edges: [
        {
          id: "e1",
          source: "src",
          target: "adapter-fields",
          targetHandle: "f:color",
        },
      ],
      nodeTypes: { adapterFields: AdapterFieldsNode },
      fitViewOnInit: true,
      style: { width: "100%", height: "100%" },
    }),
});
app.use(i18n);
app.mount("#app");
