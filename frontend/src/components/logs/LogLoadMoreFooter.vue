<script setup>
import { Button } from "@/components/ui/button";
import { useI18n } from "vue-i18n";

defineProps({
  count: { type: Number, default: 0 },
  total: { type: Number, default: null },
  /** Preformatted label; overrides count/total formatting when set */
  label: { type: String, default: "" },
  hasMore: { type: Boolean, default: false },
  loading: { type: Boolean, default: false },
  loadMoreLabel: { type: String, default: "" },
});

defineEmits(["load-more"]);

const { t } = useI18n();
</script>

<template>
  <template v-if="count > 0">
    <span class="tabular-nums">
      <slot name="label">
        {{
          label
            || (total != null
              ? t("components.logs.itemsOfTotal", { count, total })
              : String(count))
        }}
      </slot>
    </span>
    <Button
      v-if="hasMore"
      variant="outline"
      size="sm"
      :disabled="loading"
      @click="$emit('load-more')"
    >
      {{ loadMoreLabel || t("components.logs.loadMore") }}
    </Button>
  </template>
</template>
