<script setup>
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { RefreshCw } from "lucide-vue-next";
import { useI18n } from "vue-i18n";

defineProps({
  loading: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
  compact: { type: Boolean, default: false },
});

defineEmits(["click"]);

const { t } = useI18n();
</script>

<template>
  <Button
    variant="outline"
    :size="compact ? 'icon' : 'default'"
    :aria-label="t('common.refresh')"
    :title="t('common.refresh')"
    :disabled="disabled || loading"
    @click="$emit('click')"
  >
    <Spinner v-if="loading" :class="!compact && 'mr-2'" />
    <RefreshCw v-else class="size-4" :class="!compact && 'mr-2'" />
    <template v-if="!compact"
      ><slot>{{ t("common.refresh") }}</slot></template
    >
  </Button>
</template>
