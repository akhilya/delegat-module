<script setup>
import {
  ref,
  computed,
  onMounted,
  onBeforeUnmount,
  watch,
  nextTick,
} from "vue";
import { useStore } from "vuex";
import { VueFlow, useVueFlow } from "@vue-flow/core";
import { Background } from "@vue-flow/background";
import { Controls } from "@vue-flow/controls";
import { MiniMap } from "@vue-flow/minimap";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  Save,
  AlertTriangle,
  EyeOff,
  Loader2,
  Activity,
} from "lucide-vue-next";
import {
  SplitterGroup,
  SplitterPanel,
  SplitterResizeHandle,
} from "reka-ui";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import NodeContextMenu from "./NodeContextMenu.vue";
import NodeInspector from "./NodeInspector.vue";
import SearchableSelect from "@/components/ui/searchable-select/SearchableSelect.vue";

import SourceNode from "./nodes/SourceNode.vue";
import ModifierNode from "./nodes/ModifierNode.vue";
import OpNode from "./nodes/OpNode.vue";
import AdapterFieldsNode from "./nodes/AdapterFieldsNode.vue";
import AdapterPriceFieldsNode from "./nodes/AdapterPriceFieldsNode.vue";
import RerouteNode from "./nodes/RerouteNode.vue";
import ImportableEdge from "./edges/ImportableEdge.vue";
import ExportOnlyEdge from "./edges/ExportOnlyEdge.vue";
import {
  autoMigrate,
  validateGraph,
  genId,
  createSourceNode,
  createModifierNode,
  createOpNode,
  createRerouteNode,
  syncAdapterFields,
  syncAdapterPriceFields,
  ensurePriceNode,
  ensureModifierParamDefaults,
  analyzeFields,
  adapterSourceLabels,
  nodeOutputType,
  nodeInputType,
  firstInputHandle,
  ADAPTER_NODE_ID,
  ADAPTER_PRICE_NODE_ID,
  sanitizeGraphNodes,
  sanitizeGraphEdges,
  mergeProductFieldSources,
} from "./graphModel.js";
import { layoutGraph } from "./graphLayout.js";

import "@vue-flow/core/dist/style.css";
import "@vue-flow/core/dist/theme-default.css";
import "@vue-flow/controls/dist/style.css";
import "@vue-flow/minimap/dist/style.css";

const props = defineProps({
  connectionId: { type: [Number, String], required: true },
  mappingId: { type: [Number, String], required: true },
  fieldMappings: { type: Array, default: () => [] },
  // Live field definitions from /external-fields: { [fieldKey]: { name, type, options: [{value,label}] } }
  externalFieldDefs: { type: Object, default: () => ({}) },
  priceFieldMappings: { type: Array, default: () => [] },
  priceFieldDefs: { type: Object, default: () => ({}) },
  availableSources: { type: Object, default: () => ({}) },
  previewProductId: { type: [Number, String], default: null },
  previewProducts: { type: Array, default: () => [] },
});
const emit = defineEmits([
  "preview-result",
  "clear-preview",
  "select-preview",
  "fields-synced",
]);

const store = useStore();
const vf = useVueFlow();
const {
  onConnect,
  onConnectStart,
  onConnectEnd,
  findNode,
  addEdges,
  addNodes,
  removeNodes,
  removeEdges,
  setNodes,
  setEdges,
  updateNodeData,
  updateNodeInternals,
  toObject,
  fitView,
  onNodeClick,
  onPaneClick,
  onNodesChange,
  onEdgesChange,
  onEdgeDoubleClick,
  onNodeDoubleClick,
  getSelectedNodes,
  addSelectedNodes,
  removeSelectedNodes,
  nodes,
  edges,
} = vf;

const flowWrap = ref(null);
const flowPaneReady = ref(false);
let pendingLoadOptions = null;
let loadGeneration = 0;
let previewGeneration = 0;

const loading = ref(true);
const saving = ref(false);
const syncingFields = ref(false);
const dirty = ref(false);
const previewing = ref(false);
const selected = ref(null);
const focusAdapterField = ref(null);
const menu = ref({
  open: false,
  left: 0,
  top: 0,
  clientX: 0,
  clientY: 0,
  connectType: null,
  connectDir: null,
});
// Tracks an in-progress wire drag started from an output pin (for drag-to-empty → node menu).
const pendingConnect = ref(null);
const connectionMade = ref(false);
const lastMouse = ref({ x: 0, y: 0 });
const rightDown = ref(null);
const rightMoved = ref(false);
const maximized = ref(false);
const debugMode = ref(false);
const animateWires = ref(true);

// ---- Undo / Redo ----
const history = ref([]);
const histIdx = ref(-1);
let isRestoring = false;
let historyPending = false;

function makeSnapshot() {
  return {
    nodes: nodes.value.map((n) => {
      if (n.id === ADAPTER_NODE_ID || n.id === ADAPTER_PRICE_NODE_ID) {
        const { analysis, sources, setFieldSource, availableSources, openField, fieldOptionsMap, ...rest } = n.data || {};
        return JSON.parse(JSON.stringify({ ...n, data: rest }));
      }
      return JSON.parse(JSON.stringify(n));
    }),
    edges: JSON.parse(JSON.stringify(edges.value)),
  };
}

function scheduleHistory() {
  if (isRestoring || historyPending) return;
  historyPending = true;
  nextTick(() => {
    history.value = history.value.slice(0, histIdx.value + 1);
    history.value.push(makeSnapshot());
    if (history.value.length > 60) history.value.shift();
    else histIdx.value++;
    historyPending = false;
  });
}

function restoreAt(idx) {
  const snap = history.value[idx];
  if (!snap) return;
  isRestoring = true;
  setNodes(snap.nodes);
  setEdges(snap.edges);
  histIdx.value = idx;
  nextTick(() => {
    refreshAnalysis();
    schedulePreview();
    isRestoring = false;
  });
}

const canUndo = computed(() => histIdx.value > 0);
const canRedo = computed(() => histIdx.value < history.value.length - 1);;

const debugInfo = computed(() => {
  const { analysis } = analyzeFields(nodes.value, edges.value);
  return {
    analysis,
    edges: edges.value.map((e) => ({
      id: e.id,
      type: e.type ?? "default",
      src: e.source,
      tgt: e.target,
    })),
  };
});

const issues = computed(() => validateGraph(nodes.value, edges.value));
const errorCount = computed(
  () => issues.value.filter((i) => i.level === "error").length,
);
const warningCount = computed(
  () => issues.value.filter((i) => i.level === "warning").length,
);
function waitForPaint() {
  return new Promise((resolve) => {
    requestAnimationFrame(() => requestAnimationFrame(resolve));
  });
}

async function loadStep(_phase, fn) {
  try {
    return await fn();
  } catch (e) {
    displaySonnerError(e);
    throw e;
  }
}

function requestLoad(options = {}) {
  if (!props.connectionId || !props.mappingId) return;
  if (!flowPaneReady.value) {
    pendingLoadOptions = options;
    return;
  }
  load(options);
}

// Per-field import analysis + the data the adapter sink needs for its inline source dropdown.
// Lane typing (setEdges) is split out so load/preview can defer it until the canvas has painted.
function enrichFieldsWithDefs(fields, defs) {
  if (!fields?.length) return fields || [];
  return fields.map((f) => {
    const def = defs?.[f.fieldKey];
    if (!def) return f;
    return {
      ...f,
      label: def.name || f.label,
      outputType: def.type || f.outputType,
      description: def.description ?? f.description ?? null,
      unit: def.unit ?? f.unit ?? null,
      minValue: def.minValue ?? f.minValue ?? null,
      maxValue: def.maxValue ?? f.maxValue ?? null,
    };
  });
}

/** Enrich adapter field labels/tooltips once — not on every refreshAnalysis (avoids re-render thrash). */
function applyFieldDefEnrichment() {
  const adapterNode = findNode(ADAPTER_NODE_ID);
  if (adapterNode) {
    updateNodeData(ADAPTER_NODE_ID, {
      fields: enrichFieldsWithDefs(adapterNode.data?.fields, props.externalFieldDefs),
    });
  }
  const priceNode = findNode(ADAPTER_PRICE_NODE_ID);
  if (priceNode) {
    updateNodeData(ADAPTER_PRICE_NODE_ID, {
      fields: enrichFieldsWithDefs(priceNode.data?.fields, props.priceFieldDefs),
    });
  }
}

