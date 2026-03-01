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
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ChevronUp, ChevronDown, X, Plus } from "lucide-vue-next";

export default {
  components: {
    ValueMapEditor,
    OptionMapEditor,
    TemplateEditor,
    Input,
    Label,
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
  },
  props: {
    modifiers: { type: Array, default: () => [] },
    externalFieldType: { type: String, default: null },
    externalFieldOptions: { type: Array, default: () => [] },
    sourceFieldOptions: { type: Array, default: () => [] },
    availableSources: { type: Object, default: () => ({}) },
    disabled: { type: Boolean, default: false },
  },
  emits: ["update:modifiers"],
  computed: {
    availableModifiers() {
      return getAvailableModifiers(this.externalFieldType);
    },
    groupedModifiers() {
      const groups = {};
      for (const mod of this.availableModifiers) {
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
        case "multiselect": return "border-l-orange-500";
        default: return "border-l-gray-400";
      }
    },

    addModifier(catalogEntry) {
      const params = {};
      for (const p of catalogEntry.params) {
        params[p.key] = p.default;
      }
      const updated = [...this.modifiers, { type: catalogEntry.type, params }];
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

    getParamValue(mod, param) {
      return mod.params?.[param.key] ?? param.default;
    },
  },
};
</script>

<template>
  <div class="space-y-2">
    <!-- Modifier cards -->
    <div
      v-for="(mod, index) in modifiers"
      :key="index"
      :class="[
        'rounded-md border border-l-[3px] bg-card text-card-foreground',
        getCategoryBorderColor(mod.type),
      ]"
    >
      <!-- Header -->
      <div class="flex items-center gap-1 px-3 py-2">
        <span class="text-sm font-medium flex-1 truncate">
          {{ getCatalog(mod.type)?.displayName || mod.type }}
        </span>
        <div v-if="!disabled" class="flex items-center gap-0.5 shrink-0">
          <button
            type="button"
            class="p-0.5 rounded hover:bg-muted text-muted-foreground hover:text-foreground cursor-pointer disabled:opacity-30 disabled:cursor-default"
            :disabled="index === 0"
            @click="moveModifier(index, -1)"
            title="Переместить вверх"
          >
            <ChevronUp class="size-3.5" />
          </button>
          <button
            type="button"
            class="p-0.5 rounded hover:bg-muted text-muted-foreground hover:text-foreground cursor-pointer disabled:opacity-30 disabled:cursor-default"
            :disabled="index === modifiers.length - 1"
            @click="moveModifier(index, 1)"
            title="Переместить вниз"
          >
            <ChevronDown class="size-3.5" />
          </button>
          <button
            type="button"
            class="p-0.5 rounded hover:bg-destructive/10 text-muted-foreground hover:text-destructive cursor-pointer ml-1"
            @click="removeModifier(index)"
            title="Удалить модификатор"
          >
            <X class="size-3.5" />
          </button>
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
        <button
          type="button"
          class="w-full flex items-center justify-center gap-1.5 px-3 py-2 rounded-md border border-dashed text-sm text-muted-foreground hover:border-foreground/40 hover:text-foreground cursor-pointer transition-colors"
        >
          <Plus class="size-4" />
          Добавить модификатор
        </button>
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
