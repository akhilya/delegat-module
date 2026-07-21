<script setup>
import PageSection from "@/components/pageSection.vue";
import LogTableFrame from "./LogTableFrame.vue";
import LogLoadMoreFooter from "./LogLoadMoreFooter.vue";

defineProps({
  loading: { type: Boolean, default: false },
  empty: { type: Boolean, default: false },
  emptyIcon: { type: [Object, Function], default: undefined },
  emptyTitle: { type: String, default: "" },
  emptyDescription: { type: String, default: "" },
  /** Show footer when there are rows */
  showFooter: { type: Boolean, default: false },
  footerCount: { type: Number, default: 0 },
  footerTotal: { type: Number, default: null },
  footerLabel: { type: String, default: "" },
  hasMore: { type: Boolean, default: false },
  moreLoading: { type: Boolean, default: false },
  loadMoreLabel: { type: String, default: "" },
});

defineEmits(["load-more"]);
</script>

<template>
  <PageSection
    :loading="loading"
    :empty="empty"
    :empty-icon="emptyIcon"
    :empty-title="emptyTitle"
    :empty-description="emptyDescription"
  >
    <LogTableFrame>
      <slot />
      <template v-if="showFooter || $slots.footer" #footer>
        <slot name="footer">
          <LogLoadMoreFooter
            :count="footerCount"
            :total="footerTotal"
            :label="footerLabel"
            :has-more="hasMore"
            :loading="moreLoading"
            :load-more-label="loadMoreLabel"
            @load-more="$emit('load-more')"
          />
        </slot>
      </template>
    </LogTableFrame>
  </PageSection>
</template>
