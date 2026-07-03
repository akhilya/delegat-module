<script setup>
import { computed, ref, watch, nextTick } from "vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Button from "@/components/ui/button/Button.vue";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import ValueMapEditor from "../ValueMapEditor.vue";
import OptionMapEditor from "../OptionMapEditor.vue";
import SearchableSelect from "@/components/ui/searchable-select/SearchableSelect.vue";
import { Trash2, X, ChevronDown } from "lucide-vue-next";
import { MODIFIER_CATALOG, MODIFIER_CATEGORIES } from "../modifierCatalog.js";
import { OP_SPECS, OP_NODE_TYPES } from "./graphModel.js";

// reka-ui SelectItem can't hold an empty-string value; this sentinel is the re-selectable
// "default/none" option (mapped back to "" in the handlers).
const NONE = "__none__";

const props = defineProps({
  node: { type: Object, default: null },
  externalFieldOptions: { type: Array, default: () => [] },
  sourceFieldOptions: { type: Array, default: () => [] },
  storeFileOptions: { type: Array, default: () => [] },
  adapterFieldsWithOptions: { type: Array, default: () => [] },
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
        setTimeout(() => { highlightedField.value = null; }, 700);
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
    label: f?.fileName || "Файл магазина",
  });
};
const storeFileValue = computed(() =>
  data.value.storeFileId != null ? String(data.value.storeFileId) : undefined,
);

const modifierCatalog = computed(
  () => MODIFIER_CATALOG[data.value.modifierType],
);
const modifierParams = computed(() => modifierCatalog.value?.params || []);
// valueMap/optionMap editors handle the "default" param themselves — don't render it twice.
const visibleModifierParams = computed(() => {
  const isMap =
    data.value.modifierType === "valueMap" ||
    data.value.modifierType === "optionMap";
  return modifierParams.value.filter((p) => !(isMap && p.key === "default"));
});
const opSpec = computed(() => OP_SPECS[type.value]);

const isSource = computed(() =>
  ["attribute", "storeAttribute", "productField", "groupName"].includes(
    type.value,
  ),
);
const isOp = computed(() => OP_NODE_TYPES.includes(type.value));
const deletable = computed(() => type.value !== "adapterFields");

const titleByType = {
  attribute: "Атрибут категории",
  storeAttribute: "Атрибут магазина",
  productField: "Поле товара",
  static: "Статическое значение",
  groupName: "Название группы",
  storeFileUrl: "Файл магазина · ссылка",
  storeFileContent: "Файл магазина · текст",
  productFileByName: "Файл товара по имени · ссылка",
  productFileContentByName: "Файл товара по имени · текст",
  productFilesByExtension: "Файлы товара по расширению",
  adapterFields: "Поля адаптера",
};
const heading = computed(() => {
  if (!props.node) return "";
  if (type.value === "modifier") {
    const c = modifierCatalog.value;
    const cat = c ? MODIFIER_CATEGORIES[c.category]?.label : "";
    return `${c?.displayName || data.value.modifierType}${cat ? " · " + cat : ""}`;
  }
  if (isOp.value) return opSpec.value?.label || type.value;
  if (type.value === "static") {
    return (
      {
        number: "Число",
        boolean: "Логическое",
        array: "Список",
        string: "Текст",
      }[data.value.valueType] || "Значение"
    );
  }
  return titleByType[type.value] || type.value;
});

const fieldsCount = computed(() => (data.value.fields || []).length);
const requiredCount = computed(
  () => (data.value.fields || []).filter((f) => f.isRequired).length,
);

// Adapter sink: per-field source binding lives here (in settings), not as dropdowns inside the block.
const adapterFields = computed(() => data.value.fields || []);
const adapterAvail = computed(() => data.value.availableSources || {});
const adapterPreview = computed(() => data.value.preview || {});
const adapterSourceValue = (f) => {
  const s = f.source;
  if (!s) return "";
  if (s.type === "groupName") return "g";
  if (s.type === "productField") return `p:${s.field}`;
  if (s.type === "attribute") return `a:${s.attributeId}`;
  if (s.type === "storeAttribute") return `s:${s.attributeId}`;
  return "";
};
const setAdapterSource = (fieldKey, value) =>
  data.value.setFieldSource?.(fieldKey, value === NONE ? "" : value);