function refreshAnalysis(options = {}) {
  const { updateLanes = true } = options;
  const { analysisBySink } = analyzeFields(nodes.value, edges.value);
  const fieldOptionsMap = {};
  for (const [key, def] of Object.entries(props.externalFieldDefs)) {
    if (def.options?.length) fieldOptionsMap[key] = def.options;
  }
  updateNodeData(ADAPTER_NODE_ID, {
    analysis: analysisBySink[ADAPTER_NODE_ID] || {},
    fieldOptionsMap,
    sources: adapterSourceLabels(nodes.value, edges.value),
    availableSources: props.availableSources,
    setFieldSource,
    openField: openAdapterField,
  });

  const priceFieldOptionsMap = {};
  for (const [key, def] of Object.entries(props.priceFieldDefs)) {
    if (def.options?.length) priceFieldOptionsMap[key] = def.options;
  }
  const priceNode = findNode(ADAPTER_PRICE_NODE_ID);
  if (priceNode) {
    const sinkAnalysis = analysisBySink[ADAPTER_PRICE_NODE_ID] || {};
    const priceAnalysis = {};
    for (const f of priceNode.data?.fields || []) {
      if (!f.fieldKey) continue;
      priceAnalysis[f.fieldKey] = sinkAnalysis[f.fieldKey] ?? {
        connected: !!(f.source || (edges.value || []).some(
          (e) => e.target === ADAPTER_PRICE_NODE_ID && e.targetHandle === `f:${f.fieldKey}`,
        )),
        importable: false,
      };
    }
    updateNodeData(ADAPTER_PRICE_NODE_ID, {
      analysis: priceAnalysis,
      fieldOptionsMap: priceFieldOptionsMap,
      sources: adapterSourceLabels(nodes.value, edges.value),
      availableSources: props.availableSources,
      setFieldSource: setPriceFieldSource,
      openField: openPriceField,
    });
  }

  if (updateLanes) applyEdgeLaneTypes(analysisBySink);
}

// Mark edges by lane type: importable (bidirectional) vs export-only (one-way).
function pruneSourceBoundEdges() {
  const toRemove = [];
  for (const sinkId of [ADAPTER_NODE_ID, ADAPTER_PRICE_NODE_ID]) {
    const sink = findNode(sinkId);
    if (!sink) continue;
    const boundHandles = new Set(
      (sink.data?.fields || [])
        .filter((f) => f.source || f.aliasOf)
        .map((f) => `f:${f.fieldKey}`),
    );
    for (const e of edges.value) {
      if (e.target === sinkId && boundHandles.has(e.targetHandle)) {
        toRemove.push(e.id);
      }
    }
  }
  if (toRemove.length) removeEdges(toRemove);
}

function applyEdgeLaneTypes(analysisBySink = null) {
  const sinkAnalysis = analysisBySink ?? analyzeFields(nodes.value, edges.value).analysisBySink;
  const importableIds = new Set();
  const exportOnlyIds = new Set();
  const adapterIncoming = edges.value.filter(
    (e) => e.target === ADAPTER_NODE_ID || e.target === ADAPTER_PRICE_NODE_ID,
  );
  for (const e of adapterIncoming) {
    const key = e.targetHandle?.slice(2);
    const a = sinkAnalysis[e.target]?.[key];
    if (!a?.connected) continue;
    const target = a.importable ? importableIds : exportOnlyIds;
    target.add(e.id);
    const visited = new Set();
    const nodeQueue = [e.source];
    while (nodeQueue.length) {
      const nid = nodeQueue.shift();
      if (visited.has(nid)) continue;
      visited.add(nid);
      for (const edge of edges.value) {
        if (edge.target === nid) {
          target.add(edge.id);
          if (!visited.has(edge.source)) nodeQueue.push(edge.source);
        }
      }
    }
  }
  const laneType = (e) =>
    exportOnlyIds.has(e.id) ? "vf-export" : importableIds.has(e.id) ? "vf-import" : "default";
  const needsUpdate = edges.value.some((e) => (e.type || "default") !== laneType(e));
  if (needsUpdate) {
    setEdges(
      edges.value.map((e) => {
        const type = laneType(e);
        return (e.type || "default") === type ? e : { ...e, type, animated: false };
      }),
    );
  }
}

// Bind/unbind an adapter field to a standard source directly (from the adapter dropdown).
function parseSourceValue(v) {
  if (!v) return null;
  if (v === "g") return { type: "groupName" };
  const rest = v.slice(2);
  if (v[0] === "p") return { type: "productField", field: rest };
  if (v[0] === "a") return { type: "attribute", attributeId: Number(rest) };
  if (v[0] === "s")
    return { type: "storeAttribute", attributeId: Number(rest) };
  return null;
}
function setSinkFieldSource(sinkId, fieldKey, value) {
  const adapter = findNode(sinkId);
  if (!adapter) return;
  // "m:<primaryKey>" merges this field into a duplicate: it exports the primary's
  // value under its own key and is skipped on import. Any other selection un-merges.
  const mergeWith = typeof value === "string" && value.startsWith("m:") ? value.slice(2) : null;
  if (mergeWith === fieldKey) return;
  const src = mergeWith ? null : parseSourceValue(value);
  const fields = (adapter.data?.fields || []).map((f) => {
    if (f.fieldKey !== fieldKey) return f;
    const nf = { ...f };
    if (mergeWith) {
      nf.aliasOf = mergeWith;
      delete nf.source;
    } else {
      delete nf.aliasOf;
      if (src) nf.source = src;
      else delete nf.source;
    }
    return nf;
  });
  updateNodeData(sinkId, { fields });
  if (src || mergeWith) {
    const dead = edges.value.filter(
      (e) => e.target === sinkId && e.targetHandle === `f:${fieldKey}`,
    );
    if (dead.length) removeEdges(dead.map((e) => e.id));
  }
  dirty.value = true;
  refreshAnalysis();
  schedulePreview();
}

function setFieldSource(fieldKey, value) {
  setSinkFieldSource(ADAPTER_NODE_ID, fieldKey, value);
}

function setPriceFieldSource(fieldKey, value) {
  setSinkFieldSource(ADAPTER_PRICE_NODE_ID, fieldKey, value);
}

// ---- Option resolution for valueMap/optionMap editors ----

const attrOptionsCache = ref({});
const loadingOptions = ref({});

const isMapModifier = (n) =>
  n?.type === "modifier" &&
  (n.data?.modifierType === "valueMap" || n.data?.modifierType === "optionMap");

function attrIdEq(a, b) {
  if (a == null || b == null) return false;
  return Number(a) === Number(b);
}

function normalizeOptionList(raw) {
  const list = parseAttributeOptions(raw);
  return list.map((o) => ({
    value: String(o.value ?? o.Value ?? ""),
    label: String(o.label ?? o.Label ?? o.value ?? o.Value ?? ""),
  }));
}

// Walk upstream along the main value wire ("in") to the originating attribute/static options.
function traceBackAttr(startId) {
  const byId = new Map(nodes.value.map((n) => [n.id, n]));
  const incoming = new Map();
  edges.value.forEach((e) => {
    const h = e.targetHandle || "in";
    if (h !== "in") return;
    const a = incoming.get(e.target) || [];
    a.push(e);
    incoming.set(e.target, a);
  });
  const seen = new Set();
  const stack = [startId];
  while (stack.length) {
    const id = stack.pop();
    if (seen.has(id)) continue;
    seen.add(id);
    const n = byId.get(id);
    if (n && id !== startId) {
      if (n.type === "attribute" || n.type === "storeAttribute") {
        return {
          kind: n.type === "attribute" ? "category" : "store",
          id: n.data?.attributeId,
        };
      }
      if (n.type === "static" && n.data?.options?.length) {
        return { kind: "static", options: n.data.options };
      }
    }
    (incoming.get(id) || []).forEach((e) => stack.push(e.source));
  }
  return null;
}

function resolveMapModifierSourceAttr(startId) {
  const wired = traceBackAttr(startId);
  if (wired) return wired;

  const fieldKey = traceDownField(startId);
  if (!fieldKey) return null;

  for (const sinkId of [ADAPTER_NODE_ID, ADAPTER_PRICE_NODE_ID]) {
    const sink = nodes.value.find((n) => n.id === sinkId);
    const field = sink?.data?.fields?.find((f) => f.fieldKey === fieldKey);
    const src = field?.source;
    if (src?.type === "attribute" && src.attributeId != null) {
      return { kind: "category", id: src.attributeId };
    }
    if (src?.type === "storeAttribute" && src.attributeId != null) {
      return { kind: "store", id: src.attributeId };
    }
  }
  return null;
}

function sourceOptionsForAttr(attr) {
  if (!attr) return [];
  if (attr.kind === "static" && attr.options?.length) {
    return normalizeOptionList(attr.options);
  }
  if (!attr.id) return [];
  const cacheKey = `${attr.kind}:${attr.id}`;
  const cached = attrOptionsCache.value[cacheKey];
  if (cached?.length) return normalizeOptionList(cached);
  return normalizeOptionList(resolveSourceOptionsFromAvailable(attr));
}

// Walk downstream to the adapter field this branch feeds (for marketplace options).
function traceDownField(startId) {
  const byId = new Map(nodes.value.map((n) => [n.id, n]));
  const outgoing = new Map();
  edges.value.forEach((e) => {
    const a = outgoing.get(e.source) || [];
    a.push(e);
    outgoing.set(e.source, a);
  });
  const seen = new Set();
  const stack = [startId];
  while (stack.length) {
    const id = stack.pop();
    if (seen.has(id)) continue;
    seen.add(id);
    for (const e of outgoing.get(id) || []) {
      const t = byId.get(e.target);
      if (
        t &&
        (t.type === "adapterFields" || t.type === "adapterPriceFields") &&
        String(e.targetHandle).startsWith("f:")
      ) {
        return String(e.targetHandle).slice(2);
      }
      stack.push(e.target);
    }
  }
  return null;
}

