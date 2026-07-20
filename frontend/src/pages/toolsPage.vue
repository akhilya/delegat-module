<script>
import NavigationBar from "@/components/navigationBar.vue";
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { PageHeader } from "@/components/ui/page-header";
import { Wrench } from "lucide-vue-next";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    PageHeader,
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
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      :title="$t('pages.toolsPage.title')"
      :description="$t('pages.toolsPage.description')"
    />

    <PageSection
      empty
      :empty-icon="Wrench"
      :empty-title="$t('pages.toolsPage.emptyTitle')"
      :empty-description="$t('pages.toolsPage.emptyDescription')"
    />
  </Page>
</template>