// Render at most one real <Select> at a time (a category can have dozens of fields — mounting a
// Select per field freezes the UI). Resting rows are cheap buttons; clicking one opens its editor.
const editingField = ref(null);
const currentSourceLabel = (f) => {
  const s = f.source;
  if (!s) return "по проводу";
  if (s.type === "groupName") return "Название группы";
  if (s.type === "productField")
    return (
      adapterAvail.value.productFields?.find((x) => x.key === s.field)
        ?.displayName ||
      s.field ||
      "Поле товара"
    );
  if (s.type === "attribute")
    return (
      adapterAvail.value.categoryAttributes?.find((x) => x.id === s.attributeId)
        ?.name || `атрибут #${s.attributeId}`
    );
  if (s.type === "storeAttribute")
    return (
      adapterAvail.value.storeAttributes?.find((x) => x.id === s.attributeId)
        ?.name || `атрибут #${s.attributeId}`
    );
  return "по проводу";
};

const setData = (patch) => emit("update", patch);
const setParam = (key, value) =>
  emit("update", { params: { ...(data.value.params || {}), [key]: value } });

const numberOrUndefined = (v) =>
  v === "" || v == null ? undefined : Number(v);
const splitTags = (v) =>
  (v || "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
const joinTags = (v) => (Array.isArray(v) ? v.join(", ") : v || "");

// --- "Стандартное значение" (isAdapterValue static node) ---

function onAdapterValuePick(val) {
  const opt = (data.value.options || []).find((o) => o.value === val);
  emit("update", { staticValue: val, label: opt?.label ?? val });
}

function onAdapterFieldPick(fieldKey) {
  const f = props.adapterFieldsWithOptions.find((f) => f.fieldKey === fieldKey);
  if (f)
    emit("update", {
      options: f.options,
      valueType: f.valueType,
      staticValue: "",
      label: "",
    });
}

const selectedArrayValues = computed(() => {
  const v = data.value.staticValue;
  return v ? String(v).split(",").map((s) => s.trim()).filter(Boolean) : [];
});

function toggleArrayValue(val) {
  const cur = selectedArrayValues.value;
  const next = cur.includes(val) ? cur.filter((v) => v !== val) : [...cur, val];
  const keys = next.join(",");
  const labels = next
    .map((k) => (data.value.options || []).find((o) => o.value === k)?.label ?? k)
    .join(", ");
  emit("update", { staticValue: keys, label: labels });
}

// --- Template modifier: field reference insertion ---

const templateTextareaRef = ref(null);

const templateFieldGroups = computed(() => {
  const map = {
    value:    { key: "value",    label: "Исходное значение" },
    product:  { key: "product",  label: "Поля товара" },
    category: { key: "category", label: "Атрибуты категории" },
    store:    { key: "store",    label: "Атрибуты магазина" },
  };
  const grouped = {};
  for (const r of props.templateFieldRefs) {
    (grouped[r.group] ??= []).push(r);
  }
  return Object.entries(grouped)
    .map(([g, refs]) => ({ ...map[g], refs }))
    .filter((g) => g.refs.length > 0);
});

function insertTemplateRef(paramKey, snippet) {
  const ta = templateTextareaRef.value;
  const cur = data.value.params?.[paramKey] ?? "";
  if (ta) {
    const s = ta.selectionStart ?? cur.length;
    const e = ta.selectionEnd ?? cur.length;
    const next = cur.slice(0, s) + snippet + cur.slice(e);
    setParam(paramKey, next);
    nextTick(() => {
      ta.selectionStart = ta.selectionEnd = s + snippet.length;
      ta.focus();
    });
  } else {
    setParam(paramKey, cur + snippet);
  }
}

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
  <div v-if="node" class="flex h-full w-full flex-col border-l bg-card">
    <div class="flex items-center justify-between gap-2 border-b px-3 py-2">
      <div class="truncate text-sm font-semibold">{{ heading }}</div>
      <button
        type="button"
        class="text-muted-foreground hover:text-foreground"
        @click="emit('close')"
      >
        <X class="size-4" />
      </button>
    </div>

    <div ref="scrollEl" class="min-h-0 flex-1 space-y-4 overflow-y-auto p-3 thin-scrollbar">
      <!-- Store shared-storage file picker (URL / text content) -->
      <template v-if="isStoreFile">
        <div class="space-y-1.5">
          <Label>Файл из хранилища магазина</Label>
          <Select
            :model-value="storeFileValue"
            @update:model-value="pickStoreFile($event)"
          >
            <SelectTrigger class="h-9 w-full">
              <SelectValue placeholder="Выберите файл" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem :value="NONE">Не выбран</SelectItem>
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
                ? "На выход подаётся ссылка на файл (например, для изображений)."
                : "На выход подаётся текстовое содержимое файла (например, rich-JSON)."
            }}
          </p>
          <p
            v-if="!storeFileOptions.length"
            class="text-xs text-muted-foreground"
          >
            В хранилище магазина пока нет файлов.
          </p>
        </div>
      </template>

      <!-- Product file by name (URL / text content) -->
      <template v-else-if="isProductFileByName">
        <div class="space-y-1.5">
          <Label>Имя файла товара</Label>
          <Input
            :model-value="data.fileName"
            placeholder="например, spec.pdf"
            @update:model-value="
              setData({
                fileName: $event,
                label: $event || 'Файл товара по имени',
              })
            "
          />
          <p class="text-xs text-muted-foreground">
            {{
              type === "productFileByName"
                ? "Ищет файл товара по имени и подаёт ссылку на него."
                : "Ищет файл товара по имени и подаёт его текстовое содержимое."
            }}
          </p>
        </div>
      </template>

      <!-- Product files by extension (array of URLs) -->
      <template v-else-if="isProductFilesByExt">
        <div class="space-y-1.5">
          <Label>Расширение</Label>
          <Input
            :model-value="data.fileExtension"
            placeholder="например, pdf"
            @update:model-value="
              setData({
                fileExtension: $event,
                label: $event
                  ? 'Файлы *.' + $event
                  : 'Файлы товара по расширению',
              })
            "
          />
          <p class="text-xs text-muted-foreground">
            Подаёт массив ссылок на все файлы товара с этим расширением.
          </p>
        </div>
      </template>

      <!-- Source: read-only binding -->
      <template v-else-if="isSource">
        <div class="space-y-1">
          <Label>Источник</Label>
          <div class="rounded-md border bg-muted/40 px-2.5 py-1.5 text-sm">
            {{ data.label || "—" }}
          </div>
          <p class="text-xs text-muted-foreground">
            Чтобы изменить источник, добавьте другой узел из меню (ПКМ/Tab) и
            переподключите.
          </p>
        </div>
        <div v-if="type === 'productField'" class="flex items-center gap-2">
          <Switch
            :model-value="data.importable !== false"
            @update:model-value="setData({ importable: $event })"
          />
          <Label class="cursor-pointer font-normal" @click="setData({ importable: data.importable !== false ? false : true })">
            Импортируемый
          </Label>
        </div>
      </template>

      <!-- "Стандартное значение" — static node with predefined options from adapter field -->
      <template v-else-if="type === 'static' && data.isAdapterValue">
        <div class="space-y-1.5">
          <Label>Значение</Label>

          <!-- Options available: single select -->
          <template v-if="data.options?.length && data.valueType !== 'array'">
            <SearchableSelect
              :options="data.options"
              :model-value="data.staticValue"
              placeholder="Выберите значение..."
              @update:model-value="onAdapterValuePick($event)"
            />
          </template>

          <!-- Options available: multi-select (array type) — checkbox list -->
          <template v-else-if="data.options?.length && data.valueType === 'array'">
            <div class="max-h-52 overflow-y-auto rounded border p-2 space-y-1.5">
              <label
                v-for="opt in data.options"
                :key="opt.value"
                class="flex cursor-pointer items-center gap-2 text-xs"
              >
                <input
                  type="checkbox"
                  :checked="selectedArrayValues.includes(opt.value)"
                  class="size-3 shrink-0 accent-primary"
                  @change="toggleArrayValue(opt.value)"
                />
                <span>{{ opt.label }}</span>
              </label>
            </div>
          </template>

          <!-- No options from adapter: free-text fallback (user types the ID manually) -->
          <template v-else>
            <Input
              :model-value="data.staticValue"
              placeholder="Введите значение..."
              @update:model-value="setData({ staticValue: $event, label: $event })"
            />
            <p class="text-[11px] text-muted-foreground">
              Для автоматической загрузки вариантов подключите блок к полю адаптера, у которого есть предустановленные значения.
            </p>
          </template>

          <!-- Manual field picker: load options from a known field -->
          <template v-if="adapterFieldsWithOptions.length">
            <Select :model-value="NONE" @update:model-value="onAdapterFieldPick($event)">
              <SelectTrigger class="h-7 text-xs">
                <SelectValue placeholder="Загрузить варианты из поля..." />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="f in adapterFieldsWithOptions"
                  :key="f.fieldKey"
                  :value="f.fieldKey"
                >{{ f.label }}</SelectItem>
              </SelectContent>
            </Select>
          </template>
        </div>
      </template>

      <!-- Static / typed literal (Число / Текст / Логическое / Список) -->
      <template v-else-if="type === 'static'">
        <div class="space-y-1.5">
          <Label>Значение</Label>

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
                  label: $event ? 'Да' : 'Нет',
                })
              "
            />
            <span class="text-sm text-muted-foreground">
              {{
                data.staticValue === "true" || data.staticValue === true
                  ? "Да"
                  : "Нет"
              }}
            </span>
          </div>

          <Input
            v-else-if="data.valueType === 'array'"
            :model-value="data.staticValue"
            placeholder="через запятую"
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

      <!-- Adapter fields (read-only summary) -->
      <template v-else-if="type === 'adapterFields'">
        <div class="space-y-2">
          <div
            class="rounded-md border bg-muted/40 px-2.5 py-1.5 text-xs text-muted-foreground"
          >
            Полей: <b class="text-foreground">{{ fieldsCount }}</b> ·
            обязательных: <b class="text-foreground">{{ requiredCount }}</b>
          </div>
          <p class="text-xs text-muted-foreground">
            Для каждого поля выберите стандартный источник напрямую (тогда пин
            на блоке исчезает) или оставьте «по проводу» и подключите узел.
          </p>

          <div class="space-y-2">
            <div
              v-for="f in adapterFields"
              :key="f.fieldKey"
              :data-fk="f.fieldKey"
              :class="['space-y-1 px-1', highlightedField === f.fieldKey && 'vf-field-flash']"
            >
              <Label class="text-xs">
                {{ f.label || f.fieldKey
                }}<span v-if="f.isRequired" class="text-destructive"> *</span>
              </Label>
              <!-- Resting: a cheap button. Only the row being edited mounts a real <Select>. -->
              <button
                v-if="editingField !== f.fieldKey"
                type="button"
                class="flex h-9 w-full items-center justify-between gap-2 rounded-md border border-input bg-background px-3 text-sm hover:bg-accent"
                @click="editingField = f.fieldKey"
              >
                <span
                  class="truncate"
                  :class="!f.source && 'text-muted-foreground'"
                >
                  {{ currentSourceLabel(f) }}
                </span>
                <ChevronDown class="size-4 shrink-0 opacity-50" />
              </button>
              <Select
                v-else
                :default-open="true"
                :model-value="adapterSourceValue(f) || undefined"
                @update:model-value="
                  setAdapterSource(f.fieldKey, $event);
                  editingField = null;
                "
                @update:open="(o) => !o && (editingField = null)"
              >
                <SelectTrigger class="h-9 w-full">
                  <SelectValue placeholder="по проводу" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem :value="NONE">По проводу</SelectItem>
                  <SelectGroup v-if="adapterAvail.productFields?.length">
                    <SelectLabel>Поле товара</SelectLabel>
                    <SelectItem
                      v-for="pf in adapterAvail.productFields"
                      :key="pf.key"
                      :value="`p:${pf.key}`"
                    >
                      {{ pf.displayName || pf.key }}
                    </SelectItem>
                  </SelectGroup>
                  <SelectGroup v-if="adapterAvail.categoryAttributes?.length">
                    <SelectLabel>Атрибут категории</SelectLabel>
                    <SelectItem
                      v-for="a in adapterAvail.categoryAttributes"
                      :key="a.id"
                      :value="`a:${a.id}`"
                    >
                      {{ a.name }}
                    </SelectItem>
                  </SelectGroup>
                  <SelectGroup v-if="adapterAvail.storeAttributes?.length">
                    <SelectLabel>Атрибут магазина</SelectLabel>
                    <SelectItem
                      v-for="a in adapterAvail.storeAttributes"
                      :key="a.id"
                      :value="`s:${a.id}`"
                    >
                      {{ a.name }}
                    </SelectItem>
                  </SelectGroup>
                  <SelectItem value="g">Название группы</SelectItem>
                </SelectContent>
              </Select>
              <div
                v-if="adapterPreview[f.fieldKey] != null"
                class="truncate rounded bg-muted px-2 py-0.5 font-mono text-[11px] text-muted-foreground"
                :title="String(resolvePreview(f.fieldKey, adapterPreview[f.fieldKey]))"
              >
                {{ resolvePreview(f.fieldKey, adapterPreview[f.fieldKey]) }}
              </div>
            </div>
          </div>
        </div>
      </template>

      <!-- Operation nodes (formatString / join / array ops) -->
      <template v-else-if="isOp">
        <div v-for="p in opSpec.params" :key="p.key" class="space-y-1.5">
          <Label>{{ p.label }}</Label>
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
          У узла нет параметров.
        </p>
      </template>

      <!-- Modifier params -->
      <template v-else-if="type === 'modifier'">
        <div
          v-for="p in visibleModifierParams"
          :key="p.key"
          class="space-y-1.5"
        >
          <Label class="flex items-center gap-1.5">
            {{ p.label }}
            <span
              v-if="p.type === 'number' || p.type === 'text'"
              class="text-[10px] font-normal text-muted-foreground"
              title="Значение ниже — по умолчанию; его можно переопределить, подав провод в пин слева."
            >
              · пин-переопределение
            </span>
          </Label>

          <Input
            v-if="p.type === 'number'"
            type="number"
            :model-value="data.params?.[p.key]"
            @update:model-value="setParam(p.key, numberOrUndefined($event))"
          />

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
              data.params?.[p.key] ? "Да" : "Нет"
            }}</span>
          </div>

          <div v-else-if="p.type === 'template'" class="space-y-2">
            <textarea
              ref="templateTextareaRef"
              rows="3"
              class="w-full rounded-md border border-input bg-background px-2 py-1.5 font-mono text-sm"
              :value="data.params?.[p.key]"
              @input="setParam(p.key, $event.target.value)"
            />
            <div v-if="templateFieldGroups.length" class="space-y-2">
              <p class="text-[11px] text-muted-foreground">Нажмите — вставится в позицию курсора:</p>
              <div v-for="group in templateFieldGroups" :key="group.key" class="space-y-1">
                <p class="text-[10px] font-medium uppercase tracking-wide text-muted-foreground/60">
                  {{ group.label }}
                </p>
                <div class="flex flex-wrap gap-1">
                  <button
                    v-for="r in group.refs"
                    :key="r.snippet"
                    type="button"
                    class="rounded border border-dashed border-border px-1.5 py-0.5 text-[11px] transition-colors hover:border-primary hover:text-primary"
                    :title="r.snippet"
                    @click="insertTemplateRef(p.key, r.snippet)"
                  >{{ r.label }}</button>
                </div>
              </div>
            </div>
          </div>

          <Input
            v-else-if="p.type === 'tagInput'"
            :model-value="joinTags(data.params?.[p.key])"
            placeholder="через запятую"
            @update:model-value="setParam(p.key, splitTags($event))"
          />

          <ValueMapEditor
            v-else-if="p.type === 'valueMap'"
            :mappings="data.params?.[p.key] || []"
            :external-field-options="externalFieldOptions"
            :default-value="data.params?.default || ''"
            @update:mappings="(v) => setParam(p.key, v)"
            @update:defaultValue="(v) => setParam('default', v)"
          />

          <OptionMapEditor
            v-else-if="p.type === 'optionMap'"
            :mappings="data.params?.[p.key] || []"
            :external-field-options="externalFieldOptions"
            :source-field-options="sourceFieldOptions"
            :default-value="data.params?.default || ''"
            @update:mappings="(v) => setParam(p.key, v)"
            @update:defaultValue="(v) => setParam('default', v)"
          />

          <Input
            v-else
            :model-value="data.params?.[p.key]"
            @update:model-value="setParam(p.key, $event)"
          />
        </div>
        <p v-if="!modifierParams.length" class="text-xs text-muted-foreground">
          У модификатора нет параметров.
        </p>
      </template>
    </div>

    <div
      v-if="data.previewValue != null"
      class="border-t px-3 py-2"
    >
      <div class="text-xs text-muted-foreground">Значение на выходе</div>
      <div class="mt-0.5 break-words font-mono text-xs">
        {{ data.previewValue }}
      </div>
    </div>

    <div v-if="deletable" class="border-t p-2">
      <Button
        variant="ghost"
        size="sm"
        class="w-full text-destructive hover:text-destructive"
        @click="emit('delete')"
      >
        <Trash2 class="mr-2 size-4" /> Удалить узел
        <span class="ml-1 text-xs text-muted-foreground">(Delete)</span>
      </Button>
    </div>
  </div>
</template>

<style scoped>
@keyframes vf-flash {
  0%   { background-color: rgba(120, 120, 120, 0.18); }
  100% { background-color: rgba(120, 120, 120, 0); }
}
.vf-field-flash {
  animation: vf-flash 0.9s ease-out forwards;
}
</style>