function collectUpstreamNodes(startId) {
  const byId = new Map(nodes.value.map((n) => [n.id, n]));
  const incoming = new Map();
  edges.value.forEach((e) => {
    const a = incoming.get(e.target) || [];
    a.push(e);
    incoming.set(e.target, a);
  });
  const result = [];
  const stack = [startId];
  const seen = new Set();
  while (stack.length) {
    const id = stack.pop();
    if (seen.has(id)) continue;
    seen.add(id);
    const n = byId.get(id);
    if (n) result.push(n);
    (incoming.get(id) || []).forEach((e) => stack.push(e.source));
  }
  return result;
}

function syncMapModifiersForField(conn, fieldKey, fieldDef) {
  if (!fieldDef) return;
  const chain = collectUpstreamNodes(conn.source);
  for (const n of chain) {
    if (n.type !== "modifier") continue;
    if (n.data?.modifierType === "optionMap" && fieldDef.options?.length) {
      updateNodeData(n.id, {
        targetFieldKey: fieldKey,
        targetFieldOptions: fieldDef.options,
        targetValueType: fieldDef.type === "multiselect" ? "array" : "string",
      });
    } else if (n.data?.modifierType === "valueMap") {
      updateNodeData(n.id, { targetFieldKey: fieldKey });
    }
  }
}

function loadSourceOptions(kind, id) {
  const cacheKey = `${kind}:${id}`;
  if (attrOptionsCache.value[cacheKey] || loadingOptions.value[cacheKey])
    return;
  loadingOptions.value[cacheKey] = true;
  store.dispatch("getAttributeOptions", {
    type: kind,
    id,
    onSuccess: (response) => {
      const d = extractResponseData(response, {});
      const parsed = parseAttributeOptions(d.options);
      attrOptionsCache.value = {
        ...attrOptionsCache.value,
        [cacheKey]: parsed,
      };
      loadingOptions.value[cacheKey] = false;
    },
    onError: () => {
      loadingOptions.value[cacheKey] = false;
    },
  });
}

const selectedExternalOptions = computed(() => {
  if (!isMapModifier(selected.value)) return [];
  if (selected.value.data?.targetFieldOptions?.length) {
    return selected.value.data.targetFieldOptions;
  }
  const key = traceDownField(selected.value.id);
  if (!key) return [];
  return (
    props.externalFieldDefs[key]?.options ||
    props.priceFieldDefs[key]?.options ||
    []
  );
});

const adapterFieldsWithOptions = computed(() =>
  Object.entries({ ...props.externalFieldDefs, ...props.priceFieldDefs })
    .filter(([, d]) => d.options?.length > 0)
    .map(([key, d]) => ({
      fieldKey: key,
      label: d.name || key,
      options: d.options,
      valueType: d.type === "multiselect" ? "array" : "string",
    })),
);

function normalizeAiModelOption(m) {
  const id = m?.id ?? m?.Id;
  if (id == null) return null;
  const name = m?.name ?? m?.Name ?? m?.modelId ?? m?.ModelId;
  return { value: id, label: name || `Модель #${id}` };
}

const aiModelOptions = computed(() =>
  (store.state.aiModels?.pickerModels ?? [])
    .map(normalizeAiModelOption)
    .filter(Boolean),
);

function loadPickerModels() {
  store.dispatch("aiModels/getPickerModels", { onError: () => {} });
}

const templateFieldRefs = computed(() => {
  const refs = [{ group: "value", label: "Исходное значение", snippet: "{{value}}" }];
  for (const pf of mergeProductFieldSources(props.availableSources)) {
    const group = (props.availableSources.stockFields || []).some((f) => f.key === pf.key)
      ? "stock"
      : (props.availableSources.priceFields || []).some((f) => f.key === pf.key)
        ? "price"
        : "product";
    refs.push({ group, label: pf.displayName, snippet: `{{${pf.key}}}` });
  }
  for (const ca of props.availableSources.categoryAttributes || [])
    refs.push({ group: "category", label: ca.name, snippet: `{{attr:${ca.id}}}` });
  for (const sa of props.availableSources.storeAttributes || [])
    refs.push({ group: "store", label: sa.name, snippet: `{{storeAttr:${sa.id}}}` });
  if (selected.value?.type === "modifier" && selected.value?.data?.modifierType === "llmGenerate") {
    for (const pin of selected.value.data.fieldPins || []) {
      if (!pin?.id) continue;
      refs.push({
        group: "pin",
        label: pin.label || pin.id,
        snippet: `{{pin:${pin.id}}}`,
      });
    }
  }
  return refs;
});

function parseAttributeOptions(raw) {
  if (!raw) return [];
  if (Array.isArray(raw)) return raw;
  try {
    return JSON.parse(raw);
  } catch {
    return [];
  }
}

function resolveSourceOptionsFromAvailable(attr) {
  if (!attr?.id) return [];
  if (attr.kind === "category") {
    const ca = props.availableSources.categoryAttributes?.find((a) =>
      attrIdEq(a.id ?? a.Id, attr.id),
    );
    if (ca?.options) return parseAttributeOptions(ca.options);
  } else if (attr.kind === "store") {
    const sa = props.availableSources.storeAttributes?.find((a) =>
      attrIdEq(a.id ?? a.Id, attr.id),
    );
    if (sa?.options) return parseAttributeOptions(sa.options);
  }
  return [];
}

const selectedSourceOptions = computed(() => {
  if (!isMapModifier(selected.value)) return [];
  return sourceOptionsForAttr(resolveMapModifierSourceAttr(selected.value.id));
});

function preloadMapModifierOptions() {
  for (const n of nodes.value) {
    if (!isMapModifier(n)) continue;
    const attr = resolveMapModifierSourceAttr(n.id);
    if (!attr?.id || attr.kind === "static") continue;
    const cacheKey = `${attr.kind}:${attr.id}`;
    const fromSources = resolveSourceOptionsFromAvailable(attr);
    if (fromSources.length && !attrOptionsCache.value[cacheKey]?.length) {
      attrOptionsCache.value = { ...attrOptionsCache.value, [cacheKey]: fromSources };
    }
    loadSourceOptions(attr.kind, attr.id);
  }
}

watch(selected, (n) => {
  if (!isMapModifier(n)) return;
  const attr = resolveMapModifierSourceAttr(n.id);
  if (attr?.id && attr.kind !== "static") loadSourceOptions(attr.kind, attr.id);
});

watch(
  () => [nodes.value.length, edges.value.length],
  () => preloadMapModifierOptions(),
  { immediate: true },
);

onConnect((conn) => {
  connectionMade.value = true;
  const handle = conn.targetHandle || "in";
  const dupes = edges.value.filter(
    (e) => e.target === conn.target && (e.targetHandle || "in") === handle,
  );
  if (dupes.length) removeEdges(dupes.map((e) => e.id));
  addEdges([{ ...conn, id: genId("e") }]);

  // When a "Стандартное значение" node connects to an adapter field that has predefined
  // options, auto-populate the node's options and valueType from the field definition.
  if (
    (conn.target === ADAPTER_NODE_ID || conn.target === ADAPTER_PRICE_NODE_ID) &&
    conn.targetHandle?.startsWith("f:")
  ) {
    const fieldKey = conn.targetHandle.slice(2);
    const src = nodes.value.find((n) => n.id === conn.source);
    const def =
      conn.target === ADAPTER_PRICE_NODE_ID
        ? props.priceFieldDefs[fieldKey]
        : props.externalFieldDefs[fieldKey];

    syncMapModifiersForField(conn, fieldKey, def);

    if (src?.type === "static" && src.data?.isAdapterValue) {
      if (def?.options?.length) {
        const newValueType = def.type === "multiselect" ? "array" : "string";
        const sameType = src.data.valueType === newValueType;
        updateNodeData(src.id, {
          options: def.options,
          valueType: newValueType,
          ...(sameType ? {} : { staticValue: "", label: "" }),
        });
      }
    }
  }

  dirty.value = true;
  nextTick(() => preloadMapModifierOptions());
  schedulePreview();
});

// Drag a wire from a pin: remember it so a drop on empty space can offer matching blocks.
// From an output pin ⇒ offer blocks that ACCEPT its type and wire into them.
// From an input pin  ⇒ offer blocks that PRODUCE its expected type and wire out of them.
onConnectStart((params) => {
  const p = params || {};
  if (!p.nodeId) return;
  const node = findNode(p.nodeId);
  if (!node) return;
  const dir = p.handleType === "target" ? "target" : "source";
  pendingConnect.value = {
    nodeId: p.nodeId,
    handleId: p.handleId,
    dir,
    type:
      dir === "target"
        ? nodeInputType(node, p.handleId)
        : nodeOutputType(node, nodes.value, edges.value),
  };
  connectionMade.value = false;
});

// Dropped on empty canvas (no successful connection) ⇒ open the filtered node menu at the cursor.
onConnectEnd((e) => {
  if (connectionMade.value || !pendingConnect.value) {
    pendingConnect.value = null;
    return;
  }
  const pt = e?.changedTouches?.[0] || e;
  const x = pt?.clientX;
  const y = pt?.clientY;
  const rect = flowWrap.value?.getBoundingClientRect();
  if (
    x == null ||
    y == null ||
    !rect ||
    x < rect.left ||
    x > rect.right ||
    y < rect.top ||
    y > rect.bottom
  ) {
    pendingConnect.value = null;
    return;
  }
  openConnectMenuAt(x, y, pendingConnect.value.type, pendingConnect.value.dir);
});

