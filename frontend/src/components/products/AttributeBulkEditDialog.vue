<script setup>
import { computed, ref, useId, watch } from "vue";
import { t } from "@/i18n";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ATTRIBUTE_BULK_FIELDS, attributeBulkPatch } from "@/lib/attributeBulk";

const props = defineProps({
  open: { type: Boolean, default: false },
  count: { type: Number, required: true },
  busy: { type: Boolean, default: false },
});
const emit = defineEmits(["update:open", "submit", "closeAutoFocus"]);
const id = useId();
const settings = ref({});
const patch = computed(() => attributeBulkPatch(settings.value));
watch(
  () => props.open,
  (open) => {
    if (open)
      settings.value = Object.fromEntries(
        ATTRIBUTE_BULK_FIELDS.map((field) => [field, "keep"]),
      );
  },
);
function setOpen(open) {
  if (!props.busy) emit("update:open", open);
}
</script>

<template>
  <Dialog :open="open" @update:open="setOpen">
    <DialogContent
      size="md"
      class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
      :show-close="!busy"
      @close-auto-focus="emit('closeAutoFocus', $event)"
      @escape-key-down="busy && $event.preventDefault()"
      @interact-outside="busy && $event.preventDefault()"
    >
      <DialogHeader variant="bar">
        <DialogTitle>{{
          t("components.attributeDefinitions.editTitle")
        }}</DialogTitle>
        <DialogDescription>{{
          t("components.attributeDefinitions.editDescription", { count })
        }}</DialogDescription>
      </DialogHeader>
      <form
        :id="id"
        class="min-h-0 flex-1 overflow-y-auto px-6 py-5"
        @submit.prevent="
          !busy && Object.keys(patch).length && emit('submit', patch)
        "
      >
        <FieldGroup class="gap-5">
          <SettingsFormField
            v-for="field in ATTRIBUTE_BULK_FIELDS"
            :key="field"
            :label="t(`components.attributeDefinitions.${field}`)"
            :label-for="`${id}-${field}`"
          >
            <Select v-model="settings[field]" :disabled="busy">
              <SelectTrigger :id="`${id}-${field}`" class="w-full"
                ><SelectValue
              /></SelectTrigger>
              <SelectContent>
                <SelectItem value="keep">{{
                  t("components.attributeDefinitions.keep")
                }}</SelectItem>
                <SelectItem value="true">{{ t("common.yes") }}</SelectItem>
                <SelectItem value="false">{{ t("common.no") }}</SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>
          <p class="text-xs text-muted-foreground">
            {{ t("components.attributeDefinitions.requiredHint") }}
          </p>
        </FieldGroup>
      </form>
      <DialogFooter variant="bar">
        <Button
          type="button"
          variant="outline"
          :disabled="busy"
          @click="setOpen(false)"
          >{{ t("common.cancel") }}</Button
        >
        <Button
          type="submit"
          :form="id"
          :disabled="busy || !count || !Object.keys(patch).length"
        >
          <Spinner v-if="busy" class="mr-2" />{{ t("common.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
