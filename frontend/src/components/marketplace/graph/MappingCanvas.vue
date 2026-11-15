<script setup>
import {
  ref,
  computed,
  onMounted,
  onBeforeUnmount,
  watch,
  nextTick,
  toRaw,
  provide,
} from "vue";
import { useStore } from "vuex";
import { VueFlow, useVueFlow } from "@vue-flow/core";
import { Background } from "@vue-flow/background";
import { Controls } from "@vue-flow/controls";
import { MiniMap } from "@vue-flow/minimap";
import { Button } from "@/components/ui/button";
import { ButtonGroup } from "@/components/ui/button-group";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { confirm } from "@/composables/useConfirm";
import HelperLinesOverlay from "./HelperLinesOverlay.vue";
import { applyHelperLineChanges } from "./helperLines.js";
import {
  Save,
  AlertTriangle,
  EyeOff,
  Activity,
  ChevronDown,
  Download,
  LayoutGrid,
  Minus,
  Plus,
  RefreshCw,
  Upload,
  ExternalLink,
} from "lucide-vue-next";
import { RouterLink } from "vue-router";
import { Spinner } from "@/components/ui/spinner";
import { SplitterGroup, SplitterPanel, SplitterResizeHandle } from "reka-ui";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import NodeContextMenu from "./NodeContextMenu.vue";
import NodeInspector from "./NodeInspector.vue";
import SearchableSelect from "@/components/ui/searchable-select/SearchableSelect.vue";
import { t } from "@/i18n";

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
  ADD_ADAPTER_FIELD_HANDLE,
  appendCustomAdapterField,
  sanitizeGraphNodes,
  sanitizeGraphEdges,
  mergeProductFieldSources,
  attributeValueType,
  SOURCE_NODE_TYPES,
  OP_NODE_TYPES,
} from "./graphModel.js";
import { MODIFIER_CATALOG } from "../modifierCatalog.js";
import { layoutGraph, sinksOverlap } from "./graphLayout.js";
import {
  localizeExternalFieldDescription,
  localizeExternalFieldName,
} from "./fieldLabels.js";
import { samePriceFieldSchema } from "./graphPriceFields.js";
import {
  analyzeUnitWarnings,
  resolveTerminalUnit,
  resolveConversionUnits,
} from "@/lib/mappingUnitWarnings";
import { getUnitConversion, normalizeUnitCode } from "@/lib/attributeUnits";

import "@vue-flow/core/dist/style.css";
import "@vue-flow/core/dist/theme-default.css";
import "@vue-flow/controls/dist/style.css";
import "@vue-flow/minimap/dist/style.css";

const props = defineProps({
  connectionId: { type: [Number, String], required: true },
  mappingId: { type: [Number, String], required: true },
  // Returns the GET …/graph response; the page owns it so one request also feeds field defs.
  graphLoader: { type: Function, default: null },
  // Live field definitions from /external-fields: { [fieldKey]: { name, type, options: [{value,label}] } }
  externalFieldDefs: { type: Object, default: () => ({}) },
  priceFieldMappings: { type: Array, default: () => [] },
  priceFieldDefs: { type: Object, default: () => ({}) },
  availableSources: { type: Object, default: () => ({}) },
  previewProductId: { type: [Number, String], default: null },
  previewProducts: { type: Array, default: () => [] },
  previewProductsLoading: { type: Boolean, default: false },
});
const emit = defineEmits([
  "preview-result",
  "clear-preview",
  "select-preview",
  "fields-synced",
  "search-preview",
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
  zoomIn,
  zoomOut,
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

/** Snap node moves to a fixed grid (toggle in bottom-left Controls). */
const snapToGrid = ref(false);
const SNAP_GRID = [16, 16];
/** Alignment guidelines while dragging (toggle in bottom-left Controls). */
const showGuidelines = ref(true);
const helperLineHorizontal = ref(undefined);
const helperLineVertical = ref(undefined);

const loading = ref(true);
const loadingPhase = ref("graph");
const LOAD_STATUS_BY_PHASE = {
  "load:api:getMappingGraph": "graph",
  "load:prepareGraph": "graph",
  "load:setNodes": "layout",
  "load:setEdges": "layout",
  "load:relayoutMeasured": "layout",
  "load:finalizeNodes": "layout",
  "load:refreshAnalysis": "layout",
  "load:applyEdgeLaneTypes": "layout",
  "load:fitView": "layout",
  "load:historySnapshot": "layout",
};
const loadingStatusText = computed(() =>
  t(`marketplace.fieldMappingPage.loadStatus.${loadingPhase.value}`),
);
const saving = ref(false);
const syncingFields = ref(false);
const dirty = ref(false);
// Content version of the stored graph this canvas was loaded from (conflict-safe save).
let graphVersion = null;
const allowCustomFields = ref(false);
const previewing = ref(false);
// True from the moment a change is scheduled until fresh preview values land:
// nodes and the inspector swap stale values for a loader instead of showing
// outdated output without any indication.
const previewStale = ref(false);
// True when the last recompute failed: values still shown are knowingly old,
// rendered dimmed instead of spinning forever or looking current.
const previewOutdated = ref(false);
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

// Transient adapter/UI fields — never persist in undo/clipboard (and avoid walking them).
const TRANSIENT_NODE_DATA_KEYS = new Set([
  "preview",
  "previewValue",
  "analysis",
  "fieldOptionsMap",
  "sources",
  "availableSources",
  "setFieldSource",
  "setFieldMode",
  "openField",
  "unitWarnings",
]);

/**
 * Deep-clone plain data without JSON.stringify through Vue proxies.
 * Proxy enumeration on a large mapping graph was freezing the UI for ~1–2s after node drag.
 */
function clonePlain(value, seen = new WeakMap()) {
  if (value === null || typeof value !== "object") return value;
  const raw = toRaw(value);
  if (raw === null || typeof raw !== "object") return raw;
  if (typeof raw === "function") return undefined;
  if (seen.has(raw)) return seen.get(raw);
  if (Array.isArray(raw)) {
    const arr = new Array(raw.length);
    seen.set(raw, arr);
    for (let i = 0; i < raw.length; i++) {
      const v = raw[i];
      arr[i] = typeof v === "function" ? null : clonePlain(v, seen);
    }
    return arr;
  }
  if (raw instanceof Date) return new Date(raw.getTime());
  const out = {};
  seen.set(raw, out);
  for (const key of Object.keys(raw)) {
    const v = raw[key];
    if (typeof v === "function") continue;
    out[key] = clonePlain(v, seen);
  }
  return out;
}

function snapshotNodeData(data) {
  const src = toRaw(data);
  if (!src || typeof src !== "object") return {};
  const out = {};
  for (const key of Object.keys(src)) {
    if (TRANSIENT_NODE_DATA_KEYS.has(key)) continue;
    const v = src[key];
    if (typeof v === "function") continue;
    out[key] = clonePlain(v);
  }
  return out;
}

/** Lean graph snapshot for undo — only fields needed to restore, not full Vue Flow node objects. */
function makeSnapshot() {
  return {
    nodes: nodes.value.map((n) => {
      const raw = toRaw(n);
      const pos = raw.position;
      return {
        id: raw.id,
        type: raw.type,
        position: { x: pos?.x ?? 0, y: pos?.y ?? 0 },
        deletable: raw.deletable,
        data: snapshotNodeData(raw.data),
      };
    }),
    edges: edges.value.map((e) => {
      const raw = toRaw(e);
      return {
        id: raw.id,
        source: raw.source,
        target: raw.target,
        sourceHandle: raw.sourceHandle,
        targetHandle: raw.targetHandle,
      };
    }),
  };
}

function scheduleHistory() {
  if (isRestoring || historyPending) return;
  historyPending = true;
  // Defer past Vue's DOM flush + the browser's input/a11y turn so drag-end
  // does not compete with Accessibility notifications on the same long task.
  nextTick(() => {
    requestAnimationFrame(() => {
      try {
        if (isRestoring) return;
        history.value = history.value.slice(0, histIdx.value + 1);
        history.value.push(makeSnapshot());
        if (history.value.length > 60) history.value.shift();
        else histIdx.value++;
      } finally {
        historyPending = false;
      }
    });
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
const canRedo = computed(() => histIdx.value < history.value.length - 1);

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
function waitForPaint() {
  return new Promise((resolve) => {
    requestAnimationFrame(() => requestAnimationFrame(resolve));
  });
}

async function loadStep(phase, fn) {
  const status = LOAD_STATUS_BY_PHASE[phase];
  if (status && loadingPhase.value !== status) {
    loadingPhase.value = status;
    await waitForPaint();
  }
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
    const apiName = def?.name || f.label;
    const apiDesc = def?.description ?? f.description ?? null;
    return {
      ...f,
      label: localizeExternalFieldName(f.fieldKey, apiName),
      outputType: def?.type || f.outputType,
      group: def?.group ?? f.group ?? null,
      description: localizeExternalFieldDescription(f.fieldKey, apiDesc),
      sortOrder: def?.sortOrder ?? f.sortOrder ?? null,
      unit: def?.unit ?? f.unit ?? null,
      unitCode: def?.unitCode ?? f.unitCode ?? null,
      unitId: def?.unitId ?? f.unitId ?? null,
      minValue: def?.minValue ?? f.minValue ?? null,
      maxValue: def?.maxValue ?? f.maxValue ?? null,
      maxLength: def?.maxLength ?? f.maxLength ?? null,
      allowCustomValues: def?.allowCustomValues ?? f.allowCustomValues ?? false,
      maxValues: def?.maxValues ?? f.maxValues ?? null,
    };
  });
}

/** Enrich adapter field labels/tooltips once - not on every refreshAnalysis (avoids re-render thrash). */
function applyFieldDefEnrichment() {
  const adapterNode = findNode(ADAPTER_NODE_ID);
  if (adapterNode) {
    updateNodeData(ADAPTER_NODE_ID, {
      fields: enrichFieldsWithDefs(
        adapterNode.data?.fields,
        props.externalFieldDefs,
      ),
    });
  }
  const priceNode = findNode(ADAPTER_PRICE_NODE_ID);
  if (priceNode) {
    updateNodeData(ADAPTER_PRICE_NODE_ID, {
      fields: enrichFieldsWithDefs(
        priceNode.data?.fields,
        props.priceFieldDefs,
      ),
    });
  }
}

function attributeDefinition(node) {
  const list =
    node.type === "attribute"
      ? props.availableSources.categoryAttributes
      : props.availableSources.storeAttributes;
  return (list || []).find(
    (item) => String(item.id) === String(node.data?.attributeId),
  );
}

/**
 * Keep derived block metadata in step with the attribute definitions and wiring:
 * attribute blocks get their definition's type/unit, and automatic conversions
 * refresh both units from the input source and the adapter field they feed.
 */
// Set when reconcileUnitMetadata repairs persisted params, so a load keeps the canvas dirty.
let unitMetadataRepaired = false;

function reconcileUnitMetadata() {
  let changed = false;
  for (const node of nodes.value) {
    if (node.type !== "attribute" && node.type !== "storeAttribute") continue;
    const def = attributeDefinition(node);
    if (!def) continue;
    const patch = {};
    const type = attributeValueType(def.type);
    // Multiselect changes how the evaluator reads stored values; only new blocks get it.
    if (
      type &&
      type !== "multiselect" &&
      (!node.data?.valueType || node.data.valueType === "any")
    )
      patch.valueType = type;
    const unit = normalizeUnitCode(def.unitCode);
    if ((node.data?.unitCode || null) !== (unit || null)) patch.unitCode = unit;
    if (Object.keys(patch).length) updateNodeData(node.id, patch);
  }

  const conversions = resolveConversionUnits(
    nodes.value,
    edges.value,
    props.availableSources,
  );
  for (const node of nodes.value) {
    const params = conversions.get(node.id);
    if (!params) continue;
    if (
      params.fromUnit !== node.data?.params?.fromUnit ||
      params.toUnit !== node.data?.params?.toUnit
    ) {
      updateNodeData(node.id, { params });
      if (selected.value?.id === node.id)
        selected.value = {
          ...selected.value,
          data: { ...selected.value.data, params },
        };
      changed = true;
    }
  }
  if (changed) {
    unitMetadataRepaired = true;
    dirty.value = true;
  }
}

/** Insert a unit conversion block on a wire into an adapter field. */
function insertUnitConversion(edge, fromUnit, toUnit) {
  const sink = findNode(edge.target);
  const source = findNode(edge.source);
  const position = sink?.position || { x: 700, y: 0 };
  const conversionNode = createModifierNode("unitConversion", {
    x: position.x - 260,
    y: source?.position?.y ?? position.y + 40,
  });
  conversionNode.data = {
    ...conversionNode.data,
    collapsed: true,
    params: { fromUnit, toUnit },
  };
  addNodes([conversionNode]);
  addEdges([
    {
      source: edge.source,
      sourceHandle: edge.sourceHandle,
      id: genId("e"),
      target: conversionNode.id,
      targetHandle: "in",
    },
    {
      id: genId("e"),
      source: conversionNode.id,
      sourceHandle: "out",
      target: edge.target,
      targetHandle: edge.targetHandle,
    },
  ]);
  return conversionNode;
}

function directSourceValue(source) {
  if (source?.type === "attribute") return `a:${source.attributeId}`;
  if (source?.type === "storeAttribute") return `s:${source.attributeId}`;
  return null;
}

/** Resolve a unit warning by inserting the matching conversion block. */
function fixUnitMismatch(warning) {
  if (!warning?.convertible) return;
  if (warning.edgeId) {
    const edge = edges.value.find((e) => e.id === warning.edgeId);
    if (!edge) return;
    removeEdges([edge.id]);
    insertUnitConversion(edge, warning.sourceUnit, warning.targetUnit);
  } else if (warning.sinkId && warning.fieldKey) {
    const field = (findNode(warning.sinkId)?.data?.fields || []).find(
      (f) => f.fieldKey === warning.fieldKey,
    );
    const value = directSourceValue(field?.source);
    if (!value) return;
    setSinkFieldSource(warning.sinkId, warning.fieldKey, value);
  } else {
    return;
  }
  dirty.value = true;
  nextTick(() => {
    refreshAnalysis();
    schedulePreview();
  });
}

provide("mappingUnitFix", fixUnitMismatch);
provide("graphPreviewStale", previewStale);
provide("graphPreviewOutdated", previewOutdated);

function refreshAnalysis(options = {}) {
  const { updateLanes = true } = options;
  reconcileUnitMetadata();
  const { analysisBySink } = analyzeFields(nodes.value, edges.value);
  const unitWarnings = analyzeUnitWarnings(
    nodes.value,
    edges.value,
    props.availableSources,
  );
  syncUnitWarningEdges(unitWarnings.edgeWarnings);
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
    setFieldMode,
    openField: openAdapterField,
    addCustomField: allowCustomFields.value ? addCustomAdapterField : undefined,
    unitWarnings: unitWarnings.directWarnings[ADAPTER_NODE_ID] || {},
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
        connected: !!(
          f.source ||
          (edges.value || []).some(
            (e) =>
              e.target === ADAPTER_PRICE_NODE_ID &&
              e.targetHandle === `f:${f.fieldKey}`,
          )
        ),
        importable: false,
      };
    }
    updateNodeData(ADAPTER_PRICE_NODE_ID, {
      analysis: priceAnalysis,
      fieldOptionsMap: priceFieldOptionsMap,
      sources: adapterSourceLabels(nodes.value, edges.value),
      availableSources: props.availableSources,
      setFieldSource: setPriceFieldSource,
      setFieldMode: setPriceFieldMode,
      openField: openPriceField,
      unitWarnings: unitWarnings.directWarnings[ADAPTER_PRICE_NODE_ID] || {},
    });
  }

  if (updateLanes) applyEdgeLaneTypes(analysisBySink);
}

