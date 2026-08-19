<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import LogExchangeDetail from "./LogExchangeDetail.vue";
import LogCodeBlock from "./LogCodeBlock.vue";
import LogMetaGrid from "./LogMetaGrid.vue";

const props = defineProps({
  open: { type: Boolean, default: false },
  loading: { type: Boolean, default: false },
  detail: { type: Object, default: null },
  metaItems: { type: Array, default: () => [] },
  title: { type: String, default: "" },
  description: { type: String, default: "" },
});

defineEmits(["update:open"]);

const { t } = useI18n();

const extraMeta = computed(() => {
  if (!props.detail) return [];
  const items = [
    {
      label: t("marketplace.adapterLogsPage.storeId"),
      value: props.detail.storeId,
    },
    {
      label: t("marketplace.adapterLogsPage.connectionId"),
      value: props.detail.connectionId,
    },
  ];
  if (props.detail.operationId) {
    items.push({
      label: t("marketplace.adapterLogsPage.operationId"),
      value: props.detail.operationId,
    });
  }
  return items;
});
</script>

<template>
  <LogExchangeDetail
    :open="open"
    :loading="loading"
    :detail="detail"
    :meta-items="metaItems"
    :title="title"
    :description="description"
    :aria-label="t('marketplace.adapterLogsPage.request')"
    show-headers
    json-bodies
    body-download-base-url="/adapter-http-logs"
    @update:open="$emit('update:open', $event)"
  >
    <template #overview-extra="{ detail: d }">
      <LogCodeBlock
        :label="t('marketplace.adapterLogsPage.url')"
        :text="d.url"
        max-height-class="max-h-28"
      />
      <LogMetaGrid v-if="extraMeta.length" :items="extraMeta" />
    </template>
  </LogExchangeDetail>
</template>
