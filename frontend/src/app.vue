<script>
import { RouterView } from "vue-router";
import { Toaster } from "vue-sonner";
import "vue-sonner/style.css";
import ImportProgressPanel from "@/components/ImportProgressPanel.vue";
import LoadingBar from "@/components/LoadingBar.vue";

export default {
  components: {
    RouterView,
    Toaster,
    ImportProgressPanel,
    LoadingBar,
  },
};
</script>

<template>
  <LoadingBar ref="loadingBar" />
  <RouterView v-slot="{ Component, route }">
    <component :is="Component" :key="route.path" />
  </RouterView>
  <Toaster />
  <ImportProgressPanel />
</template>