onNodeClick(({ node }) => {
  selected.value = node;
  closeMenu();
});
onPaneClick(() => {
  selected.value = null;
  closeMenu();
});

function onBeforeUnload(e) {
  if (!dirty.value) return;
  e.preventDefault();
  e.returnValue = "";
}

vf.onPaneReady(() => {
  flowPaneReady.value = true;
  if (pendingLoadOptions !== null) {
    const opts = pendingLoadOptions;
    pendingLoadOptions = null;
    load(opts);
  }
});

onMounted(() => {
  window.addEventListener("keydown", onKeydown);
  window.addEventListener("beforeunload", onBeforeUnload);
  // Capture phase: fires even while Vue Flow captures pointer events on the pane.
  document.addEventListener("pointerdown", onPointerDown, true);
  document.addEventListener("pointermove", onPointerMove, true);
  // Capture + non-passive so we can preventDefault for trackpad horizontal pan.
  flowWrap.value?.addEventListener("wheel", onWheelGesture, { capture: true, passive: false });
  if (vf.viewport?.value?.viewportInitialized) {
    flowPaneReady.value = true;
  }
  loadPickerModels();
  requestLoad();
});

watch(
  () => selected.value?.data?.modifierType,
  (type) => {
    if (type === "llmGenerate") loadPickerModels();
  },
);
onBeforeUnmount(() => {
  window.removeEventListener("keydown", onKeydown);
  window.removeEventListener("beforeunload", onBeforeUnload);
  document.removeEventListener("pointerdown", onPointerDown, true);
  document.removeEventListener("pointermove", onPointerMove, true);
  flowWrap.value?.removeEventListener("wheel", onWheelGesture, { capture: true });
});
watch(() => props.mappingId, () => requestLoad());
watch(
  () => props.previewProductId,
  (id) => (id ? schedulePreview() : clearNodePreview()),
);
// Sources can arrive after the initial load — re-push them to the adapter's dropdown when they do.
watch(
  () => props.availableSources,
  () => {
    if (loading.value) return;
    refreshAnalysis();
    preloadMapModifierOptions();
  },
  { deep: true },
);
// Field defs (name/description/unit) can arrive after graph load — enrich once, not on every analysis pass.
watch(
  () => [props.externalFieldDefs, props.priceFieldDefs],
  () => {
    if (loading.value) return;
    applyFieldDefEnrichment();
  },
  { deep: true },
);
// Price-API field list can arrive after graph load — append missing fields to the price sink.
watch(
  () => props.priceFieldMappings,
  (mappings) => {
    if (loading.value || !findNode(ADAPTER_PRICE_NODE_ID)) return;
    const cur = findNode(ADAPTER_PRICE_NODE_ID)?.data?.fields || [];
    const curKeys = cur.map((f) => f.fieldKey).join("\0");
    const copy = nodes.value.map((n) => ({
      ...n,
      data: n.data?.fields
        ? { ...n.data, fields: n.data.fields.map((f) => ({ ...f })) }
        : { ...n.data },
    }));
    syncAdapterPriceFields(copy, mappings);
    const next = copy.find((n) => n.id === ADAPTER_PRICE_NODE_ID)?.data?.fields || [];
    const nextKeys = next.map((f) => f.fieldKey).join("\0");
    if (nextKeys !== curKeys) {
      updateNodeData(ADAPTER_PRICE_NODE_ID, {
        fields: enrichFieldsWithDefs(next, props.priceFieldDefs),
      });
      dirty.value = true;
    }
    refreshAnalysis();
  },
  { deep: true },
);
// Only react to structural changes (add/remove — e.g. the Delete key), not position/select/data,
// to avoid a feedback loop with preview's own updateNodeData() calls.
onNodesChange((changes) => {
  const cs = changes || [];
  const structural = cs.some((c) => c.type === "remove" || c.type === "add");
  if (structural) {
    refreshAnalysis();
    schedulePreview();
  }
  // Mark dirty on structural changes or on drag-end (position change, dragging===false).
  if (!loading.value && (structural || cs.some((c) => c.type === "position" && !c.dragging))) {
    dirty.value = true;
    scheduleHistory();
  }
});
onEdgesChange((changes) => {
  const cs = changes || [];
  const structural = cs.some((c) => c.type === "remove" || c.type === "add");
  if (structural) {
    refreshAnalysis();
    schedulePreview();
    if (!loading.value) {
      dirty.value = true;
      scheduleHistory();
    }
  }
});

// Double-click a wire ⇒ drop a draggable reroute node on it (split the edge in two through it).
onEdgeDoubleClick(({ event, edge }) => {
  const pt = event?.changedTouches?.[0] || event;
  const p = toFlow({ x: pt?.clientX ?? 0, y: pt?.clientY ?? 0 });
  const rr = createRerouteNode({ x: p.x - 7, y: p.y - 7 }); // centre the 14px dot on the cursor
  addNodes([rr]);
  addEdges([
    {
      id: genId("e"),
      source: edge.source,
      sourceHandle: edge.sourceHandle,
      target: rr.id,
      targetHandle: "in",
    },
    {
      id: genId("e"),
      source: rr.id,
      sourceHandle: "out",
      target: edge.target,
      targetHandle: edge.targetHandle,
    },
  ]);
  removeEdges([edge.id]);
  schedulePreview();
});

// Double-click: a reroute node splices out; the adapter sink is left alone; any other block toggles
// its collapsed "pill" form.
onNodeDoubleClick(({ node }) => {
  if (!node) return;
  if (node.type === "adapterFields" || node.type === "adapterPriceFields") return;
  if (node.type !== "reroute") {
    // Always toggle against the live store node so inspector edits (staticValue/params)
    // are not clobbered by a stale event snapshot.
    const live = findNode(node.id) || node;
    const nextCollapsed = !live.data?.collapsed;
    updateNodeData(live.id, { collapsed: nextCollapsed });
    if (selected.value?.id === live.id) {
      const after = findNode(live.id);
      if (after) {
        selected.value = {
          ...after,
          data: { ...(after.data || {}), collapsed: nextCollapsed },
        };
      }
    }
    nextTick(() => updateNodeInternals([live.id]));
    if (!loading.value) {
      dirty.value = true;
      scheduleHistory();
    }
    return;
  }
  const src = edges.value.find((e) => e.target === node.id);
  const outs = edges.value.filter((e) => e.source === node.id);
  if (src) {
    addEdges(
      outs.map((out) => ({
        id: genId("e"),
        source: src.source,
        sourceHandle: src.sourceHandle,
        target: out.target,
        targetHandle: out.targetHandle,
      })),
    );
  }
  if (selected.value?.id === node.id) selected.value = null;
  removeNodes([node.id]); // also removes the reroute's own edges
  schedulePreview();
});

async function load(options = {}) {
  const gen = ++loadGeneration;
  const preserveLayout = options?.preserveLayout === true;
  const stepCtx = { gen, preserveLayout };
  if (!props.connectionId || !props.mappingId) return;
  if (!flowPaneReady.value) {
    pendingLoadOptions = options;
    return;
  }
  if (previewTimer) clearTimeout(previewTimer);
  loading.value = true;
  try {
    const data = await loadStep("load:api:getMappingGraph", () =>
      store.dispatch("getMappingGraph", {
        connectionId: props.connectionId,
        mappingId: props.mappingId,
      }),
    stepCtx,
    );

    let graph;
    let migrated = false;
    if (
      data?.hasGraph &&
      data.graph &&
      Array.isArray(data.graph.nodes) &&
      data.graph.nodes.length
    ) {
      graph = data.graph;
    } else {
      graph = autoMigrate(props.fieldMappings, props.availableSources);
      migrated = true;
    }

    await loadStep("load:prepareGraph", async () => {
      const ensured = ensurePriceNode(
        graph.nodes,
        graph.edges,
        props.priceFieldMappings,
      );
      graph.nodes = syncAdapterFields(
        syncAdapterPriceFields(ensured.nodes, props.priceFieldMappings),
        props.fieldMappings,
      );
      graph.edges = ensured.edges;
      graph.nodes = syncAdapterPriceFields(graph.nodes, props.priceFieldMappings);
      ensureModifierParamDefaults(graph.nodes);
    }, { ...stepCtx, migrated });

    const { nodes: safeNodes } = sanitizeGraphNodes(graph.nodes);

    const rawNodeList = graph.nodes || [];
    const noneHavePosition =
      rawNodeList.length > 0 && rawNodeList.every((n) => !n?.position);
    const needsLayout = !preserveLayout && (migrated || noneHavePosition);
    const nodeIds = new Set(safeNodes.map((n) => n.id));
    const { edges: safeEdges } = sanitizeGraphEdges(graph.edges, nodeIds);

    const nodesToSet = needsLayout
      ? layoutGraph(safeNodes, safeEdges)
      : safeNodes;

    await loadStep("load:setNodes", async () => {
      setNodes(nodesToSet);
      await nextTick();
    }, { ...stepCtx, needsLayout, nodeCount: nodesToSet.length });

    await loadStep("load:setEdges", async () => {
      setEdges(safeEdges);
      await nextTick();
    }, { ...stepCtx, edgeCount: safeEdges.length });

    if (needsLayout) {
      await loadStep("load:relayoutMeasured", async () => {
        const measured = collectMeasuredHeights(nodes.value);
        if (measured.size > 0) {
          const obj = toObject();
          setNodes(layoutGraph(obj.nodes, obj.edges, measured));
          await nextTick();
        }
      }, stepCtx);
    }

    await loadStep("load:finalizeNodes", async () => {
      const adNode = findNode(ADAPTER_NODE_ID);
      if (adNode) adNode.deletable = false;
      const priceNode = findNode(ADAPTER_PRICE_NODE_ID);
      if (priceNode) priceNode.deletable = false;
      pruneSourceBoundEdges();
      await nextTick();
    }, stepCtx);

    await loadStep("load:refreshAnalysis", async () => {
      applyFieldDefEnrichment();
      refreshAnalysis({ updateLanes: false });
      await waitForPaint();
    }, stepCtx);

    await loadStep("load:applyEdgeLaneTypes", async () => {
      applyEdgeLaneTypes();
      await nextTick();
    }, stepCtx);

    if (!preserveLayout && flowWrap.value?.clientHeight > 0) {
      await loadStep("load:fitView", async () => {
        fitView({ padding: 0.2 });
        await nextTick();
      }, stepCtx);
    }

    dirty.value = false;
    await loadStep("load:historySnapshot", async () => {
      history.value = [makeSnapshot()];
      histIdx.value = 0;
    }, stepCtx);

  } catch {
    // loadStep already reported the error
  } finally {
    loading.value = false;
    schedulePreview();
  }
}

