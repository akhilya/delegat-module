<script setup>
import { computed, ref, watch, nextTick } from "vue";
import { useI18n } from "vue-i18n";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import ValueMapEditor from "../ValueMapEditor.vue";
import OptionMapEditor from "../OptionMapEditor.vue";
import TemplateEditor from "../TemplateEditor.vue";
import SearchableSelect from "@/components/ui/searchable-select/SearchableSelect.vue";
import AttributeUnitSelect from "@/components/products/AttributeUnitSelect.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { Trash2, X, Plus } from "lucide-vue-next";
import {
  appendCustomAdapterField,
  fieldPinsFor,
  mergeProductFieldSources,
  opLabel,
  opParamLabel,
} from "./graphModel.js";
import {
  localizeExternalFieldName,
  localizeStandardSourceName,
} from "./fieldLabels.js";
import { MODIFIER_CATALOG, MODIFIER_CATEGORIES } from "../modifierCatalog.js";
import { OP_SPECS, OP_NODE_TYPES } from "./graphModel.js";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import AdapterFieldBindingDialog from "./AdapterFieldBindingDialog.vue";

const { t } = useI18n();

// reka-ui SelectItem can't hold an empty-string value; this sentinel is the re-selectable
// "default/none" option (mapped back to "" in the handlers).
const NONE = "__none__";

const pinRef = (id) => `{{pin:${id}}}`;

const props = defineProps({
  node: { type: Object, default: null },
  externalFieldOptions: { type: Array, default: () => [] },
  sourceFieldOptions: { type: Array, default: () => [] },
  storeFileOptions: { type: Array, default: () => [] },
  adapterFieldsWithOptions: { type: Array, default: () => [] },
  aiModelOptions: { type: Array, default: () => [] },
  availableSources: { type: Object, default: () => ({}) },
  templateFieldRefs: { type: Array, default: () => [] },
  focusFieldKey: { type: String, default: null },
});
const emit = defineEmits(["update", "delete", "close", "field-focused"]);

const scrollEl = ref(null);

const highlightedField = ref(null);

watch(
  () => props.focusFieldKey,
  (key) => {
    if (!key) return;
    nextTick(() => {
      const row = scrollEl.value?.querySelector(`[data-fk="${key}"]`);
      row?.scrollIntoView({ behavior: "instant", block: "nearest" });
      // Reset first so animation replays even if same field clicked twice
      highlightedField.value = null;
      nextTick(() => {
        highlightedField.value = key;
        setTimeout(() => {
          highlightedField.value = null;
        }, 700);
      });
      emit("field-focused");
    });
  },
);

const data = computed(() => props.node?.data || {});
const type = computed(() => props.node?.type || "");

const isStoreFile = computed(() =>
  ["storeFileUrl", "storeFileContent"].includes(type.value),
);
const isProductFileByName = computed(() =>
  ["productFileByName", "productFileContentByName"].includes(type.value),
);
const isProductFilesByExt = computed(
  () => type.value === "productFilesByExtension",
);
const fileId = (f) => f.Id ?? f.id;
const pickStoreFile = (id) => {
  if (id === NONE) id = "";
  const f = props.storeFileOptions.find(
    (x) => String(fileId(x)) === String(id),
  );
  emit("update", {
    storeFileId: id === "" || id == null ? null : Number(id),
    label: f?.fileName || t("marketplace.graph.storeFileLabel"),
  });
};
const storeFileValue = computed(() =>
  data.value.storeFileId != null ? String(data.value.storeFileId) : undefined,
);

const modifierCatalog = computed(
  () => MODIFIER_CATALOG[data.value.modifierType],
);
const modifierParams = computed(() => modifierCatalog.value?.params || []);
// valueMap/optionMap editors handle the "default" param themselves - don't render it twice.
const visibleModifierParams = computed(() => {
  const isMap =
    data.value.modifierType === "valueMap" ||
    data.value.modifierType === "optionMap";
  return modifierParams.value.filter((p) => !(isMap && p.key === "default"));
});

function onOptionMapLoadField(f) {
  emit("update", {
    targetFieldKey: f.fieldKey,
    targetFieldOptions: f.options,
    targetValueType: f.valueType,
  });
}
const opSpec = computed(() => OP_SPECS[type.value]);

const isSource = computed(() =>
  ["attribute", "storeAttribute", "productField", "groupName"].includes(
    type.value,
  ),
);
const hasImportableToggle = computed(() =>
  ["attribute", "storeAttribute", "productField"].includes(type.value),
);
const isOp = computed(() => OP_NODE_TYPES.includes(type.value));
const deletable = computed(
  () => type.value !== "adapterFields" && type.value !== "adapterPriceFields",
);

const heading = computed(() => {
  if (!props.node) return "";
  if (type.value === "modifier") {
    const c = modifierCatalog.value;
    const cat = c ? MODIFIER_CATEGORIES[c.category]?.label : "";
    return `${c?.displayName || data.value.modifierType}${cat ? " · " + cat : ""}`;
  }
  if (isOp.value) return opSpec.value ? opLabel(type.value) : type.value;
  if (type.value === "static") {
    const vt = ["number", "boolean", "array", "string"].includes(
      data.value.valueType,
    )
      ? data.value.valueType
      : "default";
    return t(`marketplace.graph.inspector.staticTypeTitles.${vt}`);
  }
  const titleKeys = [
    "attribute",
    "storeAttribute",
    "productField",
    "static",
    "groupName",
    "storeFileUrl",
    "storeFileContent",
    "productFileByName",
    "productFileContentByName",
    "productFilesByExtension",
    "adapterFields",
    "adapterPriceFields",
  ];
  return titleKeys.includes(type.value)
    ? t(`marketplace.graph.inspector.titleByType.${type.value}`)
    : type.value;
});

