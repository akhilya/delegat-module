<script>
import {
  MODIFIER_CATALOG,
  MODIFIER_CATEGORIES,
  getAvailableModifiers,
} from "./modifierCatalog.js";
import ValueMapEditor from "./ValueMapEditor.vue";
import OptionMapEditor from "./OptionMapEditor.vue";
import TemplateEditor from "./TemplateEditor.vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import Badge from "@/components/ui/badge/Badge.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { Button } from "@/components/ui/button";
import { ChevronUp, ChevronDown, X, Plus, AlertTriangle } from "lucide-vue-next";

export default {
  components: {
    ValueMapEditor,
    OptionMapEditor,
    TemplateEditor,
    Input,
    Label,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Badge,
    Checkbox,
    Button,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    ChevronUp,
    ChevronDown,
    X,
    Plus,
    AlertTriangle,
  },
  props: {
    modifiers: { type: Array, default: () => [] },
    externalFieldType: { type: String, default: null },
    externalFieldOptions: { type: Array, default: () => [] },
    sourceFieldOptions: { type: Array, default: () => [] },
    adapterFieldsWithOptions: { type: Array, default: () => [] },
    aiModelOptions: { type: Array, default: () => [] },
    availableSources: { type: Object, default: () => ({}) },
    disabled: { type: Boolean, default: false },
  },
  emits: ["update:modifiers"],
  data() {
    return {
      tagInputValues: {},
    };
  },
  computed: {
    groupedModifiers() {
      const groups = {};
      for (const mod of this.getAvailableModifiersForIndex(this.modifiers.length)) {
        const cat = mod.category;
        if (!groups[cat]) {
          groups[cat] = {
            label: MODIFIER_CATEGORIES[cat]?.label || cat,
            items: [],
          };
        }
        groups[cat].items.push(mod);
      }
      return groups;
    },
  },
  methods: {
    getEffectiveTypeAtIndex(index) {
      // Scan modifiers before this index for the LAST enabled forceType
      for (let i = index - 1; i >= 0; i--) {
        if (this.modifiers[i]?.enabled === false) continue;
        if (this.modifiers[i]?.type === "forceType") {
          return this.modifiers[i]?.params?.type || this.externalFieldType;
        }
      }
      return this.externalFieldType;
    },

    getAvailableModifiersForIndex(index) {
      const effectiveType = this.getEffectiveTypeAtIndex(index);
      return getAvailableModifiers(effectiveType);
    },

    getCatalog(type) {
      return MODIFIER_CATALOG[type];
    },

    getParams(type) {
      return MODIFIER_CATALOG[type]?.params || [];
    },

    hasParams(type) {
      const catalog = MODIFIER_CATALOG[type];
      return catalog && catalog.params.length > 0;
    },

    getCategoryBorderColor(type) {
      const catalog = MODIFIER_CATALOG[type];
      if (!catalog) return "border-l-gray-400";
      switch (catalog.category) {
        case "text": return "border-l-blue-500";
        case "number": return "border-l-green-500";
        case "select": return "border-l-purple-500";
        case "array": return "border-l-cyan-500";
        default: return "border-l-gray-400";
      }
    },

    addModifier(catalogEntry) {
      const params = {};
      for (const p of catalogEntry.params) {
        params[p.key] = p.default;
      }
      const updated = [...this.modifiers, { type: catalogEntry.type, params, enabled: true }];
      this.$emit("update:modifiers", updated);
    },

    onToggleEnabled(index, value) {
      const updated = [...this.modifiers];
      updated[index] = { ...updated[index], enabled: !!value };
      this.$emit("update:modifiers", updated);
    },

    removeModifier(index) {
      const updated = this.modifiers.filter((_, i) => i !== index);
      this.$emit("update:modifiers", updated);
    },

    moveModifier(index, direction) {
      const newIndex = index + direction;
      if (newIndex < 0 || newIndex >= this.modifiers.length) return;
      const updated = [...this.modifiers];
      [updated[index], updated[newIndex]] = [updated[newIndex], updated[index]];
      this.$emit("update:modifiers", updated);
    },

    onParamChange(index, key, value) {
      const updated = [...this.modifiers];
      updated[index] = {
        ...updated[index],
        params: { ...updated[index].params, [key]: value },
      };
      this.$emit("update:modifiers", updated);
    },

    isReversible(type) {
      return MODIFIER_CATALOG[type]?.reversible === true;
    },

    getParamValue(mod, param) {
      return mod.params?.[param.key] ?? param.default;
    },

    getTagInputKey(index, paramKey) {
      return `${index}_${paramKey}`;
    },

    addTag(index, paramKey) {
      const key = this.getTagInputKey(index, paramKey);
      const val = (this.tagInputValues[key] || "").trim();
      if (!val) return;
      const current = Array.isArray(this.modifiers[index]?.params?.[paramKey])
        ? [...this.modifiers[index].params[paramKey]]
        : [];
      current.push(val);
      this.onParamChange(index, paramKey, current);
      this.tagInputValues[key] = "";
    },

    removeTag(index, paramKey, tagIndex) {
      const current = Array.isArray(this.modifiers[index]?.params?.[paramKey])
        ? [...this.modifiers[index].params[paramKey]]
        : [];
      current.splice(tagIndex, 1);
      this.onParamChange(index, paramKey, current);
    },
  },
};
</script>