async function syncAdapterFieldSchema() {
  if (!props.connectionId || !props.mappingId || syncingFields.value) return;
  syncingFields.value = true;
  try {
    const result = await store.dispatch("syncMappingFields", {
      connectionId: props.connectionId,
      mappingId: props.mappingId,
    });
    const message =
      result?.message ||
      (result?.totalAdded > 0
        ? `Добавлено полей: ${result.totalAdded}`
        : "Схема актуальна — новых полей нет");
    displaySonnerSuccess(message);
    emit("fields-synced");
  } catch (e) {
    displaySonnerError(e);
  } finally {
    syncingFields.value = false;
  }
}

defineExpose({
  reload: requestLoad,
  reloadAfterFieldSync: () => requestLoad({ preserveLayout: true }),
  autoArrange,
  syncAdapterFieldSchema,
  exportMappingFile,
  triggerImportFile,
});

// ---- Context menu (ПКМ / Tab) ----

function onPointerDown(e) {
  if (e.button === 2) {
    rightDown.value = { x: e.clientX, y: e.clientY };
    rightMoved.value = false;
  }
}
function onPointerMove(e) {
  lastMouse.value = { x: e.clientX, y: e.clientY };
  if (e.buttons & 2 && rightDown.value) {
    const dx = e.clientX - rightDown.value.x;
    const dy = e.clientY - rightDown.value.y;
    if (dx * dx + dy * dy > 16) rightMoved.value = true; // >4px ⇒ right-drag (pan)
  }
}

function openMenuAt(clientX, clientY) {
  pendingConnect.value = null; // plain ПКМ/Tab — not a wire drop
  const rect = flowWrap.value?.getBoundingClientRect();
  menu.value = {
    open: true,
    clientX,
    clientY,
    left: clientX - (rect?.left || 0),
    top: clientY - (rect?.top || 0),
    connectType: null,
    connectDir: null,
  };
}
function openConnectMenuAt(clientX, clientY, type, dir) {
  const rect = flowWrap.value?.getBoundingClientRect();
  menu.value = {
    open: true,
    clientX,
    clientY,
    left: clientX - (rect?.left || 0),
    top: clientY - (rect?.top || 0),
    connectType: type || null,
    connectDir: dir || null,
  };
}
function closeMenu() {
  menu.value.open = false;
  pendingConnect.value = null;
}
function toggleMaximize() {
  maximized.value = !maximized.value;
  nextTick(() => fitView({ padding: 0.2 }));
}
function onContextMenu(e) {
  e.preventDefault();
  // Right-drag pans; only a right *click* (no movement) opens the menu.
  if (rightMoved.value) {
    rightMoved.value = false;
    return;
  }
  openMenuAt(e.clientX, e.clientY);
}

// Prevent self-connections and output→output (or input→input) connections.
// All output handles in this graph are named "out"; all input handles have other names.
function isValidConnection(conn) {
  if (conn.source === conn.target) return false;
  if (conn.targetHandle === "out") return false; // output→output
  return true;
}

// Trackpad two-finger pan: intercept wheel events with a horizontal component
// and convert them to viewport pan, leaving pure vertical scroll for VueFlow's zoom.
function onWheelGesture(e) {
  if (e.ctrlKey) return; // pinch or Ctrl+scroll → VueFlow handles zoom
  if (Math.abs(e.deltaX) <= 1) return; // pure vertical (mouse scroll) → VueFlow handles zoom
  e.preventDefault();
  e.stopPropagation();
  const vp = vf.viewport.value;
  vf.setViewport({ x: vp.x - e.deltaX, y: vp.y - e.deltaY, zoom: vp.zoom });
}
function onKeydown(e) {
  const t = e.target;
  if (
    t &&
    (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.isContentEditable)
  )
    return; // don't hijack typing in the inspector / menu search

  // Clipboard shortcuts (Ctrl/Cmd + C / V / D). Use e.code (physical key) so they work
  // regardless of the active keyboard layout — with a Cyrillic layout e.key would be "с"/"м"/"в".
  if (e.ctrlKey || e.metaKey) {
    if (e.code === "KeyZ") {
      e.preventDefault();
      if (e.shiftKey) { if (canRedo.value) restoreAt(histIdx.value + 1); }
      else            { if (canUndo.value) restoreAt(histIdx.value - 1); }
    } else if (e.code === "KeyY") {
      e.preventDefault();
      if (canRedo.value) restoreAt(histIdx.value + 1);
    } else if (e.code === "KeyS") {
      e.preventDefault();
      save();
    } else if (e.code === "KeyC") {
      if (copySelection()) e.preventDefault();
    } else if (e.code === "KeyV") {
      if (pasteClipboard()) e.preventDefault();
    } else if (e.code === "KeyD") {
      e.preventDefault();
      duplicateSelection();
    }
    return;
  }

  if (e.shiftKey && e.code === "Tab") {
    e.preventDefault();
    debugMode.value = !debugMode.value;
    return;
  }

  if (e.code === "Period") {
    const rect = flowWrap.value?.getBoundingClientRect();
    if (!rect) return;
    const { x, y } = lastMouse.value;
    if (x < rect.left || x > rect.right || y < rect.top || y > rect.bottom) return;
    e.preventDefault();
    const p = toFlow({ x, y });
    const rr = createRerouteNode({ x: p.x - 7, y: p.y - 7 });
    addNodes([rr]);
    dirty.value = true;
    return;
  }

  if (e.code !== "Tab" && e.code !== "KeyF") return;
  const rect = flowWrap.value?.getBoundingClientRect();
  if (!rect) return;
  const { x, y } = lastMouse.value;
  if (x < rect.left || x > rect.right || y < rect.top || y > rect.bottom)
    return; // only over canvas
  e.preventDefault();
  if (e.code === "Tab") openMenuAt(x, y);
  else toggleMaximize();
}

function toFlow(client) {
  if (vf.screenToFlowCoordinate) return vf.screenToFlowCoordinate(client);
  if (vf.project) return vf.project(client);
  return client;
}

function onMenuAdd(desc) {
  const pos = toFlow({ x: menu.value.clientX, y: menu.value.clientY });
  let node = null;
  if (desc.group === "source")
    node = createSourceNode(desc.kind, pos, desc.payload || {});
  else if (desc.group === "modifier")
    node = createModifierNode(desc.modifierType, pos);
  else if (desc.group === "op") node = createOpNode(desc.opType, pos);
  if (node) {
    addNodes([node]);
    // If this menu was opened by dropping a wire on empty space, auto-wire it to the new block.
    const pc = pendingConnect.value;
    if (pc) {
      const edge =
        pc.dir === "target"
          ? {
              // Dragged from an input pin ⇒ new block's output feeds that input.
              source: node.id,
              sourceHandle: "out",
              target: pc.nodeId,
              targetHandle: pc.handleId,
            }
          : {
              // Dragged from an output pin ⇒ new block's input receives that output.
              source: pc.nodeId,
              sourceHandle: pc.handleId,
              target: node.id,
              targetHandle: firstInputHandle(node),
            };
      addEdges([{ id: genId("e"), ...edge }]);
    }
    selected.value = node;
    schedulePreview();
  }
  pendingConnect.value = null;
}

// ---- Inspector ----

function onInspectorUpdate(patch) {
  if (!selected.value) return;
  // Merge against the live Vue Flow node so detached selected snapshots can't drop
  // previously saved params/staticValue when patching (e.g. after collapse).
  const live = findNode(selected.value.id);
  const base = { ...(live?.data || selected.value.data || {}) };
  const next = { ...base, ...patch };
  if (patch.params && typeof patch.params === "object") {
    next.params = { ...(base.params || {}), ...patch.params };
  }
  updateNodeData(selected.value.id, next);
  selected.value = {
    ...(live || selected.value),
    data: next,
  };
  if ("importable" in patch) nextTick(refreshAnalysis);
  dirty.value = true;
  schedulePreview();
}