function syncUnitWarningEdges(warnings) {
  let changed = false;
  const updated = edges.value.map((edge) => {
    const unitWarning = warnings.get(edge.id) || null;
    const current = edge.data?.unitWarning || null;
    if (JSON.stringify(current) === JSON.stringify(unitWarning)) return edge;
    changed = true;
    const data = { ...(edge.data || {}) };
    if (unitWarning) data.unitWarning = unitWarning;
    else delete data.unitWarning;
    return { ...edge, data };
  });
  if (changed) setEdges(updated);
}

// Mark edges by lane type: importable (bidirectional) vs export-only (one-way).
function pruneSourceBoundEdges() {
  const toRemove = [];
  for (const sinkId of [ADAPTER_NODE_ID, ADAPTER_PRICE_NODE_ID]) {
    const sink = findNode(sinkId);
    if (!sink) continue;
    const boundHandles = new Set(
      (sink.data?.fields || [])
        .filter((f) => f.source || f.aliasOf || f.omit)
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
  const sinkAnalysis =
    analysisBySink ?? analyzeFields(nodes.value, edges.value).analysisBySink;
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
    exportOnlyIds.has(e.id)
      ? "vf-export"
      : importableIds.has(e.id)
        ? "vf-import"
        : "default";
  const needsUpdate = edges.value.some(
    (e) => (e.type || "default") !== laneType(e),
  );
  if (needsUpdate) {
    setEdges(
      edges.value.map((e) => {
        const type = laneType(e);
        return (e.type || "default") === type
          ? e
          : { ...e, type, animated: false };
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

/** Field binding mode: omit | wire | bind (source or aliasOf). */
function setSinkFieldMode(sinkId, fieldKey, mode) {
  const adapter = findNode(sinkId);
  if (!adapter) return;
  const current = (adapter.data?.fields || []).find(
    (f) => f.fieldKey === fieldKey,
  );
  // Required marketplace fields must be sent — omit is not allowed.
  if (mode === "omit" && current?.isRequired) return;
  const fields = (adapter.data?.fields || []).map((f) => {
    if (f.fieldKey !== fieldKey) return f;
    const nf = { ...f };
    if (mode === "omit") {
      nf.omit = true;
      delete nf.source;
      delete nf.aliasOf;
    } else if (mode === "wire") {
      delete nf.omit;
      delete nf.source;
      delete nf.aliasOf;
    } else {
      // bind — keep existing source/aliasOf if any; clear omit so pin stays hidden
      delete nf.omit;
    }
    return nf;
  });
  updateNodeData(sinkId, { fields });
  if (mode === "omit" || mode === "bind") {
    const dead = edges.value.filter(
      (e) => e.target === sinkId && e.targetHandle === `f:${fieldKey}`,
    );
    if (dead.length) removeEdges(dead.map((e) => e.id));
  }
  dirty.value = true;
  refreshAnalysis();
  schedulePreview();
  scheduleHistory();
}

function setSinkFieldSource(sinkId, fieldKey, value) {
  const adapter = findNode(sinkId);
  if (!adapter) return;
  // "m:<primaryKey>" merges this field into a duplicate: it exports the primary's
  // value under its own key and is skipped on import. Any other selection un-merges.
  const mergeWith =
    typeof value === "string" && value.startsWith("m:") ? value.slice(2) : null;
  if (mergeWith === fieldKey) return;
  const src = mergeWith ? null : parseSourceValue(value);
  const currentField = (adapter.data?.fields || []).find(
    (f) => f.fieldKey === fieldKey,
  );
  const sourceList =
    src?.type === "attribute"
      ? props.availableSources.categoryAttributes
      : src?.type === "storeAttribute"
        ? props.availableSources.storeAttributes
        : [];
  const sourceDefinition = sourceList?.find(
    (item) => Number(item.id) === Number(src?.attributeId),
  );
  const sourceUnit = sourceDefinition?.unitCode ?? null;
  const targetUnit = currentField?.unitCode ?? currentField?.unit ?? null;
  const normalizedSourceUnit = normalizeUnitCode(sourceUnit);
  const normalizedTargetUnit = normalizeUnitCode(targetUnit);
  const conversion =
    normalizedSourceUnit &&
    normalizedTargetUnit &&
    normalizedSourceUnit.toLowerCase() !== normalizedTargetUnit.toLowerCase()
      ? getUnitConversion(normalizedSourceUnit, normalizedTargetUnit)
      : null;
  let conversionNodes = null;
  if (src && conversion) {
    const x = adapter.position?.x ?? 700;
    const y = (adapter.position?.y ?? 0) + 40;
    const sourceNode = createSourceNode(
      src.type,
      { x: x - 520, y },
      {
        id: src.attributeId,
        name: sourceDefinition?.name,
        valueType: attributeValueType(sourceDefinition?.type) || "any",
        unitCode: normalizedSourceUnit,
      },
    );
    const conversionNode = createModifierNode("unitConversion", {
      x: x - 260,
      y,
    });
    conversionNode.data = {
      ...conversionNode.data,
      collapsed: true,
      params: { fromUnit: normalizedSourceUnit, toUnit: normalizedTargetUnit },
    };
    conversionNodes = { sourceNode, conversionNode };
  }
  const fields = (adapter.data?.fields || []).map((f) => {
    if (f.fieldKey !== fieldKey) return f;
    const nf = { ...f };
    delete nf.omit;
    if (mergeWith) {
      nf.aliasOf = mergeWith;
      delete nf.source;
    } else {
      delete nf.aliasOf;
      if (src && !conversionNodes) nf.source = src;
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
  if (conversionNodes) {
    addNodes([conversionNodes.sourceNode, conversionNodes.conversionNode]);
    addEdges([
      {
        id: genId("e"),
        source: conversionNodes.sourceNode.id,
        sourceHandle: "out",
        target: conversionNodes.conversionNode.id,
        targetHandle: "in",
      },
      {
        id: genId("e"),
        source: conversionNodes.conversionNode.id,
        sourceHandle: "out",
        target: sinkId,
        targetHandle: `f:${fieldKey}`,
      },
    ]);
  }
  dirty.value = true;
  refreshAnalysis();
  schedulePreview();
  scheduleHistory();
}

function setFieldSource(fieldKey, value) {
  setSinkFieldSource(ADAPTER_NODE_ID, fieldKey, value);
}

function setPriceFieldSource(fieldKey, value) {
  setSinkFieldSource(ADAPTER_PRICE_NODE_ID, fieldKey, value);
}

function setFieldMode(fieldKey, mode) {
  setSinkFieldMode(ADAPTER_NODE_ID, fieldKey, mode);
}

function setPriceFieldMode(fieldKey, mode) {
  setSinkFieldMode(ADAPTER_PRICE_NODE_ID, fieldKey, mode);
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
// Field whose input pin a wire drag started from - lets the node menu offer
// that field's option values as ready-made standard-value blocks.
const menuAdapterValueField = computed(() => {
  const pc = pendingConnect.value;
  if (!pc || pc.dir !== "target" || !pc.handleId?.startsWith("f:")) return null;
  const key = pc.handleId.slice(2);
  return adapterFieldsWithOptions.value.find((f) => f.fieldKey === key) || null;
});

function normalizeAiModelOption(m) {
  const id = m?.id ?? m?.Id;
  if (id == null) return null;
  const name = m?.name ?? m?.Name ?? m?.modelId ?? m?.ModelId;
  return {
    value: id,
    label: name || t("marketplace.canvas.aiModelFallback", { id }),
  };
}

const aiModelOptions = computed(() =>
  (store.state.aiModels?.pickerModels ?? [])
    .map(normalizeAiModelOption)
    .filter(Boolean),
);

async function loadPickerModels() {
  try {
    const { loadMe } = await import("@/main");
    const me = await loadMe();
    if (me?.aiEnabled !== true) return;
  } catch {
    return;
  }
  store.dispatch("aiModels/getPickerModels", { onError: () => {} });
}

const templateFieldRefs = computed(() => {
  const refs = [
    {
      group: "value",
      label: t("marketplace.canvas.sourceValueLabel"),
      snippet: "{{value}}",
    },
  ];
  for (const pf of mergeProductFieldSources(props.availableSources)) {
    const group = (props.availableSources.stockFields || []).some(
      (f) => f.key === pf.key,
    )
      ? "stock"
      : (props.availableSources.priceFields || []).some((f) => f.key === pf.key)
        ? "price"
        : "product";
    refs.push({ group, label: pf.displayName, snippet: `{{${pf.key}}}` });
  }
  for (const ca of props.availableSources.categoryAttributes || [])
    refs.push({
      group: "category",
      label: ca.name,
      snippet: `{{attr:${ca.id}}}`,
    });
  for (const sa of props.availableSources.storeAttributes || [])
    refs.push({
      group: "store",
      label: sa.name,
      snippet: `{{storeAttr:${sa.id}}}`,
    });
  if (
    selected.value?.type === "modifier" &&
    selected.value?.data?.modifierType === "llmGenerate"
  ) {
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
      attrOptionsCache.value = {
        ...attrOptionsCache.value,
        [cacheKey]: fromSources,
      };
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

function addCustomAdapterField(opts = {}) {
  const node = findNode(ADAPTER_NODE_ID);
  if (!node) return null;
  const { fields, created } = appendCustomAdapterField(node.data?.fields, opts);
  updateNodeData(ADAPTER_NODE_ID, { fields });
  dirty.value = true;
  openAdapterField(created.fieldKey);
  schedulePreview();
  return created;
}

onConnect((conn) => {
  connectionMade.value = true;
  let next = { ...conn };
  if (
    next.target === ADAPTER_NODE_ID &&
    next.targetHandle === ADD_ADAPTER_FIELD_HANDLE
  ) {
    if (!allowCustomFields.value) return;
    const created = addCustomAdapterField();
    if (!created) return;
    next = { ...next, targetHandle: `f:${created.fieldKey}` };
  }
  const handle = next.targetHandle || "in";
  const dupes = edges.value.filter(
    (e) => e.target === next.target && (e.targetHandle || "in") === handle,
  );
  if (dupes.length) removeEdges(dupes.map((e) => e.id));
  const sink = nodes.value.find((node) => node.id === next.target);
  const fieldKey = next.targetHandle?.startsWith("f:")
    ? next.targetHandle.slice(2)
    : null;
  const sinkField = fieldKey
    ? (sink?.data?.fields || []).find((field) => field.fieldKey === fieldKey)
    : null;
  let conversionNode = null;
  if (
    sinkField &&
    (sink?.type === "adapterFields" || sink?.type === "adapterPriceFields")
  ) {
    const nodesById = new Map(nodes.value.map((node) => [node.id, node]));
    const incomingByTarget = new Map();
    for (const edge of edges.value) {
      const list = incomingByTarget.get(edge.target) || [];
      list.push(edge);
      incomingByTarget.set(edge.target, list);
    }
    const sourceUnit = resolveTerminalUnit(
      next.source,
      nodesById,
      incomingByTarget,
      props.availableSources,
    );
    const targetUnit = sinkField.unitCode ?? sinkField.unit ?? null;
    const normalizedSourceUnit = normalizeUnitCode(sourceUnit);
    const normalizedTargetUnit = normalizeUnitCode(targetUnit);
    const conversion =
      normalizedSourceUnit &&
      normalizedTargetUnit &&
      normalizedSourceUnit.toLowerCase() !== normalizedTargetUnit.toLowerCase()
        ? getUnitConversion(normalizedSourceUnit, normalizedTargetUnit)
        : null;
    if (conversion) {
      conversionNode = insertUnitConversion(
        next,
        normalizedSourceUnit,
        normalizedTargetUnit,
      );
    }
  }
  if (!conversionNode) addEdges([{ ...next, id: genId("e") }]);

  // When a "standard value" node connects to an adapter field that has predefined
  // options, auto-populate the node's options and valueType from the field definition.
  if (
    (next.target === ADAPTER_NODE_ID ||
      next.target === ADAPTER_PRICE_NODE_ID) &&
    next.targetHandle?.startsWith("f:")
  ) {
    const fieldKey = next.targetHandle.slice(2);
    const src = nodes.value.find((n) => n.id === next.source);
    const def =
      next.target === ADAPTER_PRICE_NODE_ID
        ? props.priceFieldDefs[fieldKey]
        : props.externalFieldDefs[fieldKey];

    syncMapModifiersForField(next, fieldKey, def);

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
  flowWrap.value?.addEventListener("wheel", onWheelGesture, {
    capture: true,
    passive: false,
  });
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
  flowWrap.value?.removeEventListener("wheel", onWheelGesture, {
    capture: true,
  });
});
watch(
  () => props.mappingId,
  () => requestLoad(),
);
watch(
  () => props.previewProductId,
  (id) => (id ? schedulePreview() : clearNodePreview()),
);
// Sources can arrive after the initial load - re-push them to the adapter's dropdown when they do.
watch(
  () => props.availableSources,
  () => {
    if (loading.value) return;
    refreshAnalysis();
    preloadMapModifierOptions();
  },
  { deep: true },
);
// Field defs (name/description/unit) can arrive after graph load - enrich once, not on every analysis pass.
watch(
  () => [props.externalFieldDefs, props.priceFieldDefs],
  () => {
    if (loading.value) return;
    applyFieldDefEnrichment();
  },
  { deep: true },
);
// Price-API field list can arrive after graph load - append missing fields to the price sink.
watch(
  () => props.priceFieldMappings,
  (mappings) => {
    if (loading.value || !findNode(ADAPTER_PRICE_NODE_ID)) return;
    const cur = findNode(ADAPTER_PRICE_NODE_ID)?.data?.fields || [];
    const copy = nodes.value.map((n) => ({
      ...n,
      data: n.data?.fields
        ? { ...n.data, fields: n.data.fields.map((f) => ({ ...f })) }
        : { ...n.data },
    }));
    syncAdapterPriceFields(copy, mappings);
    const next =
      copy.find((n) => n.id === ADAPTER_PRICE_NODE_ID)?.data?.fields || [];
    const enrichedNext = enrichFieldsWithDefs(next, props.priceFieldDefs);
    if (!samePriceFieldSchema(cur, enrichedNext)) {
      const kept = new Set(next.map((f) => f.fieldKey));
      setEdges(
        edges.value.filter((e) => {
          if (e.target !== ADAPTER_PRICE_NODE_ID) return true;
          const handle = String(e.targetHandle || "");
          if (!handle.startsWith("f:")) return true;
          const key = handle.slice(2);
          if (
            key !== "discountedPrice" &&
            key !== "sale_price" &&
            key !== "salePrice"
          )
            return true;
          return kept.has(key);
        }),
      );
      updateNodeData(ADAPTER_PRICE_NODE_ID, {
        fields: enrichedNext,
      });
      dirty.value = true;
    }
    refreshAnalysis();
  },
  { deep: true },
);
// Only react to structural changes (add/remove - e.g. the Delete key), not position/select/data,
// to avoid a feedback loop with preview's own updateNodeData() calls.
onNodesChange((changes) => {
  const cs = changes || [];

  if (showGuidelines.value) {
    const lines = applyHelperLineChanges(cs, nodes.value, true);
    helperLineHorizontal.value = lines.horizontal;
    helperLineVertical.value = lines.vertical;
  } else {
    helperLineHorizontal.value = undefined;
    helperLineVertical.value = undefined;
  }

  // Clear guidelines when drag ends.
  if (cs.some((c) => c.type === "position" && c.dragging === false)) {
    helperLineHorizontal.value = undefined;
    helperLineVertical.value = undefined;
  }

  const structural = cs.some((c) => c.type === "remove" || c.type === "add");
  if (structural) {
    refreshAnalysis();
    schedulePreview();
  }
  // Mark dirty on structural changes or on drag-end (position change, dragging===false).
  if (
    !loading.value &&
    (structural || cs.some((c) => c.type === "position" && !c.dragging))
  ) {
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
  if (node.type === "adapterFields" || node.type === "adapterPriceFields")
    return;
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
  unitMetadataRepaired = false;
  loading.value = true;
  loadingPhase.value = "graph";
  await waitForPaint();
  try {
    const data = await loadStep(
      "load:api:getMappingGraph",
      () =>
        props.graphLoader
          ? props.graphLoader()
          : store.dispatch("getMappingGraph", {
              connectionId: props.connectionId,
              mappingId: props.mappingId,
            }),
      stepCtx,
    );
    graphVersion = data?.graphVersion ?? null;
    allowCustomFields.value = data?.allowCustomFields === true;

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
      graph = autoMigrate([], props.availableSources);
      migrated = true;
    }

    await loadStep(
      "load:prepareGraph",
      async () => {
        const ensured = ensurePriceNode(
          graph.nodes,
          graph.edges,
          props.priceFieldMappings,
        );
        graph.nodes = syncAdapterPriceFields(
          ensured.nodes,
          props.priceFieldMappings,
        );
        graph.edges = ensured.edges;
        ensureModifierParamDefaults(graph.nodes);
      },
      { ...stepCtx, migrated },
    );

    const { nodes: safeNodes } = sanitizeGraphNodes(graph.nodes);

    const rawNodeList = graph.nodes || [];
    const noneHavePosition =
      rawNodeList.length > 0 && rawNodeList.every((n) => !n?.position);
    const needsLayout =
      !preserveLayout &&
      (migrated || noneHavePosition || sinksOverlap(safeNodes));
    const nodeIds = new Set(safeNodes.map((n) => n.id));
    const { edges: safeEdges } = sanitizeGraphEdges(graph.edges, nodeIds);

    const nodesToSet = needsLayout
      ? layoutGraph(safeNodes, safeEdges)
      : safeNodes;

    await loadStep(
      "load:setNodes",
      async () => {
        setNodes(nodesToSet);
        await nextTick();
      },
      { ...stepCtx, needsLayout, nodeCount: nodesToSet.length },
    );

    await loadStep(
      "load:setEdges",
      async () => {
        setEdges(safeEdges);
        await nextTick();
      },
      { ...stepCtx, edgeCount: safeEdges.length },
    );

    if (needsLayout) {
      await loadStep(
        "load:relayoutMeasured",
        async () => {
          const measured = collectMeasuredSizes(nodes.value);
          if (measured.size > 0) {
            const obj = toObject();
            setNodes(layoutGraph(obj.nodes, obj.edges, measured));
            await nextTick();
          }
        },
        stepCtx,
      );
    }

    await loadStep(
      "load:finalizeNodes",
      async () => {
        const adNode = findNode(ADAPTER_NODE_ID);
        if (adNode) adNode.deletable = false;
        const priceNode = findNode(ADAPTER_PRICE_NODE_ID);
        if (priceNode) priceNode.deletable = false;
        pruneSourceBoundEdges();
        await nextTick();
      },
      stepCtx,
    );

    await loadStep(
      "load:refreshAnalysis",
      async () => {
        applyFieldDefEnrichment();
        refreshAnalysis({ updateLanes: false });
        await waitForPaint();
      },
      stepCtx,
    );

    await loadStep(
      "load:applyEdgeLaneTypes",
      async () => {
        applyEdgeLaneTypes();
        await nextTick();
      },
      stepCtx,
    );

    if (!preserveLayout && flowWrap.value?.clientHeight > 0) {
      await loadStep(
        "load:fitView",
        async () => {
          fitView({ padding: 0.2 });
          await nextTick();
        },
        stepCtx,
      );
    }

    // Repaired conversion units must be saved to affect export.
    dirty.value = unitMetadataRepaired;
    await loadStep(
      "load:historySnapshot",
      async () => {
        history.value = [makeSnapshot()];
        histIdx.value = 0;
      },
      stepCtx,
    );
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
        ? t("marketplace.canvas.fieldsAddedCount", { count: result.totalAdded })
        : t("marketplace.canvas.schemaUpToDate"));
    displaySonnerSuccess(message);
    emit("fields-synced");
  } catch (e) {
    displaySonnerError(e);
  } finally {
    syncingFields.value = false;
  }
}

async function confirmAutoArrange() {
  const ok = await confirm({
    title: t("marketplace.fieldMappingPage.rearrangeBlocksTitle"),
    description: t("marketplace.fieldMappingPage.rearrangeBlocksDescription"),
    confirmLabel: t("marketplace.fieldMappingPage.rearrange"),
    destructive: false,
  });
  if (ok) autoArrange();
}

// ---- Context menu (right-click / Tab) ----

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
  pendingConnect.value = null; // plain right-click/Tab - not a wire drop
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
  const target = findNode(conn.target);
  if (
    target?.type === "modifier" &&
    target.data?.collapsed &&
    (conn.targetHandle?.startsWith("p:") ||
      conn.targetHandle?.startsWith("field:"))
  )
    return false;
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
  // regardless of the active keyboard layout - with a non-Latin layout e.key would differ.
  if (e.ctrlKey || e.metaKey) {
    if (e.code === "KeyZ") {
      e.preventDefault();
      if (e.shiftKey) {
        if (canRedo.value) restoreAt(histIdx.value + 1);
      } else {
        if (canUndo.value) restoreAt(histIdx.value - 1);
      }
    } else if (e.code === "KeyY") {
      e.preventDefault();
      if (canRedo.value) restoreAt(histIdx.value + 1);
    } else if (e.code === "KeyS") {
      e.preventDefault();
      save();
    } else if (e.code === "KeyC") {
      if (copySelection()) e.preventDefault();
    } else if (e.code === "KeyV") {
      e.preventDefault();
      pasteClipboard();
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
    if (x < rect.left || x > rect.right || y < rect.top || y > rect.bottom)
      return;
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
    node = createModifierNode(desc.modifierType, pos, desc.params);
  else if (desc.group === "op")
    node = createOpNode(desc.opType, pos, desc.params);
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
  if (
    Array.isArray(patch.fields) &&
    (selected.value.type === "adapterFields" ||
      selected.value.type === "adapterPriceFields")
  ) {
    const oldKeys = new Set((base.fields || []).map((f) => f.fieldKey));
    const newKeys = new Set(patch.fields.map((f) => f.fieldKey));
    const removed = [...oldKeys].filter((k) => !newKeys.has(k));
    const added = [...newKeys].filter((k) => !oldKeys.has(k));
    if (removed.length === 1 && added.length === 1) {
      const fromH = `f:${removed[0]}`;
      const toH = `f:${added[0]}`;
      const moved = edges.value.filter(
        (e) => e.target === selected.value.id && e.targetHandle === fromH,
      );
      if (moved.length) {
        removeEdges(moved.map((e) => e.id));
        addEdges(
          moved.map((e) => ({ ...e, id: genId("e"), targetHandle: toH })),
        );
      }
    } else if (removed.length) {
      const gone = edges.value.filter(
        (e) =>
          e.target === selected.value.id &&
          removed.includes(String(e.targetHandle || "").slice(2)),
      );
      if (gone.length) removeEdges(gone.map((e) => e.id));
    }
  }
  const next = { ...base, ...patch };
  if (patch.params && typeof patch.params === "object") {
    next.params = { ...(base.params || {}), ...patch.params };
  }
  updateNodeData(selected.value.id, next);
  selected.value = {
    ...(live || selected.value),
    data: next,
  };
  if ("importable" in patch || "params" in patch || "attributeId" in patch)
    nextTick(refreshAnalysis);
  dirty.value = true;
  schedulePreview();
}

function openAdapterField(fieldKey) {
  selected.value = findNode(ADAPTER_NODE_ID) ?? selected.value;
  nextTick(() => {
    focusAdapterField.value = fieldKey;
  });
}

function openPriceField(fieldKey) {
  selected.value = findNode(ADAPTER_PRICE_NODE_ID) ?? selected.value;
  nextTick(() => {
    focusAdapterField.value = fieldKey;
  });
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
  const src = toRaw(d);
  if (!src || typeof src !== "object") return {};
  const rest = {};
  for (const key of Object.keys(src)) {
    if (TRANSIENT_NODE_DATA_KEYS.has(key)) continue;
    const v = src[key];
    if (typeof v === "function") continue;
    rest[key] = v;
  }
  return rest;
}

// ---- Clipboard: copy / paste / duplicate selected nodes ----

const clipboard = ref({ nodes: [], edges: [] });

// Payload written to the system clipboard so copy/paste works across pages
// and cabinets; the in-memory `clipboard` ref is the same-page fallback.
const CLIPBOARD_KIND = "delegat-mapping-graph";

const PASTE_NODE_TYPES = new Set([
  ...SOURCE_NODE_TYPES,
  ...OP_NODE_TYPES,
  "modifier",
  "reroute",
]);

const SINK_NODE_TYPES = new Set(["adapterFields", "adapterPriceFields"]);

/** Whether a copied node can live in this cabinet/category: its external
   references (attributes, product fields, store files) must resolve here. */
// Field keys a pasted modifier can reference (valueMap/optionMap
// targetFieldKey): fields of the sinks in the pasted set plus the sinks
// already on the canvas.
const sinkFieldKeys = (list) => {
  const keys = new Set();
  for (const n of list || [])
    if (SINK_NODE_TYPES.has(n.type))
      for (const f of n.data?.fields || []) keys.add(f.fieldKey);
  return keys;
};

function clipboardNodeUsable(node, fieldKeys = new Set()) {
  if (!node || !PASTE_NODE_TYPES.has(node.type)) return false;
  const d = node.data || {};
  const src = props.availableSources;
  switch (node.type) {
    case "attribute":
      return (src.categoryAttributes || []).some((a) =>
        attrIdEq(a.id ?? a.Id, d.attributeId),
      );
    case "storeAttribute":
      return (src.storeAttributes || []).some((a) =>
        attrIdEq(a.id ?? a.Id, d.attributeId),
      );
    case "productField":
      return mergeProductFieldSources(src).some((f) => f.key === d.field);
    case "storeFileUrl":
    case "storeFileContent":
      return (src.storeFiles || []).some(
        (f) => String(f.id) === String(d.storeFileId),
      );
    case "modifier":
      if (!MODIFIER_CATALOG[d.modifierType]) return false;
      // Map modifiers bound to a field absent in both the pasted set and the
      // target graph would silently export wrong values.
      if (d.targetFieldKey && !fieldKeys.has(d.targetFieldKey)) return false;
      return true;
    default:
      return true;
  }
}

/** Keeps usable nodes and edges between them; reports the dropped count. */
function validateClipboard(clip) {
  const kept = [];
  let dropped = 0;
  const fieldKeys = new Set([
    ...sinkFieldKeys(clip?.nodes),
    ...sinkFieldKeys(nodes.value),
  ]);
  for (const n of clip?.nodes || []) {
    if (clipboardNodeUsable(n, fieldKeys)) kept.push(n);
    else dropped += 1;
  }
  const ids = new Set(kept.map((n) => n.id));
  const edges = (clip?.edges || []).filter(
    (e) => e && ids.has(e.source) && ids.has(e.target),
  );
  return { nodes: kept, edges, dropped };
}

function writeSystemClipboard(payload) {
  try {
    navigator.clipboard?.writeText(JSON.stringify(payload));
  } catch {
    // Clipboard API unavailable/denied - the in-memory snapshot still works.
  }
}

async function readSystemClipboard() {
  try {
    const text = await navigator.clipboard?.readText();
    if (!text) return null;
    const parsed = JSON.parse(text);
    if (parsed?.kind === CLIPBOARD_KIND && Array.isArray(parsed.nodes)) {
      return parsed;
    }
  } catch {
    // Unreadable or foreign clipboard contents - fall back to memory.
  }
  return null;
}

// Selected, copyable nodes - prefer Vue Flow's selection, fall back to the inspector's node.
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
function snapshotSelection() {
  const sel = currentSelection();
  if (!sel.length) return false;
  const ids = new Set(sel.map((n) => n.id));
  clipboard.value = {
    nodes: sel.map((n) => {
      const raw = toRaw(n);
      const pos = raw.position;
      return {
        id: raw.id,
        type: raw.type,
        position: { x: pos?.x ?? 0, y: pos?.y ?? 0 },
        data: snapshotNodeData(raw.data),
      };
    }),
    edges: edges.value
      .filter((e) => ids.has(e.source) && ids.has(e.target))
      .map((e) => {
        const raw = toRaw(e);
        return {
          source: raw.source,
          target: raw.target,
          sourceHandle: raw.sourceHandle,
          targetHandle: raw.targetHandle,
        };
      }),
  };
  return true;
}

function copySelection() {
  if (!snapshotSelection()) return false;
  writeSystemClipboard({ kind: CLIPBOARD_KIND, ...clipboard.value });
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
      data: clonePlain(n.data),
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

async function pasteClipboard() {
  const payload = (await readSystemClipboard()) || clipboard.value;
  const {
    nodes: okNodes,
    edges: okEdges,
    dropped,
  } = validateClipboard(payload);
  if (dropped > 0) {
    displaySonnerError(
      t("marketplace.canvas.pasteSkipped", { count: dropped }),
    );
  }
  return spawnCopies({ nodes: okNodes, edges: okEdges }, { x: 40, y: 40 });
}

function duplicateSelection() {
  if (snapshotSelection()) spawnCopies(clipboard.value, { x: 40, y: 40 });
}

let saveInFlight = null;

/** Saves the canvas. Resolves true when the graph is persisted. */
function save(options = {}) {
  if (saveInFlight) return saveInFlight;
  saveInFlight = persistGraph(options).finally(() => {
    saveInFlight = null;
  });
  return saveInFlight;
}

async function persistGraph({ force = false } = {}) {
  if (errorCount.value > 0) {
    displaySonnerError({
      message: t("marketplace.canvas.fixErrorsBeforeSave"),
    });
    return false;
  }
  saving.value = true;
  try {
    const result = await store.dispatch("saveMappingGraph", {
      connectionId: props.connectionId,
      mappingId: props.mappingId,
      graph: buildGraph(),
      expectedGraphVersion: force ? null : graphVersion,
    });
    graphVersion = result?.graphVersion ?? null;
    dirty.value = false;
    displaySonnerSuccess(t("marketplace.canvas.graphSaved"));
    return true;
  } catch (e) {
    if (e?.response?.status === 409) {
      saving.value = false;
      return resolveSaveConflict();
    }
    displaySonnerError(e);
    return false;
  } finally {
    saving.value = false;
  }
}

async function resolveSaveConflict() {
  const choice = await confirm({
    title: t("marketplace.canvas.saveConflict.title"),
    description: t("marketplace.canvas.saveConflict.description"),
    saveLabel: t("marketplace.canvas.saveConflict.reload"),
    confirmLabel: t("marketplace.canvas.saveConflict.overwrite"),
    cancelLabel: t("common.cancel"),
  });
  if (choice === true) return persistGraph({ force: true });
  if (choice === "save") {
    dirty.value = false;
    await load({ preserveLayout: true });
  }
  return false;
}

let previewTimer = null;
let previewToken = 0;
function schedulePreview() {
  if (previewTimer) clearTimeout(previewTimer);
  if (!props.previewProductId || loading.value) return;
  previewStale.value = true;
  previewOutdated.value = false;
  previewTimer = setTimeout(runPreview, 400);
}

function applyPreviewValues(outputs, nodeValues) {
  previewStale.value = false;
  previewOutdated.value = false;
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
  previewStale.value = false;
  previewOutdated.value = false;
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
  const token = ++previewToken;
  previewing.value = true;
  try {
    const data = await store.dispatch("previewMappingGraph", {
      connectionId: props.connectionId,
      mappingId: props.mappingId,
      productId: props.previewProductId,
      graph: buildGraph(),
    });
    if (token !== previewToken || loading.value) {
      // Aborted before new values landed: loading resets preview state anyway,
      // a newer schedule owns the stale flag otherwise.
      if (loading.value) previewStale.value = false;
      return;
    }
    await waitForPaint();
    const outputs = data?.outputs || {};
    const nodeValues = data?.nodeValues || {};
    applyPreviewValues(outputs, nodeValues);
    emit("preview-result", { outputs, fieldTypes: data?.fieldTypes || {} });
  } catch (e) {
    if (token !== previewToken) return;
    previewStale.value = false;
    previewOutdated.value = true;
    displaySonnerError(e);
  } finally {
    if (token === previewToken) previewing.value = false;
  }
}

function collectMeasuredSizes(nodeList) {
  const map = new Map();
  for (const n of nodeList || []) {
    const h = n.dimensions?.height ?? n.measured?.height;
    const w = n.dimensions?.width ?? n.measured?.width;
    if ((typeof h === "number" && h > 0) || (typeof w === "number" && w > 0)) {
      map.set(n.id, { width: w, height: h });
    }
  }
  return map;
}

// Re-run the layered auto-layout on the current graph.
function autoArrange() {
  const obj = toObject();
  const measured = collectMeasuredSizes(nodes.value);
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
  const blob = new Blob([JSON.stringify(payload, null, 2)], {
    type: "application/json",
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `mapping-${props.mappingId}-${new Date().toISOString().slice(0, 10)}.json`;
  a.click();
  URL.revokeObjectURL(url);
  displaySonnerSuccess(t("marketplace.canvas.mappingExported"));
}

function triggerImportFile() {
  importFileInput.value?.click();
}

defineExpose({
  reload: requestLoad,
  isDirty: () => dirty.value,
  save,
  autoArrange,
  syncAdapterFieldSchema,
  exportMappingFile,
  triggerImportFile,
});

async function importMappingFile(event) {
  const file = event.target?.files?.[0];
  if (event.target) event.target.value = "";
  if (!file) return;
  const ok = await confirm({
    title: t("marketplace.canvas.importConfirm.title"),
    description: t("marketplace.canvas.importConfirm.description"),
    confirmLabel: t("marketplace.canvas.importConfirm.confirm"),
  });
  if (!ok) return;
  try {
    const data = JSON.parse(await file.text());
    const graph = data.graph ?? data;
    if (!graph?.nodes || !Array.isArray(graph.nodes))
      throw new Error(t("marketplace.canvas.invalidFileFormat"));

    const ensured = ensurePriceNode(
      graph.nodes,
      graph.edges || [],
      props.priceFieldMappings,
    );
    const importedNodes = syncAdapterPriceFields(
      ensured.nodes,
      props.priceFieldMappings,
    );
    const { nodes: importSafeNodes } = sanitizeGraphNodes(importedNodes);
    // Cross-cabinet imports can reference attributes/fields that don't exist
    // here: drop those nodes (sinks are structural and always kept).
    const importFieldKeys = new Set([
      ...sinkFieldKeys(importSafeNodes),
      ...sinkFieldKeys(nodes.value),
    ]);
    const usableNodes = importSafeNodes.filter(
      (n) =>
        SINK_NODE_TYPES.has(n.type) || clipboardNodeUsable(n, importFieldKeys),
    );
    const droppedImport = importSafeNodes.length - usableNodes.length;
    const importIds = new Set(usableNodes.map((n) => n.id));
    const { edges: importSafeEdges } = sanitizeGraphEdges(
      ensured.edges,
      importIds,
    );
    setNodes(layoutGraph(usableNodes, importSafeEdges));
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
    displaySonnerSuccess(t("marketplace.canvas.mappingImported"));
    if (droppedImport > 0) {
      displaySonnerError(
        t("marketplace.canvas.pasteSkipped", { count: droppedImport }),
      );
    }
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
      maximized ? 'fixed inset-0 z-50' : 'relative h-full w-full rounded-xl',
    ]"
  >
    <SplitterPanel :min-size="40">
      <div
        ref="flowWrap"
        class="relative min-w-0 h-full w-full"
        @contextmenu="onContextMenu"
      >
        <TooltipProvider :delay-duration="150">
          <div
            class="absolute left-2 top-2 z-10 flex max-w-[calc(100%-1rem)] flex-wrap items-center gap-2"
          >
            <ButtonGroup joined>
              <Tooltip :disabled="!dirty">
                <TooltipTrigger as-child>
                  <Button
                    size="sm"
                    :disabled="saving || errorCount > 0"
                    @click="save()"
                  >
                    <Spinner v-if="saving" />
                    <Save v-else class="size-4" />
                    {{ t("marketplace.canvas.save") }}
                    <span
                      v-if="dirty && !saving"
                      class="size-1.5 rounded-full bg-current opacity-80"
                      aria-hidden="true"
                    />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>
                  {{ t("marketplace.canvas.unsavedChanges") }}
                </TooltipContent>
              </Tooltip>
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button
                    size="sm"
                    class="px-2"
                    :aria-label="t('marketplace.canvas.actions')"
                  >
                    <ChevronDown class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="start" class="w-56">
                  <DropdownMenuItem @select="confirmAutoArrange">
                    <LayoutGrid class="size-4" />
                    {{ t("marketplace.fieldMappingPage.autoArrange") }}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    :disabled="syncingFields"
                    @select="syncAdapterFieldSchema"
                  >
                    <Spinner v-if="syncingFields" class="size-4" />
                    <RefreshCw v-else class="size-4" />
                    {{ t("marketplace.fieldMappingPage.checkFieldsUpToDate") }}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem @select="exportMappingFile">
                    <Download class="size-4" />
                    {{ t("marketplace.fieldMappingPage.export") }}
                  </DropdownMenuItem>
                  <DropdownMenuItem @select="triggerImportFile">
                    <Upload class="size-4" />
                    {{ t("marketplace.fieldMappingPage.import") }}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </ButtonGroup>

            <div
              class="min-w-[20rem] w-[min(36rem,calc(100vw-22rem))] max-w-full"
            >
              <SearchableSelect
                class="h-8 w-full bg-card dark:bg-card"
                content-class="w-[var(--reka-popover-trigger-width)] max-w-[min(40rem,calc(100vw-2rem))]"
                remote
                :loading="previewProductsLoading"
                :options="previewProducts"
                :model-value="previewProductId ? String(previewProductId) : ''"
                @update:modelValue="emit('select-preview', $event)"
                @search="emit('search-preview', $event)"
                :placeholder="t('marketplace.canvas.previewPlaceholder')"
              />
            </div>

            <ButtonGroup v-if="previewProductId">
              <Tooltip>
                <TooltipTrigger as-child>
                  <Button size="sm" as-child>
                    <RouterLink :to="`/products/${previewProductId}/edit`">
                      <ExternalLink class="size-4" />
                      {{ t("marketplace.canvas.openPreviewProduct") }}
                    </RouterLink>
                  </Button>
                </TooltipTrigger>
                <TooltipContent>
                  {{ t("marketplace.canvas.openPreviewProductTitle") }}
                </TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger as-child>
                  <Button
                    size="sm"
                    variant="outline"
                    @click="emit('clear-preview')"
                  >
                    <EyeOff class="size-4" />
                    {{ t("marketplace.canvas.resetPreview") }}
                  </Button>
                </TooltipTrigger>
                <TooltipContent>
                  {{ t("marketplace.canvas.resetPreviewTitle") }}
                </TooltipContent>
              </Tooltip>
            </ButtonGroup>

            <span
              v-if="previewing"
              class="inline-flex items-center gap-1 px-1 text-xs text-muted-foreground"
            >
              <Spinner size="sm" /> {{ t("marketplace.canvas.previewing") }}
            </span>
            <input
              ref="importFileInput"
              type="file"
              accept="application/json,.json"
              class="hidden"
              @change="importMappingFile"
            />
            <Badge v-if="errorCount" variant="destructive" class="gap-1">
              <AlertTriangle class="size-3" /> {{ errorCount }}
            </Badge>
          </div>
        </TooltipProvider>

        <div v-if="debugMode" class="vf-debug-panel">
          <div class="vf-debug-section">
            <b>{{ t("marketplace.canvas.fieldsAnalysis") }}</b>
            <div
              v-for="(v, k) in debugInfo.analysis"
              :key="k"
              :class="['vf-debug-row', v.importable ? 'ok' : 'no']"
            >
              <span class="badge">{{
                v.importable ? "IMPORT" : "EXPORT"
              }}</span>
              {{ k }}: connected={{ v.connected }}, rev={{ v.reversible
              }}<span v-if="v.fanOut" style="color: #f59e0b; margin-left: 4px"
                >⚠ fan-out</span
              >
            </div>
            <div
              v-if="!Object.keys(debugInfo.analysis).length"
              class="text-xs opacity-50"
            >
              {{ t("marketplace.canvas.noFields") }}
            </div>
          </div>
          <div class="vf-debug-section">
            <b
              >{{ t("marketplace.canvas.edges") }} ({{
                debugInfo.edges.length
              }})</b
            >
            <div
              v-for="e in debugInfo.edges"
              :key="e.id"
              :class="[
                'vf-debug-row',
                e.type === 'vf-import'
                  ? 'ok'
                  : e.type === 'vf-export'
                    ? 'no'
                    : '',
              ]"
            >
              <span class="badge">{{ e.type }}</span>
              <span class="truncate">{{ e.src }} → {{ e.tgt }}</span>
            </div>
          </div>
          <div class="vf-debug-hint">
            {{ t("marketplace.canvas.hideDebug") }}
          </div>
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
          :snap-to-grid="snapToGrid"
          :snap-grid="SNAP_GRID"
          class="vf-canvas"
        >
          <Background
            :variant="snapToGrid ? 'lines' : 'dots'"
            :gap="snapToGrid ? SNAP_GRID[0] : 16"
            :size="snapToGrid ? 1 : 1"
            :line-width="1"
            color="var(--vf-grid-color)"
          />
          <HelperLinesOverlay
            v-if="showGuidelines"
            :horizontal="helperLineHorizontal"
            :vertical="helperLineVertical"
          />
          <MiniMap
            pannable
            zoomable
            node-color="var(--muted-foreground)"
            mask-color="var(--muted)"
          />
          <TooltipProvider :delay-duration="150">
            <Controls position="bottom-left" :show-interactive="false">
              <template #control-zoom-in>
                <Tooltip>
                  <TooltipTrigger as-child>
                    <button
                      type="button"
                      class="vue-flow__controls-button vue-flow__controls-zoomin"
                      @click="zoomIn()"
                    >
                      <Plus class="size-3.5" aria-hidden="true" />
                    </button>
                  </TooltipTrigger>
                  <TooltipContent side="right">
                    {{ t("marketplace.canvas.zoomIn") }}
                  </TooltipContent>
                </Tooltip>
              </template>
              <template #control-zoom-out>
                <Tooltip>
                  <TooltipTrigger as-child>
                    <button
                      type="button"
                      class="vue-flow__controls-button vue-flow__controls-zoomout"
                      @click="zoomOut()"
                    >
                      <Minus class="size-3.5" aria-hidden="true" />
                    </button>
                  </TooltipTrigger>
                  <TooltipContent side="right">
                    {{ t("marketplace.canvas.zoomOut") }}
                  </TooltipContent>
                </Tooltip>
              </template>
              <Tooltip>
                <TooltipTrigger as-child>
                  <button
                    class="vue-flow__controls-button"
                    type="button"
                    :aria-pressed="snapToGrid"
                    :style="snapToGrid ? {} : { opacity: 0.4 }"
                    @click="snapToGrid = !snapToGrid"
                  >
                    <!-- Snap-to-grid: cell grid + block on an intersection -->
                    <svg
                      class="vf-ctrl-icon"
                      viewBox="0 0 24 24"
                      aria-hidden="true"
                    >
                      <path
                        class="vf-ctrl-icon__soft"
                        d="M4 9h16M4 15h16M9 4v16M15 4v16"
                      />
                      <rect
                        class="vf-ctrl-icon__solid"
                        x="9"
                        y="9"
                        width="6"
                        height="6"
                        rx="1"
                      />
                    </svg>
                  </button>
                </TooltipTrigger>
                <TooltipContent side="right">
                  {{
                    snapToGrid
                      ? t("marketplace.canvas.disableSnapToGrid")
                      : t("marketplace.canvas.enableSnapToGrid")
                  }}
                </TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger as-child>
                  <button
                    class="vue-flow__controls-button"
                    type="button"
                    :aria-pressed="showGuidelines"
                    :style="showGuidelines ? {} : { opacity: 0.4 }"
                    @click="showGuidelines = !showGuidelines"
                  >
                    <!-- Alignment guides: two blocks + shared center guide -->
                    <svg
                      class="vf-ctrl-icon"
                      viewBox="0 0 24 24"
                      aria-hidden="true"
                    >
                      <path
                        class="vf-ctrl-icon__soft vf-ctrl-icon__dash"
                        d="M12 3v18"
                      />
                      <rect x="4" y="6" width="7" height="5" rx="1" />
                      <rect x="13" y="13" width="7" height="5" rx="1" />
                    </svg>
                  </button>
                </TooltipTrigger>
                <TooltipContent side="right">
                  {{
                    showGuidelines
                      ? t("marketplace.canvas.disableGuidelines")
                      : t("marketplace.canvas.enableGuidelines")
                  }}
                </TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger as-child>
                  <button
                    class="vue-flow__controls-button"
                    type="button"
                    :style="animateWires ? {} : { opacity: 0.4 }"
                    @click="animateWires = !animateWires"
                  >
                    <Activity class="size-3.5" />
                  </button>
                </TooltipTrigger>
                <TooltipContent side="right">
                  {{
                    animateWires
                      ? t("marketplace.canvas.disableWireAnimation")
                      : t("marketplace.canvas.enableWireAnimation")
                  }}
                </TooltipContent>
              </Tooltip>
              <template #control-fit-view>
                <Tooltip>
                  <TooltipTrigger as-child>
                    <button
                      class="vue-flow__controls-button"
                      type="button"
                      @click="toggleMaximize"
                    >
                      <svg
                        v-if="maximized"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="3"
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        aria-hidden="true"
                      >
                        <path d="M8 3v3a2 2 0 0 1-2 2H3" />
                        <path d="M21 8h-3a2 2 0 0 1-2-2V3" />
                        <path d="M3 16h3a2 2 0 0 1 2 2v3" />
                        <path d="M16 21v-3a2 2 0 0 1 2-2h3" />
                      </svg>
                      <svg
                        v-else
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="3"
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        aria-hidden="true"
                      >
                        <path d="M8 3H5a2 2 0 0 0-2 2v3" />
                        <path d="M21 8V5a2 2 0 0 0-2-2h-3" />
                        <path d="M3 16v3a2 2 0 0 0 2 2h3" />
                        <path d="M16 21h3a2 2 0 0 0 2-2v-3" />
                      </svg>
                    </button>
                  </TooltipTrigger>
                  <TooltipContent side="right">
                    {{
                      maximized
                        ? t("marketplace.canvas.collapse")
                        : t("marketplace.canvas.fullscreen")
                    }}
                  </TooltipContent>
                </Tooltip>
              </template>
            </Controls>
          </TooltipProvider>

          <template #edge-vf-import="p"
            ><ImportableEdge
              v-bind="p"
              :debug="debugMode"
              :animate="animateWires"
          /></template>
          <template #edge-vf-export="p"
            ><ExportOnlyEdge
              v-bind="p"
              :debug="debugMode"
              :animate="animateWires"
          /></template>

          <template #node-attribute="p"><SourceNode v-bind="p" /></template>
          <template #node-storeAttribute="p"
            ><SourceNode v-bind="p"
          /></template>
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
          <template #node-add="p"><OpNode v-bind="p" /></template>
          <template #node-subtract="p"><OpNode v-bind="p" /></template>
          <template #node-multiply="p"><OpNode v-bind="p" /></template>
          <template #node-divide="p"><OpNode v-bind="p" /></template>
          <template #node-min="p"><OpNode v-bind="p" /></template>
          <template #node-max="p"><OpNode v-bind="p" /></template>
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
            :adapter-fields-with-options="adapterFieldsWithOptions"
            :adapter-value-field="menuAdapterValueField"
            @add="onMenuAdd"
            @close="closeMenu"
          />
        </div>

        <div
          v-if="loading"
          class="absolute inset-0 z-40 flex flex-col items-center justify-center gap-3 bg-background/60"
          aria-busy="true"
        >
          <Spinner size="xl" class="text-muted-foreground" />
          <p
            class="text-sm text-muted-foreground text-center max-w-sm"
            role="status"
            aria-live="polite"
          >
            {{ loadingStatusText }}
          </p>
        </div>
      </div>
    </SplitterPanel>

    <template v-if="selected">
      <SplitterResizeHandle
        class="w-1 bg-border hover:bg-primary/30 transition-colors cursor-col-resize"
      />
      <SplitterPanel :default-size="25" :min-size="15" :max-size="50">
        <NodeInspector
          :node="selected"
          :external-field-options="selectedExternalOptions"
          :source-field-options="selectedSourceOptions"
          :store-file-options="availableSources.storeFiles || []"
          :adapter-fields-with-options="adapterFieldsWithOptions"
          :ai-model-options="aiModelOptions"
          :available-sources="availableSources"
          :allow-custom-fields="allowCustomFields"
          :template-field-refs="templateFieldRefs"
          :focus-field-key="focusAdapterField"
          :preview-stale="previewStale"
          :preview-outdated="previewOutdated"
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
  /* Soft grid / guide strokes — keep below node chrome contrast */
  --vf-grid-color: var(--border);
  --vf-helper-line: var(--muted-foreground);
}
.vf-canvas .vue-flow__background {
  opacity: 0.55;
}

/* The wide invisible interaction path stays clickable: wires get a fat hit zone.
   Canvas panning still works on empty space and the minimap. */
.vf-canvas .vue-flow__edge-interaction {
  stroke-width: 20;
}

/* `.vf-card.<mod>` (specificity 0,2,0) so these beat the base `.vf-card` rule that appears later. */
.vf-card.vf-card--collapsed {
  min-width: 0;
  max-width: 200px;
  padding: 6px 14px;
  border-radius: 999px;
}
.vf-card--collapsed .vf-card__head {
  gap: 6px;
}
/* Collapsed multi/fixed-input ops keep height so their left input pins still spread out. */
.vf-card--collapsed-multi {
  min-height: 48px;
  display: flex;
  align-items: center;
}

/* Modifiers that expose operand params as override pins: a row per input handle. Metrics must
   match HEADER/ROW in ModifierNode.vue so the left pins line up with their rows. */
.vf-card.vf-card--haspins {
  min-width: 200px;
  padding: 0;
}
.vf-card--haspins .vf-card__head {
  height: 30px;
  padding: 0 12px;
}
.vf-mod__pin-row {
  display: flex;
  align-items: center;
  height: 20px;
  padding: 0 12px 0 16px;
  font-size: 12px;
  color: var(--muted-foreground);
}
.vf-mod__pin-row--value {
  color: var(--foreground);
  font-weight: 500;
}
.vf-mod__body {
  padding: 6px 12px 10px;
}

.vf-card {
  position: relative;
  min-width: 170px;
  max-width: 240px;
  padding: 10px 12px;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border);
  background: var(--card);
  color: var(--foreground);
  box-shadow: 0 1px 3px color-mix(in oklch, var(--foreground) 8%, transparent);
  font-size: 13px;
}
.vf-card--selected {
  border-color: var(--primary);
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
  font-size: 11px;
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
  width: 14px;
  height: 14px;
  color: var(--warning);
}
.vf-card__title {
  margin-top: 3px;
  font-weight: 600;
  font-size: 14px;
  line-height: 1.2;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.vf-mod__unit {
  display: inline-block;
  font-size: 11px;
  font-weight: 500;
  padding: 1px 7px;
  border-radius: var(--radius-md);
  background: var(--muted);
  border: 1px solid var(--border);
  color: var(--foreground);
}
.vf-card__title--sm {
  font-weight: 500;
  font-size: 13px;
  color: var(--muted-foreground);
}

.vf-card__pin {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
}
.vf-card__pin--right {
  justify-content: flex-end;
}
.vf-dot {
  width: 9px;
  height: 9px;
  border-radius: 9999px;
  display: inline-block;
}
.vf-pin-label {
  font-size: 11px;
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

/* Single "adapter fields" sink node - metrics must match HEADER/ROW in AdapterFieldsNode.vue. */
.vf-card--adapter {
  min-width: 320px;
  max-width: 440px;
  padding: 0;
}

.vf-adapter__head {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 34px;
  padding: 0 12px;
  border-bottom: 1px solid var(--border);
}
.vf-adapter__row {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 28px;
  padding: 0 12px;
  font-size: 13px;
}

.vf-adapter__row:hover {
  background: var(--accent);
}
/* Last row's hover fill must not escape the card's rounded bottom corners. */
.vf-adapter__list > :last-child {
  border-radius: 0 0 var(--radius-lg) var(--radius-lg);
}
.vf-adapter__row--add {
  color: var(--muted-foreground);
  border-top: 1px dashed var(--border);
}
.vf-adapter__info {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  align-self: stretch;
  gap: 6px;
}
.vf-adapter__mid {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
  margin-inline: auto;
}
.vf-adapter__pills {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.vf-adapter__unit {
  flex-shrink: 0;
  font-size: 11px;
  font-weight: 500;
  padding: 1px 5px;
  border-radius: var(--radius-md);
  background: var(--muted);
  color: var(--muted-foreground);
  line-height: 1.5;
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
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 11px;
  font-weight: 500;
  padding: 1px 6px;
  border-radius: var(--radius-md);
  border: 1px solid transparent;
  line-height: 1.5;
  cursor: pointer;
}
.vf-adapter__pill:hover {
  opacity: 0.8;
}
/* Merged-duplicate (alias) field: dashed neutral pill "= <primary>" */
.vf-adapter__pill--alias {
  background: var(--muted);
  border-color: var(--border);
  border-style: dashed;
  color: var(--muted-foreground);
}
.vf-adapter__pill--omit {
  background: transparent;
  border-color: var(--border);
  border-style: dotted;
  color: var(--muted-foreground);
  opacity: 0.85;
}
.vf-adapter__dir {
  flex-shrink: 0;
  width: 13px;
  height: 13px;
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
  background: color-mix(in oklch, var(--primary) 12%, transparent);
  border: 1px solid color-mix(in oklch, var(--primary) 70%, transparent);
}

/* Vue Flow handle (pin) sizing — chunky enough to grab, with a hover affordance. */
.vue-flow__handle {
  width: 13px;
  height: 13px;
  border: 2px solid var(--card);
  background: var(--muted-foreground);
  transition:
    box-shadow 0.12s ease,
    background-color 0.12s ease;
}
.vue-flow__handle:hover {
  background: var(--primary);
}

/* Default (static / floating) edges - plain wire, no animation. */
.vf-canvas .vue-flow__edge-path {
  stroke: var(--muted-foreground);
  stroke-width: 3;
  opacity: 0.5;
}
.vf-canvas .vue-flow__edge.selected .vue-flow__edge-path {
  stroke: var(--primary);
  opacity: 0.9;
}

/* Debug overlay panel */
.vf-debug-panel {
  position: absolute;
  top: 8px;
  right: 8px;
  z-index: 100;
  background: color-mix(in oklch, var(--foreground) 92%, transparent);
  color: var(--background);
  font-family: monospace;
  font-size: 11px;
  line-height: 1.5;
  border-radius: var(--radius-md);
  padding: 10px 12px;
  max-width: 400px;
  max-height: 65vh;
  overflow-y: auto;
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
.vf-debug-row.ok .badge {
  background: #14532d;
  color: #4ade80;
}
.vf-debug-row.no .badge {
  background: #7c2d12;
  color: #fb923c;
}
.vf-debug-hint {
  font-size: 9px;
  color: #6b7280;
  text-align: right;
}

/* ---- Controls panel ---- */
.vf-canvas .vue-flow__controls {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  box-shadow: none;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
/* Keep control buttons as flex children when wrapped in shadcn Tooltip */
.vf-canvas .vue-flow__controls [data-slot="tooltip"] {
  display: contents;
}
.vf-canvas .vue-flow__controls-button {
  background: var(--card);
  border: none;
  border-bottom: 1px solid var(--border);
  color: var(--foreground);
  width: 34px;
  height: 34px;
  padding: 8px;
}
.vf-canvas .vue-flow__controls-button:last-child {
  border-bottom: none;
}
.vf-canvas .vue-flow__controls-button:hover {
  background: var(--muted);
}
.vf-canvas .vue-flow__controls-button svg {
  width: 16px;
  max-width: 16px;
  height: 16px;
  max-height: 16px;
  fill: none;
  stroke: currentColor;
}
.vf-canvas .vue-flow__controls-button svg.vf-ctrl-icon {
  fill: none;
  stroke: currentColor;
  stroke-width: 2;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.vf-canvas .vue-flow__controls-button svg.vf-ctrl-icon .vf-ctrl-icon__soft {
  opacity: 0.45;
}
.vf-canvas .vue-flow__controls-button svg.vf-ctrl-icon .vf-ctrl-icon__dash {
  stroke-dasharray: 2.5 2.5;
}
.vf-canvas .vue-flow__controls-button svg.vf-ctrl-icon .vf-ctrl-icon__solid {
  fill: currentColor;
  stroke: none;
  opacity: 1;
}
/* Bottom two buttons: larger icons */
.vf-canvas .vue-flow__controls-button:nth-last-child(-n + 2) {
  width: 30px;
  height: 32px;
}
.vf-canvas .vue-flow__controls-button:nth-last-child(-n + 2) svg {
  width: 16px;
  max-width: 16px;
  height: 16px;
  max-height: 16px;
}

/* ---- MiniMap ---- */
.vf-canvas .vue-flow__minimap {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
  box-shadow: none;
}
.vf-canvas .vue-flow__minimap-mask {
  fill: var(--muted);
  opacity: 0.65;
}
</style>
