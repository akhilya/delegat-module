<script setup>
import { Button } from "@/components/ui/button";
import { useI18n } from "vue-i18n";
import LogSelectionBar from "./LogSelectionBar.vue";

defineProps({
  count: { type: Number, default: 0 },
  total: { type: Number, default: null },
  /** Preformatted label; overrides count/total formatting when set */
  label: { type: String, default: "" },
  selectedCount: { type: Number, default: 0 },
  downloading: { type: Boolean, default: false },
  deleting: { type: Boolean, default: false },
});

defineEmits(["download", "delete", "clear-selection"]);

const { t } = useI18n();
</script>

<template>
  <div class="flex w-full min-w-0 flex-wrap items-center gap-2">
    <span v-if="count > 0 || label" class="tabular-nums whitespace-nowrap">
      <slot name="label">
        {{
          label
            || (total != null
              ? t("components.logs.itemsOfTotal", { count, total })
              : String(count))
        }}
      </slot>
    </span>
    <LogSelectionBar
      :count="selectedCount"
      :downloading="downloading"
      :deleting="deleting"
      @download="$emit('download')"
      @delete="$emit('delete')"
      @clear="$emit('clear-selection')"
    />
  </div>
</template>