function openAdapterField(fieldKey) {
  selected.value = findNode(ADAPTER_NODE_ID) ?? selected.value;
  nextTick(() => { focusAdapterField.value = fieldKey; });
}

function openPriceField(fieldKey) {
  selected.value = findNode(ADAPTER_PRICE_NODE_ID) ?? selected.value;
  nextTick(() => { focusAdapterField.value = fieldKey; });
}
function onInspectorDelete() {
  if (!selected.value || selected.value.deletable === false) return;
  removeNodes([selected.value.id]);
  selected.value = null;
  schedulePreview();
}

// ---- Save / preview ----

function buildGraph() {
  const obj = toObject();
  return {
    nodes: (obj.nodes || []).map((n) => ({
      id: n.id,
      type: n.type,
      position: n.position,
      deletable: n.deletable,
      data: cleanData(n.data),
    })),
    edges: (obj.edges || []).map((e) => ({
      id: e.id,
      source: e.source,
      target: e.target,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle,
    })),
  };
}
function cleanData(d) {
  const rest = { ...(d || {}) };
  delete rest.preview;
  delete rest.previewValue;
  delete rest.analysis;
  delete rest.fieldOptionsMap; // adapter: transient, rebuilt from props on every refresh
  delete rest.sources; // adapter: transient traced-source labels
  delete rest.availableSources; // adapter: transient dropdown options
  delete rest.setFieldSource; // adapter: transient callback
  delete rest.openField; // adapter: transient callback
  return rest;
}

// ---- Clipboard: copy / paste / duplicate selected nodes ----

const clipboard = ref({ nodes: [], edges: [] });

// Selected, copyable nodes — prefer Vue Flow's selection, fall back to the inspector's node.
function currentSelection() {
  const fromVf = (getSelectedNodes?.value || []).filter(
    (n) => n.deletable !== false,
  );
  if (fromVf.length) return fromVf;
  if (selected.value && selected.value.deletable !== false) {
    return [findNode(selected.value.id) || selected.value];
  }
  return [];
}

// Snapshot the current selection (excluding the non-deletable adapter sink) + its internal edges.
function copySelection() {
  const sel = currentSelection();
  if (!sel.length) return false;
  const ids = new Set(sel.map((n) => n.id));
  clipboard.value = {
    nodes: sel.map((n) => ({
      id: n.id,
      type: n.type,
      position: { ...n.position },
      data: JSON.parse(JSON.stringify(cleanData(n.data))),
    })),
    edges: edges.value
      .filter((e) => ids.has(e.source) && ids.has(e.target))
      .map((e) => ({
        source: e.source,
        target: e.target,
        sourceHandle: e.sourceHandle,
        targetHandle: e.targetHandle,
      })),
  };
  return true;
}

// Materialize a clipboard snapshot as fresh nodes/edges (new ids), offset and selected.
function spawnCopies(clip, offset) {
  if (!clip.nodes.length) return false;
  const idMap = new Map();
  const newNodes = clip.nodes.map((n) => {
    const id = genId(n.type);
    idMap.set(n.id, id);
    return {
      id,
      type: n.type,
      position: { x: n.position.x + offset.x, y: n.position.y + offset.y },
      data: JSON.parse(JSON.stringify(n.data)),
      selected: true,
    };
  });
  const newEdges = clip.edges.map((e) => ({
    id: genId("e"),
    source: idMap.get(e.source),
    target: idMap.get(e.target),
    sourceHandle: e.sourceHandle,
    targetHandle: e.targetHandle,
  }));
  const prevSelected = getSelectedNodes?.value
    ? [...getSelectedNodes.value]
    : [];
  addNodes(newNodes);
  if (newEdges.length) addEdges(newEdges);
  // Move selection to the freshly created copies.
  const created = newNodes.map((n) => findNode(n.id)).filter(Boolean);
  if (prevSelected.length && typeof removeSelectedNodes === "function")
    removeSelectedNodes(prevSelected);
  if (created.length && typeof addSelectedNodes === "function")
    addSelectedNodes(created);
  selected.value = created.length === 1 ? created[0] : null;
  schedulePreview();
  return true;
}

function pasteClipboard() {
  return spawnCopies(clipboard.value, { x: 40, y: 40 });
}

function duplicateSelection() {
  if (copySelection()) spawnCopies(clipboard.value, { x: 40, y: 40 });
}

async function save() {
  saving.value = true;
  try {
    const { projections } = analyzeFields(nodes.value, edges.value);
    await store.dispatch("saveMappingGraph", {
      connectionId: props.connectionId,
      mappingId: props.mappingId,
      graph: buildGraph(),
      fields: projections,
    });
    dirty.value = false;
    displaySonnerSuccess("Граф сохранён");
  } catch (e) {
    displaySonnerError(e);
  } finally {
    saving.value = false;
  }
}

let previewTimer = null;
let previewToken = 0;
function schedulePreview() {
  if (previewTimer) clearTimeout(previewTimer);
  if (!props.previewProductId || loading.value) return;
  previewTimer = setTimeout(runPreview, 400);
}

function applyPreviewValues(outputs, nodeValues) {
  if (findNode(ADAPTER_NODE_ID)) {
    updateNodeData(ADAPTER_NODE_ID, { preview: outputs });
  }
  if (findNode(ADAPTER_PRICE_NODE_ID)) {
    updateNodeData(ADAPTER_PRICE_NODE_ID, { preview: outputs });
  }
  for (const n of nodes.value) {
    if (n.type === "adapterFields" || n.type === "adapterPriceFields") continue;
    updateNodeData(n.id, { previewValue: nodeValues[n.id] ?? null });
  }
}

// Clearing the preview product also clears the per-node preview values from the blocks.
function clearNodePreview() {
  if (previewTimer) clearTimeout(previewTimer);
  if (findNode(ADAPTER_NODE_ID)) {
    updateNodeData(ADAPTER_NODE_ID, { preview: {} });
  }
  if (findNode(ADAPTER_PRICE_NODE_ID)) {
    updateNodeData(ADAPTER_PRICE_NODE_ID, { preview: {} });
  }
  for (const n of nodes.value) {
    if (n.type === "adapterFields" || n.type === "adapterPriceFields") continue;
    updateNodeData(n.id, { previewValue: null });
  }
}

// Runs automatically (debounced) on graph changes when a preview product is selected.
async function runPreview() {
  if (!props.previewProductId || !props.connectionId || !props.mappingId)
    return;
  if (loading.value) return;
  await nextTick();
  const gen = ++previewGeneration;
  const token = ++previewToken;
  previewing.value = true;
  try {
    const data = await store.dispatch("previewMappingGraph", {
      connectionId: props.connectionId,
      mappingId: props.mappingId,
      productId: props.previewProductId,
      graph: buildGraph(),
    });
    if (token !== previewToken || loading.value) return;
    await waitForPaint();
    const outputs = data?.outputs || {};
    const nodeValues = data?.nodeValues || {};
    applyPreviewValues(outputs, nodeValues);
    emit("preview-result", { outputs, fieldTypes: data?.fieldTypes || {} });
  } catch (e) {
    if (token !== previewToken) return;
    displaySonnerError(e);
  } finally {
    if (token === previewToken) previewing.value = false;
  }
}

function collectMeasuredHeights(nodeList) {
  const map = new Map();
  for (const n of nodeList || []) {
    const h = n.dimensions?.height ?? n.measured?.height;
    if (typeof h === "number" && h > 0) map.set(n.id, h);
  }
  return map;
}

// Re-run the layered auto-layout on the current graph.
function autoArrange() {
  const obj = toObject();
  const measured = collectMeasuredHeights(nodes.value);
  setNodes(layoutGraph(obj.nodes, obj.edges, measured));
  nextTick(() => fitView({ padding: 0.2 }));
}

const importFileInput = ref(null);

function exportMappingFile() {
  const payload = {
    version: 1,
    exportedAt: new Date().toISOString(),
    mappingId: props.mappingId,
    connectionId: props.connectionId,
    graph: buildGraph(),
  };
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `mapping-${props.mappingId}-${new Date().toISOString().slice(0, 10)}.json`;
  a.click();
  URL.revokeObjectURL(url);
  displaySonnerSuccess("Маппинг экспортирован");
}

function triggerImportFile() {
  importFileInput.value?.click();
}