const fieldsCount = computed(() => (data.value.fields || []).length);
const requiredCount = computed(
  () => (data.value.fields || []).filter((f) => f.isRequired).length,
);

function adapterFieldLabel(f) {
  const base = localizeExternalFieldName(f.fieldKey, f.label || f.fieldKey);
  // Avoid "Name (kg), kg" when label already contains unit
  if (f.unit && !String(base).includes(f.unit)) return `${base}, ${f.unit}`;
  return base;
}

function adapterFieldTooltip(f) {
  const parts = [];
  if (f.description) parts.push(String(f.description).trim());
  if (f.unit) parts.push(t("marketplace.graph.unitLabel", { unit: f.unit }));
  parts.push(t("marketplace.graph.keyLabel", { key: f.fieldKey }));
  return parts.filter(Boolean).join("\n");
}

// Adapter sink: per-field source binding lives here (in settings), not as dropdowns inside the block.
const adapterFields = computed(() => data.value.fields || []);
const adapterAvail = computed(() => data.value.availableSources || {});

const dictionaryAttribute = computed(() => {
  const sources = props.availableSources || {};
  const sameId = (a, b) => a != null && b != null && String(a) === String(b);
  if (type.value === "attribute")
    return sources.categoryAttributes?.find((x) =>
      sameId(x.id, data.value.attributeId),
    );
  if (type.value === "storeAttribute")
    return sources.storeAttributes?.find((x) =>
      sameId(x.id, data.value.attributeId),
    );
  return null;
});

const hasDictionaryKey = computed(() =>
  (dictionaryAttribute.value?.options || []).some(
    (o) => o?.value != null && o?.label && String(o.value) !== String(o.label),
  ),
);

const adapterValueLocked = computed(() => {
  if (data.value.optionSourceKey) return true;
  return Array.isArray(data.value.options) && data.value.options.length > 0;
});

const adapterValueSourceName = computed(() => {
  if (data.value.optionSourceLabel) return data.value.optionSourceLabel;
  const hit = props.adapterFieldsWithOptions.find(
    (f) => f.fieldKey === data.value.optionSourceKey,
  );
  return hit?.label || "";
});
const adapterPreview = computed(() => data.value.preview || {});
const adapterFieldFilter = ref("");
const adapterGroupFilter = ref("__all__");
const adapterGroups = computed(() =>
  [...new Set(adapterFields.value.map((f) => f.group).filter(Boolean))].sort(),
);
watch(adapterGroups, (groups) => {
  if (
    adapterGroupFilter.value !== "__all__" &&
    !groups.includes(adapterGroupFilter.value)
  ) {
    adapterGroupFilter.value = "__all__";
  }
});
const bindingDialogOpen = ref(false);
const bindingDialogField = ref(null);
/** While the binding dialog is open for a field that has no source yet, keep "bind" radio selected. */
const pendingBindKey = ref(null);

function addCustomAdapterField() {
  if (type.value !== "adapterFields") return;
  const { fields } = appendCustomAdapterField(adapterFields.value, {
    label: t("marketplace.graph.customFieldDefault"),
  });
  emit("update", { fields });
}

function removeCustomAdapterField(fieldKey) {
  emit("update", {
    fields: adapterFields.value.filter((f) => f.fieldKey !== fieldKey),
  });
}

function slugCustomKey(raw) {
  return String(raw || "")
    .trim()
    .replace(/\s+/g, "_")
    .replace(/[^a-zA-Z0-9._-]/g, "");
}

function commitCustomName(field, raw) {
  const label = String(raw || "").trim() || field.fieldKey;
  let key = slugCustomKey(label) || field.fieldKey;
  if (key === "__add__") key = field.fieldKey;
  if (
    key !== field.fieldKey &&
    adapterFields.value.some((f) => f.fieldKey === key)
  ) {
    key = field.fieldKey;
  }
  emit("update", {
    fields: adapterFields.value.map((f) =>
      f.fieldKey === field.fieldKey ? { ...f, fieldKey: key, label } : f,
    ),
  });
}

const filteredAdapterFields = computed(() => {
  const q = adapterFieldFilter.value.trim().toLowerCase();
  const list = adapterFields.value;
  return list.filter((f) => {
    const hay =
      `${f.label || ""} ${f.fieldKey || ""} ${f.description || ""}`.toLowerCase();
    return (
      hay.includes(q) &&
      (adapterGroupFilter.value === "__all__" ||
        f.group === adapterGroupFilter.value)
    );
  });
});

const adapterSourceValue = (f) => {
  if (f.aliasOf) return `m:${f.aliasOf}`;
  const s = f.source;
  if (!s) return "";
  if (s.type === "groupName") return "g";
  if (s.type === "productField") return `p:${s.field}`;
  if (s.type === "attribute") return `a:${s.attributeId}`;
  if (s.type === "storeAttribute") return `s:${s.attributeId}`;
  return "";
};

