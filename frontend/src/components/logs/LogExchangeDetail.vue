<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { InsetGroupLabel } from "@/components/ui/inset-group";
import LogDetailSheet from "./LogDetailSheet.vue";
import LogMetaGrid from "./LogMetaGrid.vue";
import LogCodeBlock from "./LogCodeBlock.vue";

const props = defineProps({
  open: { type: Boolean, default: false },
  loading: { type: Boolean, default: false },
  detail: { type: Object, default: null },
  title: { type: String, default: "" },
  description: { type: String, default: "" },
  ariaLabel: { type: String, default: "" },
  metaItems: { type: Array, default: () => [] },
  requestBodyKey: { type: String, default: "requestBody" },
  responseBodyKey: { type: String, default: "responseBody" },
  requestHeadersKey: { type: String, default: "requestHeadersJson" },
  responseHeadersKey: { type: String, default: "responseHeadersJson" },
  errorKey: { type: String, default: "errorMessage" },
  showHeaders: { type: Boolean, default: false },
  jsonBodies: { type: Boolean, default: false },
  overviewLabel: { type: String, default: "" },
  requestLabel: { type: String, default: "" },
  responseLabel: { type: String, default: "" },
  errorLabel: { type: String, default: "" },
  bodyLabel: { type: String, default: "" },
  headersLabel: { type: String, default: "" },
  /** Base path without trailing slash, e.g. /adapter-http-logs/12 */
  bodyDownloadBaseUrl: { type: String, default: "" },
});

defineEmits(["update:open"]);

const { t } = useI18n();

const labels = computed(() => ({
  overview: props.overviewLabel || t("components.logs.overview"),
  request: props.requestLabel || t("components.logs.request"),
  response: props.responseLabel || t("components.logs.response"),
  error: props.errorLabel || t("components.logs.error"),
  body: props.bodyLabel || t("components.logs.body"),
  headers: props.headersLabel || t("components.logs.headers"),
}));

const requestBody = computed(() => props.detail?.[props.requestBodyKey]);
const responseBody = computed(() => props.detail?.[props.responseBodyKey]);
const requestHeaders = computed(() => props.detail?.[props.requestHeadersKey]);
const responseHeaders = computed(
  () => props.detail?.[props.responseHeadersKey],
);
const errorMessage = computed(() => props.detail?.[props.errorKey]);
const useJson = computed(() => props.jsonBodies || props.showHeaders);
const detailsAvailable = computed(
  () =>
    props.detail?.detailsAvailable ?? props.detail?.DetailsAvailable ?? true,
);

const requestBodyTruncated = computed(
  () =>
    !!(
      props.detail?.requestBodyTruncated ?? props.detail?.RequestBodyTruncated
    ),
);
const responseBodyTruncated = computed(
  () =>
    !!(
      props.detail?.responseBodyTruncated ?? props.detail?.ResponseBodyTruncated
    ),
);

const detailId = computed(() => props.detail?.id ?? props.detail?.Id ?? null);

const requestDownloadUrl = computed(() => {
  if (
    !detailsAvailable.value ||
    !props.bodyDownloadBaseUrl ||
    detailId.value == null
  )
    return "";
  return `${props.bodyDownloadBaseUrl}/${detailId.value}/request-body`;
});
const responseDownloadUrl = computed(() => {
  if (
    !detailsAvailable.value ||
    !props.bodyDownloadBaseUrl ||
    detailId.value == null
  )
    return "";
  return `${props.bodyDownloadBaseUrl}/${detailId.value}/response-body`;
});
</script>

<template>
  <LogDetailSheet
    :open="open"
    :loading="loading"
    :title="title"
    :description="description"
    :aria-label="ariaLabel"
    @update:open="$emit('update:open', $event)"
  >
    <template v-if="$slots.actions" #actions>
      <slot name="actions" />
    </template>

    <Tabs
      v-if="detail"
      :key="detailId"
      default-value="overview"
      class="flex min-h-0 flex-1 flex-col"
    >
      <div class="shrink-0 border-b border-border px-6 py-2">
        <TabsList class="grid h-9 w-full grid-cols-3">
          <TabsTrigger value="overview">{{ labels.overview }}</TabsTrigger>
          <TabsTrigger value="request" :disabled="!detailsAvailable">{{
            labels.request
          }}</TabsTrigger>
          <TabsTrigger value="response" :disabled="!detailsAvailable">{{
            labels.response
          }}</TabsTrigger>
        </TabsList>
      </div>

      <div class="min-h-0 flex-1 overflow-y-auto px-4 py-4">
        <p
          v-if="!detailsAvailable"
          class="mb-4 text-sm text-muted-foreground"
          role="status"
        >
          {{ t("components.logs.detailsUnavailable") }}
        </p>
        <TabsContent value="overview" class="mt-0 space-y-5">
          <div>
            <InsetGroupLabel>{{ t("common.details") }}</InsetGroupLabel>
            <slot name="meta" :items="metaItems">
              <LogMetaGrid :items="metaItems" />
            </slot>
          </div>

          <slot name="overview-extra" :detail="detail" />

          <LogCodeBlock
            v-if="errorMessage"
            :label="labels.error"
            :text="errorMessage"
            tone="destructive"
            max-height-class="max-h-40"
          />
        </TabsContent>

        <TabsContent value="request" class="mt-0 space-y-5">
          <slot name="request-extra" :detail="detail" />
          <LogCodeBlock
            v-if="showHeaders"
            :label="labels.headers"
            :text="requestHeaders"
            json
            max-height-class="max-h-40"
          />
          <LogCodeBlock
            :label="labels.body"
            :text="requestBody || '-'"
            :json="useJson"
            :truncated="requestBodyTruncated"
            :download-url="requestDownloadUrl"
            :download-filename="`log-${detailId ?? 'body'}-request.json`"
          />
        </TabsContent>

        <TabsContent value="response" class="mt-0 space-y-5">
          <slot name="response-extra" :detail="detail" />
          <LogCodeBlock
            v-if="showHeaders"
            :label="labels.headers"
            :text="responseHeaders"
            json
            max-height-class="max-h-40"
          />
          <LogCodeBlock
            :label="labels.body"
            :text="responseBody || '-'"
            :json="useJson"
            :truncated="responseBodyTruncated"
            :download-url="responseDownloadUrl"
            :download-filename="`log-${detailId ?? 'body'}-response.json`"
          />
        </TabsContent>
      </div>
    </Tabs>
  </LogDetailSheet>
</template>