async function importMappingFile(event) {
  const file = event.target?.files?.[0];
  if (event.target) event.target.value = "";
  if (!file) return;
  try {
    const data = JSON.parse(await file.text());
    const graph = data.graph ?? data;
    if (!graph?.nodes || !Array.isArray(graph.nodes))
      throw new Error("Неверный формат: ожидается объект с nodes и edges");

    const ensured = ensurePriceNode(
      graph.nodes,
      graph.edges || [],
      props.priceFieldMappings,
    );
    let importedNodes = syncAdapterFields(
      syncAdapterPriceFields(ensured.nodes, props.priceFieldMappings),
      props.fieldMappings,
    );
    importedNodes = syncAdapterPriceFields(importedNodes, props.priceFieldMappings);
    const { nodes: importSafeNodes } = sanitizeGraphNodes(importedNodes);
    const importIds = new Set(importSafeNodes.map((n) => n.id));
    const { edges: importSafeEdges } = sanitizeGraphEdges(ensured.edges, importIds);
    setNodes(layoutGraph(importSafeNodes, importSafeEdges));
    setEdges(importSafeEdges);
    await nextTick();
    const adNode = findNode(ADAPTER_NODE_ID);
    if (adNode) adNode.deletable = false;
    const priceNode = findNode(ADAPTER_PRICE_NODE_ID);
    if (priceNode) priceNode.deletable = false;
    refreshAnalysis({ updateLanes: false });
    await waitForPaint();
    applyEdgeLaneTypes();
    await nextTick();
    fitView({ padding: 0.2 });
    dirty.value = true;
    schedulePreview();
    displaySonnerSuccess("Маппинг импортирован — нажмите «Сохранить»");
  } catch (e) {
    displaySonnerError(e);
  }
}
</script>

<template>
  <SplitterGroup
    direction="horizontal"
    :class="[
      'flex min-h-0 overflow-hidden border bg-background',
      maximized ? 'fixed inset-0 z-50' : 'relative h-full w-full rounded-lg',
    ]"
  >
    <SplitterPanel :min-size="40">
      <div
        ref="flowWrap"
        class="relative min-w-0 h-full w-full"
        @contextmenu="onContextMenu"
      >
      <div
        class="absolute left-2 top-2 z-10 flex items-center gap-1.5 rounded-md border bg-card/95 p-1 shadow-sm backdrop-blur"
      >
        <Button size="sm" :disabled="saving || errorCount > 0" @click="save">
          <Loader2 v-if="saving" class="mr-1.5 size-4 animate-spin" />
          <Save v-else class="mr-1.5 size-4" />
          Сохранить
        </Button>
        <span class="mx-0.5 h-5 w-px bg-border" />
        <SearchableSelect
          class="w-64"
          :options="previewProducts"
          :model-value="previewProductId ? String(previewProductId) : ''"
          @update:modelValue="emit('select-preview', $event)"
          placeholder="Товар для предпросмотра…"
        />
        <span
          v-if="previewing"
          class="inline-flex items-center gap-1 px-1 text-xs text-muted-foreground"
        >
          <Loader2 class="size-3.5 animate-spin" /> предпросмотр…
        </span>
        <Button
          v-if="previewProductId"
          size="sm"
          variant="ghost"
          @click="emit('clear-preview')"
          title="Сбросить товар предпросмотра"
        >
          <EyeOff class="mr-1.5 size-4" />
          Сбросить
        </Button>
        <input
          ref="importFileInput"
          type="file"
          accept="application/json,.json"
          class="hidden"
          @change="importMappingFile"
        />
        <span v-if="errorCount" class="mx-1 h-5 w-px bg-border" />
        <Badge v-if="errorCount" variant="destructive" class="gap-1">
          <AlertTriangle class="size-3" /> {{ errorCount }}
        </Badge>
      </div>

      <div v-if="debugMode" class="vf-debug-panel">
        <div class="vf-debug-section">
          <b>Анализ полей</b>
          <div
            v-for="(v, k) in debugInfo.analysis"
            :key="k"
            :class="['vf-debug-row', v.importable ? 'ok' : 'no']"
          >
            <span class="badge">{{ v.importable ? "IMPORT" : "EXPORT" }}</span>
            {{ k }}: connected={{ v.connected }}, rev={{ v.reversible }}<span v-if="v.fanOut" style="color:#f59e0b;margin-left:4px">⚠ fan-out</span>
          </div>
          <div v-if="!Object.keys(debugInfo.analysis).length" class="text-xs opacity-50">нет полей</div>
        </div>
        <div class="vf-debug-section">
          <b>Рёбра ({{ debugInfo.edges.length }})</b>
          <div
            v-for="e in debugInfo.edges"
            :key="e.id"
            :class="['vf-debug-row', e.type === 'vf-import' ? 'ok' : e.type === 'vf-export' ? 'no' : '']"
          >
            <span class="badge">{{ e.type }}</span>
            <span class="truncate">{{ e.src }} → {{ e.tgt }}</span>
          </div>
        </div>
        <div class="vf-debug-hint">Shift+Tab — скрыть</div>
      </div>

      <VueFlow
        :min-zoom="0.2"
        :max-zoom="1.75"
        :delete-key-code="['Delete', 'Backspace']"
        :selection-key-code="true"
        :pan-on-drag="[1, 2]"
        :only-render-visible-elements="false"
        :elevate-edges-on-select="true"
        :is-valid-connection="isValidConnection"
        class="vf-canvas"
      >
        <Background />
        <MiniMap pannable zoomable node-color="var(--muted-foreground)" mask-color="var(--muted)" />
        <Controls :show-interactive="false">
          <button
            class="vue-flow__controls-button"
            :title="animateWires ? 'Выключить анимацию проводов' : 'Включить анимацию проводов'"
            :style="animateWires ? {} : { opacity: 0.4 }"
            @click="animateWires = !animateWires"
          >
            <Activity class="size-3.5" />
          </button>
          <template #control-fit-view>
            <button
              class="vue-flow__controls-button"
              :title="maximized ? 'Свернуть (F)' : 'На весь экран (F)'"
              @click="toggleMaximize"
            >
              <svg v-if="maximized" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                <path d="M8 3v3a2 2 0 0 1-2 2H3"/>
                <path d="M21 8h-3a2 2 0 0 1-2-2V3"/>
                <path d="M3 16h3a2 2 0 0 1 2 2v3"/>
                <path d="M16 21v-3a2 2 0 0 1 2-2h3"/>
              </svg>
              <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                <path d="M8 3H5a2 2 0 0 0-2 2v3"/>
                <path d="M21 8V5a2 2 0 0 0-2-2h-3"/>
                <path d="M3 16v3a2 2 0 0 0 2 2h3"/>
                <path d="M16 21h3a2 2 0 0 0 2-2v-3"/>
              </svg>
            </button>
          </template>
        </Controls>

        <template #edge-vf-import="p"><ImportableEdge v-bind="p" :debug="debugMode" :animate="animateWires" /></template>
        <template #edge-vf-export="p"><ExportOnlyEdge v-bind="p" :debug="debugMode" :animate="animateWires" /></template>

        <template #node-attribute="p"><SourceNode v-bind="p" /></template>
        <template #node-storeAttribute="p"><SourceNode v-bind="p" /></template>
        <template #node-productField="p"><SourceNode v-bind="p" /></template>
        <template #node-static="p"><SourceNode v-bind="p" /></template>
        <template #node-groupName="p"><SourceNode v-bind="p" /></template>
        <template #node-storeFileUrl="p"><SourceNode v-bind="p" /></template>
        <template #node-storeFileContent="p"
          ><SourceNode v-bind="p"
        /></template>
        <template #node-productFileByName="p"
          ><SourceNode v-bind="p"
        /></template>
        <template #node-productFileContentByName="p"
          ><SourceNode v-bind="p"
        /></template>
        <template #node-productFilesByExtension="p"
          ><SourceNode v-bind="p"
        /></template>
        <template #node-modifier="p"><ModifierNode v-bind="p" /></template>
        <template #node-formatString="p"><OpNode v-bind="p" /></template>
        <template #node-join="p"><OpNode v-bind="p" /></template>
        <template #node-makeArray="p"><OpNode v-bind="p" /></template>
        <template #node-arrayAppend="p"><OpNode v-bind="p" /></template>
        <template #node-splitToArray="p"><OpNode v-bind="p" /></template>
        <template #node-arrayIndex="p"><OpNode v-bind="p" /></template>
        <template #node-arrayLength="p"><OpNode v-bind="p" /></template>
        <template #node-ifElse="p"><OpNode v-bind="p" /></template>
        <template #node-not="p"><OpNode v-bind="p" /></template>
        <template #node-and="p"><OpNode v-bind="p" /></template>
        <template #node-or="p"><OpNode v-bind="p" /></template>
        <template #node-equals="p"><OpNode v-bind="p" /></template>
        <template #node-notEquals="p"><OpNode v-bind="p" /></template>
        <template #node-greaterThan="p"><OpNode v-bind="p" /></template>
        <template #node-lessThan="p"><OpNode v-bind="p" /></template>
        <template #node-contains="p"><OpNode v-bind="p" /></template>
        <template #node-isEmpty="p"><OpNode v-bind="p" /></template>
        <template #node-isValid="p"><OpNode v-bind="p" /></template>
        <template #node-adapterFields="p"
          ><AdapterFieldsNode v-bind="p"
        /></template>
        <template #node-adapterPriceFields="p"
          ><AdapterPriceFieldsNode v-bind="p"
        /></template>
        <template #node-reroute="p"><RerouteNode v-bind="p" /></template>
      </VueFlow>

      <!-- context menu + click-away catcher -->
      <div
        v-if="menu.open"
        class="absolute inset-0 z-20"
        @click="closeMenu"
        @contextmenu.prevent="closeMenu"
      />
      <div
        v-if="menu.open"
        class="absolute z-30"
        :style="{ left: menu.left + 'px', top: menu.top + 'px' }"
      >
        <NodeContextMenu
          :available-sources="availableSources"
          :filter-type="menu.connectType"
          :connect-dir="menu.connectDir"
          @add="onMenuAdd"
          @close="closeMenu"
        />
      </div>

      <div
        v-if="loading"
        class="absolute inset-0 z-40 flex items-center justify-center bg-background/60"
      >
        <Loader2 class="size-6 animate-spin text-muted-foreground" />
      </div>
      </div>
    </SplitterPanel>

    <template v-if="selected">
      <SplitterResizeHandle class="w-1 bg-border hover:bg-primary/30 transition-colors cursor-col-resize" />
      <SplitterPanel :default-size="25" :min-size="15" :max-size="50">
        <NodeInspector
          :node="selected"
          :external-field-options="selectedExternalOptions"
          :source-field-options="selectedSourceOptions"
          :store-file-options="availableSources.storeFiles || []"
          :adapter-fields-with-options="adapterFieldsWithOptions"
          :ai-model-options="aiModelOptions"
          :available-sources="availableSources"
          :template-field-refs="templateFieldRefs"
          :focus-field-key="focusAdapterField"
          @update="onInspectorUpdate"
          @delete="onInspectorDelete"
          @close="selected = null"
          @field-focused="focusAdapterField = null"
        />
      </SplitterPanel>
    </template>
  </SplitterGroup>
