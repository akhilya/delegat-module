<script>
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Plus } from "lucide-vue-next";

const BRACES_OPEN = "{{";
const BRACES_CLOSE = "}}";

export default {
  components: {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    Plus,
  },
  props: {
    modelValue: { type: String, default: "" },
    availableSources: { type: Object, default: () => ({}) },
  },
  emits: ["update:modelValue"],
  computed: {
    placeholderText() {
      return `${BRACES_OPEN}value${BRACES_CLOSE}, высота: ${BRACES_OPEN}attr:5${BRACES_CLOSE}`;
    },
    valueLabel() {
      return `${BRACES_OPEN}value${BRACES_CLOSE}`;
    },
  },
  methods: {
    onInput(event) {
      this.$emit("update:modelValue", event.target.value);
    },
    wrap(key) {
      return `${BRACES_OPEN}${key}${BRACES_CLOSE}`;
    },
    insertPlaceholder(placeholder) {
      const textarea = this.$refs.textarea;
      if (!textarea) {
        this.$emit("update:modelValue", (this.modelValue || "") + placeholder);
        return;
      }
      const start = textarea.selectionStart;
      const end = textarea.selectionEnd;
      const text = this.modelValue || "";
      const newText = text.substring(0, start) + placeholder + text.substring(end);
      this.$emit("update:modelValue", newText);
      this.$nextTick(() => {
        const pos = start + placeholder.length;
        textarea.setSelectionRange(pos, pos);
        textarea.focus();
      });
    },
    insertField(key) {
      this.insertPlaceholder(this.wrap(key));
    },
    insertAttr(id) {
      this.insertPlaceholder(this.wrap("attr:" + id));
    },
    insertStoreAttr(id) {
      this.insertPlaceholder(this.wrap("storeAttr:" + id));
    },
    insertValue() {
      this.insertPlaceholder(this.wrap("value"));
    },
  },
};
</script>

<template>
  <div class="space-y-2">
    <textarea
      ref="textarea"
      :value="modelValue"
      @input="onInput"
      class="flex w-full rounded-md border border-input bg-background px-3 py-2 text-xs ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 min-h-[80px] resize-y font-mono"
      :placeholder="placeholderText"
    />
    <DropdownMenu>
      <DropdownMenuTrigger as-child>
        <button
          type="button"
          class="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground cursor-pointer transition-colors"
        >
          <Plus class="size-3" />
          Вставить поле
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" class="w-72 max-h-[350px] overflow-y-auto">
        <!-- Current value -->
        <DropdownMenuGroup>
          <DropdownMenuItem
            class="text-xs cursor-pointer py-1"
            @click="insertValue()"
          >
            <span class="font-mono text-xs text-muted-foreground shrink-0" v-text="valueLabel"></span>
            <span class="ml-auto text-muted-foreground text-xs">значение поля</span>
          </DropdownMenuItem>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />

        <!-- Product fields -->
        <template v-if="availableSources.productFields?.length">
          <DropdownMenuLabel class="text-xs py-0.5">Поля товара</DropdownMenuLabel>
          <DropdownMenuGroup>
            <DropdownMenuItem
              v-for="field in availableSources.productFields"
              :key="field.key"
              class="text-xs cursor-pointer py-1"
              @click="insertField(field.key)"
            >
              {{ field.displayName }}
              <span class="ml-auto font-mono text-xs text-muted-foreground">{{ field.key }}</span>
            </DropdownMenuItem>
          </DropdownMenuGroup>
          <DropdownMenuSeparator />
        </template>

        <!-- Category attributes -->
        <template v-if="availableSources.categoryAttributes?.length">
          <DropdownMenuLabel class="text-xs py-0.5">Атрибуты категории</DropdownMenuLabel>
          <DropdownMenuGroup>
            <DropdownMenuItem
              v-for="attr in availableSources.categoryAttributes"
              :key="'ca-' + attr.id"
              class="text-xs cursor-pointer py-1"
              @click="insertAttr(attr.id)"
            >
              {{ attr.name }}
              <span class="ml-auto font-mono text-xs text-muted-foreground">attr:{{ attr.id }}</span>
            </DropdownMenuItem>
          </DropdownMenuGroup>
          <DropdownMenuSeparator />
        </template>

        <!-- Store attributes -->
        <template v-if="availableSources.storeAttributes?.length">
          <DropdownMenuLabel class="text-xs py-0.5">Атрибуты магазина</DropdownMenuLabel>
          <DropdownMenuGroup>
            <DropdownMenuItem
              v-for="attr in availableSources.storeAttributes"
              :key="'sa-' + attr.id"
              class="text-xs cursor-pointer py-1"
              @click="insertStoreAttr(attr.id)"
            >
              {{ attr.name }}
              <span class="ml-auto font-mono text-xs text-muted-foreground">storeAttr:{{ attr.id }}</span>
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </template>
      </DropdownMenuContent>
    </DropdownMenu>
  </div>
</template>
