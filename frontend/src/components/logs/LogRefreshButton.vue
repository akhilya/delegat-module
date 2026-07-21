<script setup>
import { Button } from "@/components/ui/button";
import { Loader2, RefreshCw } from "lucide-vue-next";
import { useI18n } from "vue-i18n";

defineProps({
  loading: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
});

defineEmits(["click"]);

const { t } = useI18n();
</script>

<template>
  <Button
    variant="outline"
    :disabled="disabled || loading"
    @click="$emit('click')"
  >
    <Loader2 v-if="loading" class="mr-2 size-4 animate-spin" />
    <RefreshCw v-else class="mr-2 size-4" />
    <slot>{{ t("common.refresh") }}</slot>
  </Button>
</template>
