<script setup>
import { useI18n } from "vue-i18n";
import LogExchangeDetail from "./LogExchangeDetail.vue";
import LogCodeBlock from "./LogCodeBlock.vue";
import LogMetaGrid from "./LogMetaGrid.vue";

defineProps({
  open: { type: Boolean, default: false },
  loading: { type: Boolean, default: false },
  detail: { type: Object, default: null },
  metaItems: { type: Array, default: () => [] },
  title: { type: String, default: "" },
  description: { type: String, default: "" },
});

defineEmits(["update:open"]);

const { t } = useI18n();
</script>

<template>
  <LogExchangeDetail
    :open="open"
    :loading="loading"
    :detail="detail"
    :meta-items="metaItems"
    :title="title"
    :description="description"
    :aria-label="t('pages.apiPage.logs.request')"
    json-bodies
    body-download-base-url="/api-keys/logs"
    @update:open="$emit('update:open', $event)"
  >
    <template #overview-extra="{ detail: d }">
      <LogMetaGrid
        v-if="d.userAgent"
        :items="[
          { label: t('pages.apiPage.logs.userAgent'), value: d.userAgent },
        ]"
      />
    </template>

    <template #request-extra="{ detail: d }">
      <LogCodeBlock
        v-if="d.queryString"
        :label="t('pages.apiPage.logs.query')"
        :text="d.queryString"
      />
    </template>
  </LogExchangeDetail>
</template>