const fieldMode = (f) => {
  if (pendingBindKey.value === f.fieldKey) return "bind";
  if (f.omit) return "omit";
  if (f.source || f.aliasOf) return "bind";
  return "wire";
};

const setAdapterSource = (fieldKey, value) =>
  data.value.setFieldSource?.(fieldKey, value === NONE ? "" : value);

const setAdapterMode = (fieldKey, mode) =>
  data.value.setFieldMode?.(fieldKey, mode);

function onFieldModeChange(f, mode) {
  if (mode === "bind") {
    pendingBindKey.value = f.fieldKey;
    openBindingDialog(f);
    return;
  }
  pendingBindKey.value = null;
  setAdapterMode(f.fieldKey, mode);
}

function openBindingDialog(f) {
  bindingDialogField.value = f;
  pendingBindKey.value = f.fieldKey;
  bindingDialogOpen.value = true;
}

function onBindingDialogOpen(open) {
  bindingDialogOpen.value = open;
  if (!open) pendingBindKey.value = null;
}

function onBindingSelect(value) {
  const f = bindingDialogField.value;
  if (!f) return;
  pendingBindKey.value = null;
  setAdapterSource(f.fieldKey, value);
}

const currentSourceLabel = (f) => {
  if (f.omit) return t("marketplace.graph.inspector.modeOmit");
  if (f.aliasOf) {
    const primary = adapterFields.value.find((x) => x.fieldKey === f.aliasOf);
    return t("marketplace.graph.inspector.aliasEquals", {
      primary: primary?.label || f.aliasOf,
    });
  }
  const s = f.source;
  if (!s) return t("marketplace.graph.inspector.byWire");
  if (s.type === "groupName") return t("marketplace.graph.groupNameLabel");
  if (s.type === "productField")
    return localizeStandardSourceName(
      s.field,
      mergeProductFieldSources(adapterAvail.value).find(
        (x) => x.key === s.field,
      )?.displayName ||
        s.field ||
        t("marketplace.graph.inspector.productField"),
    );
  if (s.type === "attribute")
    return (
      adapterAvail.value.categoryAttributes?.find((x) => x.id === s.attributeId)
        ?.name ||
      t("marketplace.graph.attributeFallback", { id: s.attributeId })
    );
  if (s.type === "storeAttribute")
    return (
      adapterAvail.value.storeAttributes?.find((x) => x.id === s.attributeId)
        ?.name ||
      t("marketplace.graph.attributeFallback", { id: s.attributeId })
    );
  return t("marketplace.graph.inspector.byWire");
};

const setData = (patch) => emit("update", patch);
const setParam = (key, value) =>
  emit("update", { params: { ...(data.value.params || {}), [key]: value } });
const paramValue = (p) => {
  const v = data.value.params?.[p.key];
  if (v != null && v !== "") return v;
  return p.default ?? "";
};

const numberOrUndefined = (v) =>
  v === "" || v == null ? undefined : Number(v);
