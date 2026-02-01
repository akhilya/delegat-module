<script>
import NavigationBar from "@/components/navigationBar.vue";
import Page from "@/components/page.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import GoBackButton from "@/components/goBackButton.vue";
import HomeButton from "@/components/homeButton.vue";
import { Wrench } from "lucide-vue-next";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    GoBackButton,
    HomeButton,
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
    <NavigationBar>
      <template #pre-left>
        <GoBackButton />
      </template>
      <template #pro-left>
        <HomeButton />
      </template>
    </NavigationBar>
    <Breadcrumbs :items="breadcrumbs" />
    <div class="flex-1 flex items-center justify-center text-muted-foreground">
      <div class="text-center">
        <Wrench class="w-12 h-12 mx-auto mb-4 opacity-50" />
        <p>Инструменты</p>
        <p class="text-sm">Раздел в разработке</p>
      </div>
    </div>
  </Page>
</template>