</template>

<!-- Global (non-scoped) styles: node cards render into the Vue Flow tree via slots, so scoped
     styles wouldn't reach them. Colors use the project's OKLCH var() tokens. -->
<style>
.vf-canvas {
  height: 100%;
  width: 100%;
}

/* Let a drag that starts over a wire pan the canvas: disable only the wide invisible interaction
   path; the visible line keeps pointer events so double-click reroute still works. */
.vf-canvas .vue-flow__edge-interaction {
  pointer-events: none;
}

/* `.vf-card.<mod>` (specificity 0,2,0) so these beat the base `.vf-card` rule that appears later. */
.vf-card.vf-card--collapsed {
  min-width: 0;
  max-width: 180px;
  padding: 4px 12px;
  border-radius: 999px;
}
.vf-card--collapsed .vf-card__head {
  gap: 6px;
}
/* Collapsed multi/fixed-input ops keep height so their left input pins still spread out. */
.vf-card--collapsed-multi {
  min-height: 44px;
  display: flex;
  align-items: center;
}

/* Modifiers that expose operand params as override pins: a row per input handle. Metrics must
   match HEADER/ROW in ModifierNode.vue so the left pins line up with their rows. */
.vf-card.vf-card--haspins {
  min-width: 180px;
  padding: 0;
}
.vf-card--haspins .vf-card__head {
  height: 26px;
  padding: 0 10px;
}
.vf-mod__pin-row {
  display: flex;
  align-items: center;
  height: 18px;
  padding: 0 10px 0 14px;
  font-size: 11px;
  color: var(--muted-foreground);
}
.vf-mod__pin-row--value {
  color: var(--foreground);
  font-weight: 500;
}
.vf-mod__body {
  padding: 4px 10px 8px;
}

.vf-card {
  position: relative;
  min-width: 150px;
  max-width: 220px;
  padding: 8px 10px;
  border-radius: 8px;
  border: 1px solid var(--border);
  background: var(--card);
  color: var(--foreground);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.06);
  font-size: 12px;
}
.vf-card--selected {
  border-color: var(--primary);
  box-shadow: 0 0 0 2px var(--primary);
}
.vf-card--error {
  border-color: var(--destructive);
}

.vf-card__head {
  display: flex;
  align-items: center;
  gap: 6px;
}
.vf-card__kind {
  font-size: 10px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.02em;
  color: var(--muted-foreground);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.vf-card__warn {
  flex-shrink: 0;
  margin-left: auto;
  width: 13px;
  height: 13px;
  color: var(--warning, #d97706);
}
.vf-card__title {
  margin-top: 3px;
  font-weight: 600;
  font-size: 13px;
  line-height: 1.2;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.vf-card__title--sm {
  font-weight: 500;
  font-size: 12px;
  color: var(--muted-foreground);
}

.vf-card__pin {
  display: flex;
  align-items: center;
  gap: 5px;
  margin-top: 5px;
}
.vf-card__pin--right {
  justify-content: flex-end;
}
.vf-dot {
  width: 8px;
  height: 8px;
  border-radius: 9999px;
  display: inline-block;
}
.vf-pin-label {
  font-size: 10px;
  color: var(--muted-foreground);
}
.vf-req {
  color: var(--destructive);
  font-weight: 700;
}

.vf-combine__inputs {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
}
.vf-iconbtn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  border-radius: 4px;
  border: 1px solid var(--border);
  color: var(--muted-foreground);
}
.vf-iconbtn:hover {
  background: var(--accent);
}

/* Single "Поля адаптера" sink node — metrics must match HEADER/ROW in AdapterFieldsNode.vue. */
.vf-card--adapter {
  min-width: 300px;
  max-width: 420px;
  padding: 0;
}

.vf-adapter__head {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 10px;
  border-bottom: 1px solid var(--border);
}
.vf-adapter__row {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 24px;
  padding: 0 10px;
  font-size: 12px;
}
.vf-adapter__row:hover {
  background: var(--accent);
}
.vf-adapter__label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.vf-adapter__pill {
  flex-shrink: 0;
  max-width: 110px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 10px;
  font-weight: 500;
  padding: 1px 6px;
  border-radius: 9999px;
  border: 1px solid transparent;
  line-height: 1.5;
  cursor: pointer;
}
.vf-adapter__pill:hover {
  opacity: 0.8;
}
/* Merged-duplicate (alias) field: dashed neutral pill "= <primary>" */
.vf-adapter__pill--alias {
  background: hsl(var(--muted));
  border-color: hsl(var(--border));
  border-style: dashed;
  color: hsl(var(--muted-foreground));
}
.vf-adapter__dir {
  flex-shrink: 0;
  width: 11px;
  height: 11px;
  opacity: 0.45;
  color: var(--muted-foreground);
}
.vf-adapter__dir--import {
  opacity: 0.65;
  color: var(--primary);
}

/* Windows-style marquee selection rectangle */
.vue-flow__selection,
.vue-flow__nodesselection-rect {
  background: rgba(59, 130, 246, 0.12);
  border: 1px solid rgba(59, 130, 246, 0.8);
}

/* Vue Flow handle (pin) sizing */
.vue-flow__handle {
  width: 9px;
  height: 9px;
  border: 2px solid var(--card);
  background: var(--muted-foreground);
}

/* Default (static / floating) edges — plain wire, no animation. */
.vf-canvas .vue-flow__edge-path {
  stroke: #b1b1b7;
  stroke-width: 2.5;
  opacity: 0.35;
}
.vf-canvas .vue-flow__edge.selected .vue-flow__edge-path {
  stroke: #3b82f6;
  opacity: 0.85;
}

/* Debug overlay panel */
.vf-debug-panel {
  position: absolute;
  top: 8px;
  right: 8px;
  z-index: 100;
  background: rgba(0, 0, 0, 0.88);
  color: #e5e7eb;
  font-family: monospace;
  font-size: 11px;
  line-height: 1.5;
  border-radius: 8px;
  padding: 10px 12px;
  max-width: 400px;
  max-height: 65vh;
  overflow-y: auto;
  backdrop-filter: blur(4px);
  pointer-events: none;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.vf-debug-section {
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.vf-debug-section b {
  color: #9ca3af;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 2px;
}
.vf-debug-row {
  display: flex;
  align-items: center;
  gap: 6px;
  overflow: hidden;
}
.vf-debug-row .badge {
  flex-shrink: 0;
  padding: 1px 5px;
  border-radius: 3px;
  font-size: 9px;
  font-weight: bold;
  background: #374151;
  color: #9ca3af;
}
.vf-debug-row.ok .badge { background: #14532d; color: #4ade80; }
.vf-debug-row.no .badge { background: #7c2d12; color: #fb923c; }
.vf-debug-hint {
  font-size: 9px;
  color: #6b7280;
  text-align: right;
}

/* ---- Controls panel ---- */
.vf-canvas .vue-flow__controls {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 8px;
  box-shadow: none;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
.vf-canvas .vue-flow__controls-button {
  background: var(--card);
  border: none;
  border-bottom: 1px solid var(--border);
  color: var(--foreground);
  width: 30px;
  height: 30px;
  padding: 6px;
}
.vf-canvas .vue-flow__controls-button:last-child {
  border-bottom: none;
}
.vf-canvas .vue-flow__controls-button:hover {
  background: var(--muted);
}
.vf-canvas .vue-flow__controls-button svg {
  width: 14px;
  max-width: 14px;
  height: 14px;
  max-height: 14px;
  fill: currentColor;
}
/* Bottom two buttons: larger icons */
.vf-canvas .vue-flow__controls-button:nth-last-child(-n+2) {
  width: 30px;
  height: 32px;
}
.vf-canvas .vue-flow__controls-button:nth-last-child(-n+2) svg {
  width: 16px;
  max-width: 16px;
  height: 16px;
  max-height: 16px;
}

/* ---- MiniMap ---- */
.vf-canvas .vue-flow__minimap {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: none;
}
.vf-canvas .vue-flow__minimap-mask {
  fill: var(--muted);
  opacity: 0.65;
}
</style>