<template>
  <div class="space-y-2">
    <!-- Modifier cards -->
    <div
      v-for="(mod, index) in modifiers"
      :key="mod.type + '-' + index"
      :class="[
        'rounded-md border border-l-[3px] bg-card text-card-foreground',
        getCategoryBorderColor(mod.type),
        mod.enabled === false && 'opacity-50',
      ]"
    >
      <!-- Header -->
      <div class="flex items-center gap-1.5 px-3 py-2">
        <Checkbox
          v-if="!disabled"
          :model-value="mod.enabled !== false"
          @update:modelValue="(v) => onToggleEnabled(index, v)"
          class="shrink-0"
          title="Включить/выключить модификатор"
        />
        <span :class="['text-sm font-medium flex-1 truncate', mod.enabled === false && 'line-through text-muted-foreground']">
          {{ getCatalog(mod.type)?.displayName || mod.type }}
        </span>
        <span
          v-if="!isReversible(mod.type)"
          class="inline-flex items-center gap-0.5 text-warning shrink-0"
          title="При импорте значение не будет восстановлено"
        >
          <AlertTriangle class="size-3" />
          <span class="text-xs leading-none">необратимый</span>
        </span>
        <div v-if="!disabled" class="flex items-center gap-0.5 shrink-0">
          <Button
            type="button"
            variant="ghost"
            size="icon"
            class="size-6 p-0.5 text-muted-foreground hover:text-foreground disabled:opacity-30"
            :disabled="index === 0"
            @click="moveModifier(index, -1)"
            title="Переместить вверх"
          >
            <ChevronUp class="size-3.5" />
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            class="size-6 p-0.5 text-muted-foreground hover:text-foreground disabled:opacity-30"
            :disabled="index === modifiers.length - 1"
            @click="moveModifier(index, 1)"
            title="Переместить вниз"
          >
            <ChevronDown class="size-3.5" />
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            class="size-6 p-0.5 ml-1 text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
            @click="removeModifier(index)"
            title="Удалить модификатор"
          >
            <X class="size-3.5" />
          </Button>
        </div>
      </div>

      <!-- Body (params) -->
      <div v-if="hasParams(mod.type)" class="px-3 pb-3 space-y-2 border-t pt-2">
        <template v-for="param in getParams(mod.type)" :key="param.key">
          <!-- ValueMap editor -->
          <template v-if="param.type === 'valueMap'">
            <ValueMapEditor
              :mappings="mod.params?.[param.key] || []"
              :external-field-options="externalFieldOptions"
              :source-field-options="sourceFieldOptions"
              :default-value="mod.params?.['default'] || ''"
              @update:mappings="(v) => onParamChange(index, param.key, v)"
              @update:defaultValue="(v) => onParamChange(index, 'default', v)"
            />
          </template>

          <!-- OptionMap editor (source text → marketplace option) -->
          <template v-else-if="param.type === 'optionMap'">
            <OptionMapEditor
              :mappings="mod.params?.[param.key] || []"
              :external-field-options="externalFieldOptions"
              :source-field-options="sourceFieldOptions"
              :persisted-target-options="mod.targetFieldOptions || []"
              :adapter-fields-with-options="adapterFieldsWithOptions"
              :default-value="mod.params?.['default'] || ''"
              @update:mappings="(v) => onParamChange(index, param.key, v)"
              @update:defaultValue="(v) => onParamChange(index, 'default', v)"
            />
          </template>

          <!-- Template editor -->
          <template v-else-if="param.type === 'template'">
            <div class="space-y-1">
              <Label class="text-xs">{{ param.label }}</Label>
              <TemplateEditor
                :model-value="getParamValue(mod, param)"
                :available-sources="availableSources"
                @update:modelValue="(v) => onParamChange(index, param.key, v)"
              />
            </div>
          </template>

          <!-- Tag input (string array) -->
          <template v-else-if="param.type === 'tagInput'">
            <div class="space-y-1">
              <Label class="text-xs">{{ param.label }}</Label>
              <div class="flex gap-1">
                <Input
                  :model-value="tagInputValues[getTagInputKey(index, param.key)] || ''"
                  @update:modelValue="(v) => (tagInputValues[getTagInputKey(index, param.key)] = v)"
                  @keydown.enter.prevent="addTag(index, param.key)"
                  placeholder="Введите значение…"
                  class="h-7 text-xs flex-1"
                />
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  class="h-7 px-2 text-xs shrink-0"
                  @click="addTag(index, param.key)"
                >
                  <Plus class="size-3" />
                </Button>
              </div>
              <div v-if="(getParamValue(mod, param) || []).length" class="flex flex-wrap gap-1 pt-1">
                <Badge
                  v-for="(tag, tagIdx) in getParamValue(mod, param)"
                  :key="tagIdx"
                  variant="secondary"
                  class="text-xs gap-1 pr-1"
                >
                  {{ tag }}
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    class="size-auto h-auto p-0 hover:text-destructive"
                    @click="removeTag(index, param.key, tagIdx)"
                  >
                    <X class="size-3" />
                  </Button>
                </Badge>
              </div>
            </div>
          </template>

          <!-- AI model picker -->
          <template v-else-if="param.type === 'aiModel'">
            <div class="space-y-1">
              <Label class="text-xs">{{ param.label }}</Label>
              <Select
                :model-value="getParamValue(mod, param) != null ? String(getParamValue(mod, param)) : undefined"
                @update:modelValue="(v) => onParamChange(index, param.key, v ? Number(v) : undefined)"
              >
                <SelectTrigger class="h-7 text-xs">
                  <SelectValue placeholder="Выберите модель" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="m in aiModelOptions"
                    :key="m.value"
                    :value="String(m.value)"
                    class="text-xs"
                  >
                    {{ m.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
          </template>

          <!-- Select param (dropdown) -->
          <template v-else-if="param.type === 'select'">
            <div class="space-y-1">
              <Label class="text-xs">{{ param.label }}</Label>
              <Select
                :model-value="getParamValue(mod, param)"
                @update:modelValue="(v) => onParamChange(index, param.key, v)"
              >
                <SelectTrigger class="h-7 text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="opt in param.options"
                    :key="opt.value"
                    :value="opt.value"
                    class="text-xs"
                  >
                    {{ opt.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
          </template>

          <!-- Number param -->
          <template v-else-if="param.type === 'number'">
            <div class="space-y-1">
              <Label class="text-xs">{{ param.label }}</Label>
              <Input
                type="number"
                :model-value="getParamValue(mod, param)"
                @update:modelValue="(v) => onParamChange(index, param.key, Number(v))"
                class="h-7 text-xs"
              />
            </div>
          </template>

          <!-- Checkbox param -->
          <template v-else-if="param.type === 'checkbox'">
            <div class="flex items-center gap-2">
              <Checkbox
                :id="`mod-${index}-${param.key}`"
                :model-value="!!getParamValue(mod, param)"
                @update:modelValue="(v) => onParamChange(index, param.key, v)"
              />
              <Label :for="`mod-${index}-${param.key}`" class="text-xs cursor-pointer">
                {{ param.label }}
              </Label>
            </div>
          </template>

          <!-- Text param (skip 'default' when inside valueMap/optionMap — handled by editor) -->
          <template v-else-if="param.type === 'text' && !((mod.type === 'valueMap' || mod.type === 'optionMap') && param.key === 'default')">
            <div class="space-y-1">
              <Label class="text-xs">{{ param.label }}</Label>
              <Input
                :model-value="getParamValue(mod, param)"
                @update:modelValue="(v) => onParamChange(index, param.key, v)"
                class="h-7 text-xs"
              />
            </div>
          </template>
        </template>
      </div>
    </div>

    <!-- Add modifier button -->
    <DropdownMenu v-if="!disabled">
      <DropdownMenuTrigger as-child>
        <Button
          type="button"
          variant="outline"
          class="w-full h-auto flex items-center justify-center gap-1.5 px-3 py-2 border-dashed text-sm text-muted-foreground hover:border-foreground/40 hover:text-foreground"
        >
          <Plus class="size-4" />
          Добавить модификатор
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" class="w-56">
        <template v-for="(group, catKey) in groupedModifiers" :key="catKey">
          <DropdownMenuLabel class="text-xs">{{ group.label }}</DropdownMenuLabel>
          <DropdownMenuGroup>
            <DropdownMenuItem
              v-for="mod in group.items"
              :key="mod.type"
              @click="addModifier(mod)"
              class="text-sm cursor-pointer"
            >
              {{ mod.displayName }}
            </DropdownMenuItem>
          </DropdownMenuGroup>
          <DropdownMenuSeparator />
        </template>
      </DropdownMenuContent>
    </DropdownMenu>
  </div>
</template>
