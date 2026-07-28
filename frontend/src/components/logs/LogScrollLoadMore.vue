<script setup>
import { Button } from "@/components/ui/button";
import { Loader2 } from "lucide-vue-next";
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
      <Loader2 v-if="loading" class="mr-1.5 size-3.5 animate-spin" />
      {{ label || t("components.logs.loadMore") }}
    </Button>
  </div>
</template>
