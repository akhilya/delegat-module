<script>
import { RouterView } from "vue-router";
import { Toaster } from "vue-sonner";
import "vue-sonner/style.css";
import ImportProgressPanel from "@/components/ImportProgressPanel.vue";

export default {
  components: {
    RouterView,
    Toaster,
    ImportProgressPanel,
  },
};
</script>

<template>
  <RouterView v-slot="{ Component, route }">
    <component :is="Component" :key="route.path" />
  </RouterView>
  <Toaster />
  <ImportProgressPanel />
</template>
