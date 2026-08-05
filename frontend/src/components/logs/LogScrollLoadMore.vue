<script setup>
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";

import { useI18n } from "vue-i18n";

defineProps({
  loading: { type: Boolean, default: false },
  label: { type: String, default: "" },
});

defineEmits(["load-more"]);

const { t } = useI18n();
</script>

<template>
  <div class="flex justify-center border-t px-3 py-3">
    <Button
      type="button"
      variant="outline"
      size="sm"
      class="h-8"
      :disabled="loading"
      @click="$emit('load-more')"
    >
      <Spinner v-if="loading" size="sm" class="mr-1.5" />
      {{ label || t("components.logs.loadMore") }}
    </Button>
  </div>
</template>
