<script>
import {
  MODIFIER_CATALOG,
  MODIFIER_CATEGORIES,
  getAvailableModifiers,
  getModifierLabel,
} from "./modifierCatalog.js";
import ModifierEditor from "./ModifierEditor.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Plus,
  X,
  ChevronLeft,
  ChevronRight,
  GripVertical,
} from "lucide-vue-next";

export default {
  components: {
    ModifierEditor,
    Button,
    Badge,
    Popover,
    PopoverContent,
    PopoverTrigger,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    Plus,
    X,
    ChevronLeft,
    ChevronRight,
    GripVertical,
  },
  props: {
    modifiers: { type: Array, default: () => [] },
    externalFieldType: { type: String, default: null },
    externalFieldOptions: { type: Array, default: () => [] },
    sourceFieldOptions: { type: Array, default: () => [] },
    disabled: { type: Boolean, default: false },
  },
  emits: ["update:modifiers"],
  data() {
    return {
      editingIndex: null,
    };
  },
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
    getModifierLabel,

    addModifier(catalogEntry) {
      const params = {};
      for (const p of catalogEntry.params) {
        params[p.key] = p.default;
      }
      const newModifier = { type: catalogEntry.type, params };
      const updated = [...this.modifiers, newModifier];
      this.$emit("update:modifiers", updated);
      // Open editor for the new modifier if it has params
      if (catalogEntry.params.length > 0) {
        this.$nextTick(() => {
          this.editingIndex = updated.length - 1;
        });
      }
    },

    removeModifier(index) {
      const updated = this.modifiers.filter((_, i) => i !== index);
      this.$emit("update:modifiers", updated);
      if (this.editingIndex === index) this.editingIndex = null;
    },

    moveModifier(index, direction) {
      const newIndex = index + direction;
      if (newIndex < 0 || newIndex >= this.modifiers.length) return;
      const updated = [...this.modifiers];
      [updated[index], updated[newIndex]] = [updated[newIndex], updated[index]];
      this.$emit("update:modifiers", updated);
      if (this.editingIndex === index) this.editingIndex = newIndex;
    },

    updateModifier(index, modifier) {
      const updated = [...this.modifiers];
      updated[index] = modifier;
      this.$emit("update:modifiers", updated);
    },

    openEditor(index) {
      this.editingIndex = this.editingIndex === index ? null : index;
    },

    closeEditor() {
      this.editingIndex = null;
    },

    getCategoryColor(type) {
      const catalog = MODIFIER_CATALOG[type];
      if (!catalog) return "";
      switch (catalog.category) {
        case "text": return "bg-blue-50 text-blue-700 border-blue-200 hover:bg-blue-100";
        case "number": return "bg-green-50 text-green-700 border-green-200 hover:bg-green-100";
        case "select": return "bg-purple-50 text-purple-700 border-purple-200 hover:bg-purple-100";
        case "multiselect": return "bg-orange-50 text-orange-700 border-orange-200 hover:bg-orange-100";
        default: return "bg-gray-50 text-gray-700 border-gray-200 hover:bg-gray-100";
      }
    },

    hasParams(type) {
      const catalog = MODIFIER_CATALOG[type];
      return catalog && catalog.params.length > 0;
    },
  },
};
</script>

<template>
  <div v-if="modifiers.length > 0 || !disabled" class="flex flex-wrap items-center gap-1.5">
    <!-- Modifier pills -->
    <template v-for="(mod, index) in modifiers" :key="mod.type + '-' + index">
      <div class="flex items-center gap-0">
        <!-- Reorder buttons -->
        <Button
          v-if="index > 0 && !disabled"
          type="button"
          variant="ghost"
          size="icon"
          class="size-6 p-0.5 h-6 text-muted-foreground hover:text-foreground"
          @click="moveModifier(index, -1)"
          :title="$t('marketplacePillChain.moveLeft')"
        >
          <ChevronLeft class="size-3" />
        </Button>

        <!-- Pill -->
        <Popover :open="editingIndex === index" @update:open="(v) => { if (!v) closeEditor(); }">
          <div class="inline-flex items-center">
            <PopoverTrigger as-child>
              <Button
                type="button"
                variant="outline"
                :class="[
                  'inline-flex items-center gap-1 px-2 py-0.5 h-auto rounded-full text-xs transition-colors',
                  getCategoryColor(mod.type),
                  disabled ? 'opacity-50 pointer-events-none' : '',
                ]"
                @click="!disabled && hasParams(mod.type) && openEditor(index)"
                :title="hasParams(mod.type) ? $t('marketplacePillChain.editHint') : ''"
              >
                <span>{{ getModifierLabel(mod) }}</span>
              </Button>
            </PopoverTrigger>
            <Button
              v-if="!disabled"
              type="button"
              variant="ghost"
              size="icon"
              class="size-5 p-0 ml-0.5 hover:text-destructive"
              @click.stop="removeModifier(index)"
              :title="$t('marketplacePillChain.removeModifier')"
            >
              <X class="size-3" />
            </Button>
          </div>
          <PopoverContent
            v-if="hasParams(mod.type)"
            class="w-auto p-0"
            align="start"
            :side="'bottom'"
          >
            <ModifierEditor
              :modifier="mod"
              :external-field-options="externalFieldOptions"
              :source-field-options="sourceFieldOptions"
              @update="(updated) => updateModifier(index, updated)"
              @close="closeEditor"
            />
          </PopoverContent>
        </Popover>

        <!-- Reorder right -->
        <Button
          v-if="index < modifiers.length - 1 && !disabled"
          type="button"
          variant="ghost"
          size="icon"
          class="size-6 p-0.5 h-6 text-muted-foreground hover:text-foreground"
          @click="moveModifier(index, 1)"
          :title="$t('marketplacePillChain.moveRight')"
        >
          <ChevronRight class="size-3" />
        </Button>
      </div>

      <!-- Arrow between pills -->
      <span
        v-if="index < modifiers.length - 1"
        class="text-muted-foreground text-xs"
      >→</span>
    </template>

    <!-- Add modifier button -->
    <DropdownMenu v-if="!disabled">
      <DropdownMenuTrigger as-child>
        <Button
          type="button"
          variant="outline"
          class="inline-flex items-center gap-1 px-2 py-0.5 h-auto rounded-full text-xs border-dashed border-muted-foreground/40 text-muted-foreground hover:border-foreground/60 hover:text-foreground"
        >
          <Plus class="size-3" />
          <span v-if="modifiers.length === 0">{{ $t("marketplace.graph.modifierFallback") }}</span>
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