const splitTags = (v) =>
  (v || "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
const joinTags = (v) => (Array.isArray(v) ? v.join(", ") : v || "");

// --- "standard value" (isAdapterValue static node) ---

function onAdapterValuePick(val) {
  const opt = (data.value.options || []).find((o) => o.value === val);
  emit("update", { staticValue: val, label: opt?.label ?? val });
}

function onAdapterFieldPick(fieldKey) {
  if (adapterValueLocked.value) return;
  const f = props.adapterFieldsWithOptions.find((f) => f.fieldKey === fieldKey);
  if (f)
    emit("update", {
      options: f.options,
      valueType: f.valueType,
      staticValue: "",
      label: "",
      optionSourceKey: f.fieldKey,
      optionSourceLabel: f.label,
    });
}

const selectedArrayValues = computed(() => {
  const v = data.value.staticValue;
  return v
    ? String(v)
        .split(",")
        .map((s) => s.trim())
        .filter(Boolean)
    : [];
});

function toggleArrayValue(val) {
  const cur = selectedArrayValues.value;
  const next = cur.includes(val) ? cur.filter((v) => v !== val) : [...cur, val];
  const keys = next.join(",");
  const labels = next
    .map(
      (k) => (data.value.options || []).find((o) => o.value === k)?.label ?? k,
    )
    .join(", ");
  emit("update", { staticValue: keys, label: labels });
}

const isLlmGenerate = computed(
  () => type.value === "modifier" && data.value.modifierType === "llmGenerate",
);
const llmFieldPins = computed(() => fieldPinsFor(data.value));

function slugPinId(label, existing) {
  const base =
    String(label || "pin")
      .toLowerCase()
      .replace(/[^a-z0-9\u0400-\u04FF]+/gi, "_")
      .replace(/^_+|_+$/g, "") || "pin";
  let id = base;
  let n = 2;
  const taken = new Set((existing || []).map((p) => p.id));
  while (taken.has(id)) {
    id = `${base}_${n++}`;
  }
  return id;
}

function addFieldPin() {
  const pins = [...llmFieldPins.value];
  const label = `${t("marketplace.graph.inspector.pinDefaultLabel")} ${pins.length + 1}`;
  pins.push({ id: slugPinId(label, pins), label });
  emit("update", { fieldPins: pins });
}

function updateFieldPin(index, patch) {
  const pins = llmFieldPins.value.map((p, i) =>
    i === index ? { ...p, ...patch } : p,
  );
  emit("update", { fieldPins: pins });
}

function removeFieldPin(index) {
  const pins = llmFieldPins.value.filter((_, i) => i !== index);
  emit("update", { fieldPins: pins });
}

const templatePinPlaceholders = computed(() =>
  llmFieldPins.value.map((pin) => ({
    label: pin.label || pin.id,
    snippet: `{{pin:${pin.id}}}`,
  })),
);

// --- Adapter field preview: resolve key → label for select/multiselect fields ---

function resolvePreview(fieldKey, raw) {
  const opts = data.value.fieldOptionsMap?.[fieldKey];
  if (!opts?.length || raw == null) return raw;
  return String(raw)
    .split(",")
    .map((k) => opts.find((o) => o.value === k.trim())?.label ?? k.trim())
    .join(", ");
}
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <div v-if="node" class="flex h-full w-full flex-col border-l bg-card">
      <div class="flex items-center justify-between gap-2 border-b px-3 py-2">
        <div class="truncate text-sm font-semibold">{{ heading }}</div>
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          class="text-muted-foreground hover:text-foreground"
          :aria-label="t('common.close')"
          @click="emit('close')"
        >
          <X class="size-4" />
        </Button>
      </div>

      <div
        ref="scrollEl"
        class="min-h-0 flex-1 space-y-4 overflow-y-auto p-3 thin-scrollbar"
      >
        <!-- Store shared-storage file picker (URL / text content) -->
        <template v-if="isStoreFile">
          <div class="space-y-1.5">
            <Label>{{
              t("marketplace.graph.inspector.storeFileFieldLabel")
            }}</Label>
            <Select
              :model-value="storeFileValue"
              @update:model-value="pickStoreFile($event)"
            >
              <SelectTrigger class="h-9 w-full">
                <SelectValue
                  :placeholder="
                    t('marketplace.graph.inspector.chooseFilePlaceholder')
                  "
                />
              </SelectTrigger>
              <SelectContent>
                <SelectItem :value="NONE">{{
                  t("marketplace.graph.inspector.notSelected")
                }}</SelectItem>
                <SelectItem
                  v-for="f in storeFileOptions"
                  :key="fileId(f)"
                  :value="String(fileId(f))"
                >
                  {{ f.fileName }}
                </SelectItem>
              </SelectContent>
            </Select>
            <p class="text-xs text-muted-foreground">
              {{
                type === "storeFileUrl"
                  ? t("marketplace.graph.inspector.storeFileUrlHint")
                  : t("marketplace.graph.inspector.storeFileContentHint")
              }}
            </p>
            <p
              v-if="!storeFileOptions.length"
              class="text-xs text-muted-foreground"
            >
              {{ t("marketplace.graph.inspector.noStoreFiles") }}
            </p>
          </div>
        </template>

        <!-- Product file by name (URL / text content) -->
        <template v-else-if="isProductFileByName">
          <div class="space-y-1.5">
            <Label>{{
              t("marketplace.graph.inspector.productFileNameLabel")
            }}</Label>
            <Input
              :model-value="data.fileName"
              :placeholder="
                t('marketplace.graph.inspector.productFileNamePlaceholder')
              "
              @update:model-value="
                setData({
                  fileName: $event,
                  label:
                    $event || t('marketplace.graph.productFileByNameLabel'),
                })
              "
            />
            <p class="text-xs text-muted-foreground">
              {{
                type === "productFileByName"
                  ? t("marketplace.graph.inspector.productFileByNameHint")
                  : t(
                      "marketplace.graph.inspector.productFileContentByNameHint",
                    )
              }}
            </p>
          </div>
        </template>

        <!-- Product files by extension (array of URLs) -->
        <template v-else-if="isProductFilesByExt">
          <div class="space-y-1.5">
            <Label>{{ t("marketplace.graph.inspector.extensionLabel") }}</Label>
            <Input
              :model-value="data.fileExtension"
              :placeholder="
                t('marketplace.graph.inspector.extensionPlaceholder')
              "
              @update:model-value="
                setData({
                  fileExtension: $event,
                  label: $event
                    ? t('marketplace.graph.productFilesByExtensionPrefix') +
                      $event
                    : t('marketplace.graph.productFilesByExtensionLabel'),
                })
              "
            />
            <p class="text-xs text-muted-foreground">
              {{ t("marketplace.graph.inspector.productFilesByExtensionHint") }}
            </p>
          </div>
        </template>

        <!-- Source: read-only binding -->
        <template v-else-if="isSource">
          <div class="space-y-1">
            <Label>{{ t("marketplace.graph.inspector.sourceLabel") }}</Label>
            <div class="rounded-md border bg-muted/40 px-2.5 py-1.5 text-sm">
              {{ data.label || "-" }}
            </div>
            <p class="text-xs text-muted-foreground">
              {{ t("marketplace.graph.inspector.sourceChangeHint") }}
            </p>
          </div>
          <div v-if="hasDictionaryKey" class="space-y-1.5">
            <div class="flex items-center gap-2">
              <span
                class="text-xs"
                :class="
                  data.emitLabel === true
                    ? 'text-muted-foreground'
                    : 'font-medium'
                "
              >
                {{ t("marketplace.graph.inspector.dictionaryKey") }}
              </span>
              <Switch
                :model-value="data.emitLabel === true"
                @update:model-value="setData({ emitLabel: $event })"
              />
              <span
                class="text-xs"
                :class="
                  data.emitLabel === true
                    ? 'font-medium'
                    : 'text-muted-foreground'
                "
              >
                {{ t("marketplace.graph.inspector.dictionaryValue") }}
              </span>
            </div>
            <p class="text-[11px] text-muted-foreground">
              {{ t("marketplace.graph.inspector.dictionarySideHint") }}
            </p>
          </div>
          <div v-if="hasImportableToggle" class="space-y-1.5">
            <div class="flex items-center gap-2">
              <Switch
                :model-value="data.importable !== false"
                @update:model-value="setData({ importable: $event })"
              />
              <Label
                class="cursor-pointer font-normal"
                @click="
                  setData({
                    importable: data.importable !== false ? false : true,
                  })
                "
              >
                {{ t("marketplace.graph.inspector.importableLabel") }}
              </Label>
            </div>
            <p class="text-[11px] text-muted-foreground">
              {{ t("marketplace.graph.inspector.importableHint") }}
            </p>
          </div>
        </template>

        <!-- "standard value" - static node with predefined options from adapter field -->
        <template v-else-if="type === 'static' && data.isAdapterValue">
          <div v-if="!adapterValueLocked" class="space-y-1.5">
            <Label>{{
              t("marketplace.graph.inspector.dictionarySourceLabel")
            }}</Label>
            <Select @update:model-value="onAdapterFieldPick($event)">
              <SelectTrigger class="h-9 text-xs">
                <SelectValue
                  :placeholder="
                    t('marketplace.graph.inspector.chooseDictionarySource')
                  "
                />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="f in adapterFieldsWithOptions"
                  :key="f.fieldKey"
                  :value="f.fieldKey"
                  >{{ f.label }}</SelectItem
                >
              </SelectContent>
            </Select>
            <p class="text-[11px] text-muted-foreground">
              {{ t("marketplace.graph.inspector.dictionarySourceHint") }}
            </p>
          </div>
          <div v-else class="space-y-1.5">
            <Label>{{
              t("marketplace.graph.inspector.dictionarySourceLabel")
            }}</Label>
            <div class="rounded-md border bg-muted/40 px-2.5 py-1.5 text-sm">
              {{
                adapterValueSourceName ||
                t("marketplace.graph.inspector.dictionarySourceLocked")
              }}
            </div>
            <p class="text-[11px] text-muted-foreground">
              {{ t("marketplace.graph.inspector.dictionarySourceLockedHint") }}
            </p>

            <Label>{{ t("marketplace.graph.inspector.valueLabel") }}</Label>

            <template v-if="data.options?.length && data.valueType !== 'array'">
              <SearchableSelect
                :options="data.options"
                :model-value="data.staticValue"
                :placeholder="
                  t('marketplace.graph.inspector.choosePlaceholder')
                "
                @update:model-value="onAdapterValuePick($event)"
              />
            </template>

            <template
              v-else-if="data.options?.length && data.valueType === 'array'"
            >
              <div
                class="max-h-52 overflow-y-auto rounded border p-2 space-y-1.5"
              >
                <label
                  v-for="opt in data.options"
                  :key="opt.value"
                  class="flex cursor-pointer items-center gap-2 text-xs"
                >
                  <Checkbox
                    :model-value="selectedArrayValues.includes(opt.value)"
                    @update:model-value="toggleArrayValue(opt.value)"
                  />
                  <span>{{ opt.label }}</span>
                </label>
              </div>
            </template>

            <p v-else class="text-[11px] text-muted-foreground">
              {{ t("marketplace.graph.inspector.noDictionary") }}
            </p>
          </div>
        </template>

        <!-- Static / typed literal (Number / Text / Boolean / List) -->
        <template v-else-if="type === 'static'">
          <div class="space-y-1.5">
            <Label>{{ t("marketplace.graph.inspector.valueLabel") }}</Label>

            <Input
              v-if="data.valueType === 'number'"
              type="number"
              :model-value="data.staticValue"
              @update:model-value="
                setData({
                  staticValue: String($event ?? ''),
                  label: String($event ?? ''),
                })
              "
            />

            <div
              v-else-if="data.valueType === 'boolean'"
              class="flex items-center gap-2"
            >
              <Switch
                :model-value="
                  data.staticValue === 'true' || data.staticValue === true
                "
                @update:model-value="
                  setData({
                    staticValue: $event ? 'true' : 'false',
                    label: $event
                      ? t('marketplace.graph.inspector.yes')
                      : t('marketplace.graph.inspector.no'),
                  })
                "
              />
              <span class="text-sm text-muted-foreground">
                {{
                  data.staticValue === "true" || data.staticValue === true
                    ? t("marketplace.graph.inspector.yes")
                    : t("marketplace.graph.inspector.no")
                }}
              </span>
            </div>

            <Input
              v-else-if="data.valueType === 'array'"
              :model-value="data.staticValue"
              :placeholder="t('marketplace.graph.inspector.commaSeparated')"
              @update:model-value="
                setData({ staticValue: $event, label: $event })
              "
            />

            <Input
              v-else
              :model-value="data.staticValue"
              @update:model-value="
                setData({ staticValue: $event, label: $event })
              "
            />
          </div>
        </template>

        <!-- Adapter fields: mode radios + binding dialog -->
        <template
          v-else-if="type === 'adapterFields' || type === 'adapterPriceFields'"
        >
          <div class="space-y-3">
            <div
              class="rounded-md border bg-muted/40 px-2.5 py-1.5 text-xs text-muted-foreground"
            >
              {{ t("marketplace.graph.inspector.fieldsCount") }}
              <b class="text-foreground">{{ fieldsCount }}</b> ·
              {{ t("marketplace.graph.inspector.requiredCount") }}
              <b class="text-foreground">{{ requiredCount }}</b>
            </div>
            <p class="text-xs text-muted-foreground">
              {{ t("marketplace.graph.inspector.adapterFieldsHint") }}
            </p>
            <Input
              v-model="adapterFieldFilter"
              type="search"
              :placeholder="
                t('marketplace.graph.inspector.fieldSearchPlaceholder')
              "
              class="h-8"
            />

            <Select v-if="adapterGroups.length" v-model="adapterGroupFilter">
              <SelectTrigger class="h-8"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">{{
                  t("marketplace.graph.allGroups")
                }}</SelectItem>
                <SelectItem
                  v-for="group in adapterGroups"
                  :key="group"
                  :value="group"
                  >{{ group }}</SelectItem
                >
              </SelectContent>
            </Select>
            <div class="space-y-3">
              <div
                v-for="f in filteredAdapterFields"
                :key="f.fieldKey"
                :data-fk="f.fieldKey"
                :class="[
                  'space-y-1.5 rounded-md border border-transparent px-1 py-1',
                  highlightedField === f.fieldKey && 'vf-field-flash',
                ]"
              >
                <div class="min-w-0">
                  <div
                    v-if="f.isCustom && type === 'adapterFields'"
                    data-row-actions
                    class="flex items-center gap-1"
                  >
                    <Input
                      class="h-7 text-xs"
                      :model-value="f.label || f.fieldKey"
                      :placeholder="
                        t('marketplace.graph.customFieldLabelPlaceholder')
                      "
                      @blur="commitCustomName(f, $event.target.value)"
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      reveal-on-row-hover
                      class="size-7 shrink-0 text-muted-foreground hover:text-destructive"
                      :aria-label="t('marketplace.graph.removeCustomField')"
                      @click="removeCustomAdapterField(f.fieldKey)"
                    >
                      <Trash2 class="size-3.5" />
                    </Button>
                  </div>
                  <Tooltip v-else>
                    <TooltipTrigger as-child>
                      <Label class="text-xs">
                        {{ adapterFieldLabel(f)
                        }}<span v-if="f.isRequired" class="text-destructive">
                          *</span
                        >
                      </Label>
                    </TooltipTrigger>
                    <TooltipContent class="max-w-72 whitespace-pre-line">
                      {{ adapterFieldTooltip(f) }}
                    </TooltipContent>
                  </Tooltip>
                  <Tooltip v-if="f.description">
                    <TooltipTrigger as-child>
                      <p
                        class="text-[11px] leading-snug text-muted-foreground line-clamp-1"
                      >
                        {{ f.description }}
                      </p>
                    </TooltipTrigger>
                    <TooltipContent class="max-w-72 whitespace-pre-line">
                      {{ adapterFieldTooltip(f) }}
                    </TooltipContent>
                  </Tooltip>
                </div>

                <RadioGroup
                  :model-value="fieldMode(f)"
                  class="grid gap-1"
                  @update:model-value="onFieldModeChange(f, $event)"
                >
                  <label
                    v-if="!f.isRequired || f.omit"
                    class="flex cursor-pointer items-center gap-2 rounded-md px-1 py-0.5 text-xs hover:bg-accent/60"
                    :for="`af-mode-omit-${f.fieldKey}`"
                  >
                    <RadioGroupItem
                      :id="`af-mode-omit-${f.fieldKey}`"
                      value="omit"
                    />
                    <span>{{ t("marketplace.graph.inspector.modeOmit") }}</span>
                  </label>
                  <label
                    class="flex cursor-pointer items-center gap-2 rounded-md px-1 py-0.5 text-xs hover:bg-accent/60"
                    :for="`af-mode-wire-${f.fieldKey}`"
                  >
                    <RadioGroupItem
                      :id="`af-mode-wire-${f.fieldKey}`"
                      value="wire"
                    />
                    <span>{{ t("marketplace.graph.inspector.modeWire") }}</span>
                  </label>
                  <label
                    class="flex cursor-pointer items-center gap-2 rounded-md px-1 py-0.5 text-xs hover:bg-accent/60"
                    :for="`af-mode-bind-${f.fieldKey}`"
                  >
                    <RadioGroupItem
                      :id="`af-mode-bind-${f.fieldKey}`"
                      value="bind"
                    />
                    <span>{{ t("marketplace.graph.inspector.modeBind") }}</span>
                  </label>
                </RadioGroup>

                <div v-if="fieldMode(f) === 'bind'" class="pl-1 text-xs">
                  <span class="text-muted-foreground">{{
                    currentSourceLabel(f)
                  }}</span>
                  <Button
                    type="button"
                    variant="link"
                    size="xs"
                    class="ml-1.5 h-auto p-0 text-primary"
                    @click="openBindingDialog(f)"
                  >
                    {{
                      f.source || f.aliasOf
                        ? t("marketplace.graph.inspector.changeBinding")
                        : t("marketplace.graph.inspector.chooseBinding")
                    }}
                  </Button>
                </div>

                <Tooltip v-if="adapterPreview[f.fieldKey] != null">
                  <TooltipTrigger as-child>
                    <div
                      class="truncate rounded bg-muted px-2 py-0.5 font-mono text-[11px] text-muted-foreground"
                    >
                      {{
                        resolvePreview(f.fieldKey, adapterPreview[f.fieldKey])
                      }}
                    </div>
                  </TooltipTrigger>
                  <TooltipContent
                    class="max-w-80 whitespace-pre-wrap break-words font-mono"
                  >
                    {{
                      String(
                        resolvePreview(f.fieldKey, adapterPreview[f.fieldKey]),
                      )
                    }}
                  </TooltipContent>
                </Tooltip>
              </div>

              <p
                v-if="!filteredAdapterFields.length"
                class="py-4 text-center text-xs text-muted-foreground"
              >
                {{ t("marketplace.graph.inspector.fieldSearchEmpty") }}
              </p>
            </div>

            <Button
              v-if="type === 'adapterFields'"
              type="button"
              variant="outline"
              size="sm"
              class="h-8 w-full text-xs"
              @click="addCustomAdapterField"
            >
              <Plus class="size-3.5" />
              {{ t("marketplace.graph.addCustomField") }}
            </Button>

            <AdapterFieldBindingDialog
              :open="bindingDialogOpen"
              :field="bindingDialogField"
              :available-sources="adapterAvail"
              :adapter-fields="adapterFields"
              :current-value="
                bindingDialogField ? adapterSourceValue(bindingDialogField) : ''
              "
              @update:open="onBindingDialogOpen"
              @select="onBindingSelect"
            />
          </div>
        </template>

        <!-- Operation nodes (formatString / join / array ops) -->
        <template v-else-if="isOp">
          <div v-for="p in opSpec.params" :key="p.key" class="space-y-1.5">
            <Label>{{ opParamLabel(type, p.key) }}</Label>
            <Input
              v-if="p.type === 'number'"
              type="number"
              :model-value="data[p.key]"
              @update:model-value="
                setData({ [p.key]: numberOrUndefined($event) })
              "
            />
            <Input
              v-else
              :model-value="data[p.key]"
              @update:model-value="setData({ [p.key]: $event })"
            />
          </div>
          <p v-if="!opSpec.params.length" class="text-xs text-muted-foreground">
            {{ t("marketplace.graph.inspector.noParams") }}
          </p>
        </template>

        <!-- Modifier params -->
        <template v-else-if="type === 'modifier'">
          <div v-if="data.modifierType === 'unitConversion'" class="space-y-3">
            <Label class="flex items-center gap-2">
              <Checkbox
                :model-value="data.params?.autoDetect !== false"
                @update:model-value="setParam('autoDetect', $event === true)"
              />
              {{ t("marketplace.graph.unitConversion.autoDetect") }}
            </Label>
            <div
              v-for="key in ['fromUnit', 'toUnit']"
              :key="key"
              class="space-y-1.5"
            >
              <Label :for="`conversion-${key}`">{{
                t(`marketplace.graph.unitConversion.${key}`)
              }}</Label>
              <AttributeUnitSelect
                :id="`conversion-${key}`"
                :model-value="data.params?.[key] || ''"
                :placeholder="t(`marketplace.graph.unitConversion.${key}`)"
                :disabled="data.params?.autoDetect !== false"
                @update:model-value="setParam(key, $event)"
              />
            </div>
            <p class="text-xs text-muted-foreground">
              {{
                t("marketplace.graph.unitConversion.inspectorHint", {
                  from: data.params?.fromUnit || "?",
                  to: data.params?.toUnit || "?",
                })
              }}
            </p>
          </div>
          <div
            v-if="isLlmGenerate"
            class="mb-4 space-y-2 rounded-md border p-3"
          >
            <Label class="text-xs font-medium">{{
              t("marketplace.graph.inspector.additionalInputs")
            }}</Label>
            <p class="text-[11px] text-muted-foreground">
              {{ t("marketplace.graph.inspector.llmPinsHint") }}
              <code v-pre class="font-mono text-[10px]">{{pin:id}}</code
              >.
            </p>
            <div
              v-for="(pin, idx) in llmFieldPins"
              data-row-actions
              :key="pin.id"
              class="flex items-center gap-1"
            >
              <Input
                class="h-8 flex-1 text-xs"
                :model-value="pin.label"
                :placeholder="
                  t('marketplace.graph.inspector.pinNamePlaceholder')
                "
                @update:model-value="updateFieldPin(idx, { label: $event })"
              />
              <Tooltip>
                <TooltipTrigger as-child>
                  <span
                    class="shrink-0 font-mono text-[10px] text-muted-foreground"
                    >{{ pin.id }}</span
                  >
                </TooltipTrigger>
                <TooltipContent> {{ pinRef(pin.id) }} </TooltipContent>
              </Tooltip>
              <Button
                variant="ghost"
                size="icon"
                reveal-on-row-hover
                class="size-8 shrink-0 text-destructive hover:bg-destructive/10 hover:text-destructive"
                @click="removeFieldPin(idx)"
              >
                <Trash2 class="size-3.5" />
              </Button>
            </div>
            <Button
              variant="outline"
              size="sm"
              class="h-7 w-full text-xs"
              @click="addFieldPin"
            >
              <Plus class="size-3 mr-1" />
              {{ t("marketplace.graph.inspector.addPin") }}
            </Button>
          </div>

          <div
            v-for="p in visibleModifierParams"
            :key="p.key"
            class="space-y-1.5"
          >
            <Label class="flex items-center gap-1.5">
              {{ p.label }}
              <Tooltip v-if="p.type === 'number' || p.type === 'text'">
                <TooltipTrigger as-child>
                  <span class="text-[10px] font-normal text-muted-foreground">
                    · {{ t("marketplace.graph.inspector.pinOverrideBadge") }}
                  </span>
                </TooltipTrigger>
                <TooltipContent>
                  {{ t("marketplace.graph.inspector.pinOverrideHint") }}
                </TooltipContent>
              </Tooltip>
            </Label>

            <Input
              v-if="p.type === 'number'"
              type="number"
              :model-value="data.params?.[p.key]"
              @update:model-value="setParam(p.key, numberOrUndefined($event))"
            />

            <div v-else-if="p.type === 'aiModel'" class="space-y-1.5">
              <Select
                :model-value="
                  data.params?.[p.key] != null
                    ? String(data.params[p.key])
                    : undefined
                "
                @update:model-value="
                  setParam(p.key, $event ? Number($event) : undefined)
                "
              >
                <SelectTrigger class="h-9 w-full">
                  <SelectValue
                    :placeholder="
                      t('marketplace.graph.inspector.chooseModelPlaceholder')
                    "
                  />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="m in aiModelOptions"
                    :key="m.value"
                    :value="String(m.value)"
                  >
                    {{ m.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <p
                v-if="!aiModelOptions.length"
                class="text-[11px] text-muted-foreground"
              >
                {{ t("marketplace.graph.inspector.noAiModelsHint") }}
              </p>
            </div>

            <Select
              v-else-if="p.type === 'select'"
              :model-value="data.params?.[p.key] ?? p.default"
              @update:model-value="setParam(p.key, $event)"
            >
              <SelectTrigger class="h-9 w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="o in p.options"
                  :key="o.value"
                  :value="o.value"
                >
                  {{ o.label }}
                </SelectItem>
              </SelectContent>
            </Select>

            <div
              v-else-if="p.type === 'checkbox'"
              class="flex items-center gap-2"
            >
              <Switch
                :model-value="!!data.params?.[p.key]"
                @update:model-value="setParam(p.key, $event)"
              />
              <span class="text-sm text-muted-foreground">{{
                data.params?.[p.key]
                  ? t("marketplace.graph.inspector.yes")
                  : t("marketplace.graph.inspector.no")
              }}</span>
            </div>

            <TemplateEditor
              v-else-if="p.type === 'template'"
              :model-value="paramValue(p)"
              :available-sources="availableSources"
              :extra-placeholders="isLlmGenerate ? templatePinPlaceholders : []"
              @update:modelValue="(v) => setParam(p.key, v)"
            />

            <Input
              v-else-if="p.type === 'tagInput'"
              :model-value="joinTags(data.params?.[p.key])"
              :placeholder="t('marketplace.graph.inspector.commaSeparated')"
              @update:model-value="setParam(p.key, splitTags($event))"
            />

            <ValueMapEditor
              v-else-if="p.type === 'valueMap'"
              :mappings="data.params?.[p.key] || []"
              :external-field-options="externalFieldOptions"
              :source-field-options="sourceFieldOptions"
              :default-value="data.params?.default || ''"
              @update:mappings="(v) => setParam(p.key, v)"
              @update:defaultValue="(v) => setParam('default', v)"
            />

            <OptionMapEditor
              v-else-if="p.type === 'optionMap'"
              :mappings="data.params?.[p.key] || []"
              :external-field-options="externalFieldOptions"
              :source-field-options="sourceFieldOptions"
              :persisted-target-options="data.targetFieldOptions || []"
              :adapter-fields-with-options="adapterFieldsWithOptions"
              :default-value="data.params?.default || ''"
              @update:mappings="(v) => setParam(p.key, v)"
              @update:defaultValue="(v) => setParam('default', v)"
              @load-field-options="onOptionMapLoadField"
            />

            <Input
              v-else
              :model-value="data.params?.[p.key]"
              @update:model-value="setParam(p.key, $event)"
            />
          </div>
          <p
            v-if="
              !modifierParams.length && data.modifierType !== 'unitConversion'
            "
            class="text-xs text-muted-foreground"
          >
            {{ t("marketplace.graph.inspector.noModifierParams") }}
          </p>
        </template>
      </div>

      <div v-if="data.previewValue != null" class="border-t px-3 py-2">
        <div class="text-xs text-muted-foreground">
          {{ t("marketplace.graph.inspector.outputValueLabel") }}
        </div>
        <div class="mt-0.5 break-words font-mono text-xs">
          {{ data.previewValue }}
        </div>
      </div>

      <div v-if="deletable" class="border-t p-2">
        <Button
          variant="destructive"
          size="sm"
          class="w-full"
          @click="emit('delete')"
        >
          <Trash2 class="mr-2 size-4" />
          {{ t("marketplace.graph.inspector.deleteNode") }}
          <span class="ml-1 text-xs opacity-80">(Delete)</span>
        </Button>
      </div>
    </div>
  </TooltipProvider>
</template>

<style scoped>
@keyframes vf-flash {
  0% {
    background-color: rgba(120, 120, 120, 0.18);
  }
  100% {
    background-color: rgba(120, 120, 120, 0);
  }
}
.vf-field-flash {
  animation: vf-flash 0.9s ease-out forwards;
}
</style>
