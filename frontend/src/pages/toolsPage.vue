<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Wrench } from "lucide-vue-next";

export default {
  components: {
    AppPageChrome,
    PageSection,
    Wrench,
  },
  data() {
    return {
      breadcrumbs: [{ label: this.$t("pages.toolsPage.breadcrumb") }],
    };
  },
};
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <PageSection
      empty
      :empty-icon="Wrench"
      :empty-title="$t('pages.toolsPage.emptyTitle')"
      :empty-description="$t('pages.toolsPage.emptyDescription')"
    />
  </AppPageChrome>
</template>
