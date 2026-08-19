<script setup>
import PageSection from "@/components/pageSection.vue";
import LogTableFrame from "./LogTableFrame.vue";
import LogLoadMoreFooter from "./LogLoadMoreFooter.vue";
import LogScrollLoadMore from "./LogScrollLoadMore.vue";

defineProps({
  loading: { type: Boolean, default: false },
  empty: { type: Boolean, default: false },
  emptyIcon: { type: [Object, Function], default: undefined },
  emptyTitle: { type: String, default: "" },
  emptyDescription: { type: String, default: "" },
  /** Fill remaining page height; scroll inside the table body */
  fill: { type: Boolean, default: true },
  /** Show footer when there are rows */
  showFooter: { type: Boolean, default: false },
  footerCount: { type: Number, default: 0 },
  footerTotal: { type: Number, default: null },
  footerLabel: { type: String, default: "" },
  hasMore: { type: Boolean, default: false },
  moreLoading: { type: Boolean, default: false },
  loadMoreLabel: { type: String, default: "" },
  selectedCount: { type: Number, default: 0 },
  downloading: { type: Boolean, default: false },
  deleting: { type: Boolean, default: false },
  class: { type: null, required: false },
});

defineEmits(["load-more", "download", "delete", "clear-selection"]);
</script>

<template>
  <PageSection
    :loading="loading"
    :empty="empty"
    :empty-icon="emptyIcon"
    :empty-title="emptyTitle"
    :empty-description="emptyDescription"
    :class="[
      fill ? '!min-h-0 mb-0 flex min-h-0 flex-1 flex-col p-px' : '',
      $props.class,
    ]"
  >
    <LogTableFrame :class="fill ? 'min-h-0 flex-1' : ''">
      <slot />
      <LogScrollLoadMore
        v-if="hasMore"
        :loading="moreLoading"
        :label="loadMoreLabel"
        @load-more="$emit('load-more')"
      />
      <template v-if="showFooter || $slots.footer" #footer>
        <slot name="footer">
          <LogLoadMoreFooter
            :count="footerCount"
            :total="footerTotal"
            :label="footerLabel"
            :selected-count="selectedCount"
            :downloading="downloading"
            :deleting="deleting"
            @download="$emit('download')"
            @delete="$emit('delete')"
            @clear-selection="$emit('clear-selection')"
          />
        </slot>
      </template>
    </LogTableFrame>
  </PageSection>
</template>
