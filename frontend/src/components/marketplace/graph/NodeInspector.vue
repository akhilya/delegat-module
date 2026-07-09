<script setup>
import { computed } from "vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Button from "@/components/ui/button/Button.vue";
import ValueMapEditor from "../ValueMapEditor.vue";
import OptionMapEditor from "../OptionMapEditor.vue";
import { Trash2, X } from "lucide-vue-next";
import { MODIFIER_CATALOG, MODIFIER_CATEGORIES } from "../modifierCatalog.js";
import { OP_SPECS, OP_NODE_TYPES } from "./graphModel.js";

const props = defineProps({
  node: { type: Object, default: null },
  externalFieldOptions: { type: Array, default: () => [] },
  sourceFieldOptions: { type: Array, default: () => [] },
  storeFileOptions: { type: Array, default: () => [] },
});
const emit = defineEmits(["update", "delete", "close"]);

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
  const f = props.storeFileOptions.find(
    (x) => String(fileId(x)) === String(id),
  );
  emit("update", {
    storeFileId: id === "" || id == null ? null : Number(id),
    label: f?.fileName || "Файл магазина",
  });
};

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
</script>

<template>
  <div v-if="node" class="flex h-full w-72 shrink-0 flex-col border-l bg-card">
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

    <div class="min-h-0 flex-1 space-y-4 overflow-y-auto p-3 thin-scrollbar">
      <!-- Per-node preview value (intermediate) -->
      <div
        v-if="data.previewValue != null"
        class="rounded-md border bg-muted/40 px-2.5 py-2"
      >
        <div class="text-xs text-muted-foreground">Значение на выходе</div>
        <div class="mt-0.5 break-words font-mono text-xs">
          {{ data.previewValue }}
        </div>
      </div>

      <!-- Store shared-storage file picker (URL / text content) -->
      <template v-if="isStoreFile">
        <div class="space-y-1.5">
          <Label>Файл из хранилища магазина</Label>
          <select
            class="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
            :value="data.storeFileId ?? ''"
            @change="pickStoreFile($event.target.value)"
          >
            <option value="">— выберите файл —</option>
            <option
              v-for="f in storeFileOptions"
              :key="fileId(f)"
              :value="fileId(f)"
            >
              {{ f.fileName }}
            </option>
          </select>
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
        <div class="space-y-1">
          <div class="rounded-md border bg-muted/40 px-2.5 py-2 text-sm">
            Полей: <b>{{ fieldsCount }}</b> · обязательных:
            <b>{{ requiredCount }}</b>
          </div>
          <p class="text-xs text-muted-foreground">
            Подключите выход к нужному полю. Тип отправки берётся из адаптера,
            либо из модификатора «Принудительный тип».
          </p>
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
          <Label>{{ p.label }}</Label>

          <Input
            v-if="p.type === 'number'"
            type="number"
            :model-value="data.params?.[p.key]"
            @update:model-value="setParam(p.key, numberOrUndefined($event))"
          />

          <select
            v-else-if="p.type === 'select'"
            class="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
            :value="data.params?.[p.key] ?? p.default"
            @change="setParam(p.key, $event.target.value)"
          >
            <option v-for="o in p.options" :key="o.value" :value="o.value">
              {{ o.label }}
            </option>
          </select>

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

          <textarea
            v-else-if="p.type === 'template'"
            rows="2"
            class="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
            :value="data.params?.[p.key]"
            @input="setParam(p.key, $event.target.value)"
          />

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
