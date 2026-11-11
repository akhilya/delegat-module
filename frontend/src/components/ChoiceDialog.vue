<script>
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";

export default {
  name: "ChoiceDialog",
  components: {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    InsetGroup,
    InsetGroupItem,
  },
  props: {
    open: { type: Boolean, default: false },
    title: { type: String, required: true },
    description: { type: String, default: "" },
    /** Options use { value, label, description?, disabled? }. */
    options: { type: Array, default: () => [] },
    closeOnSelect: { type: Boolean, default: true },
  },
  emits: ["update:open", "select"],
  methods: {
    selectOption(option) {
      if (option.disabled) return;
      this.$emit("select", option.value);
      if (this.closeOnSelect) this.$emit("update:open", false);
    },
  },
};
</script>

<template>
  <Dialog :open="open" @update:open="$emit('update:open', $event)">
    <DialogContent size="md" class="max-h-[88vh] overflow-y-auto">
      <DialogHeader>
        <DialogTitle>{{ title }}</DialogTitle>
        <DialogDescription v-if="description">
          {{ description }}
        </DialogDescription>
      </DialogHeader>

      <InsetGroup v-if="options.length">
        <InsetGroupItem
          v-for="option in options"
          :key="option.value"
          :disabled="option.disabled"
          show-chevron
          @click="selectOption(option)"
        >
          <p class="font-medium">{{ option.label }}</p>
          <p
            v-if="option.description"
            class="mt-0.5 text-xs font-normal text-muted-foreground"
          >
            {{ option.description }}
          </p>
        </InsetGroupItem>
      </InsetGroup>
    </DialogContent>
  </Dialog>
</template>
