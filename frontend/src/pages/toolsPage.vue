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
      breadcrumbs: [{ label: "Инструменты" }],
    };
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      title="Инструменты"
      description="Дополнительные функции и настройки"
    />

    <PageSection
      empty
      :empty-icon="Wrench"
      empty-title="Пока нет дополнительных инструментов"
      empty-description="Категории и атрибуты доступны через меню пользователя"
    />
  </Page>
</template>
