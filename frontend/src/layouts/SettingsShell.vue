<script setup>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import SettingsLayout from "@/components/SettingsLayout.vue";
</script>

<template>
  <Page class="flex h-svh flex-col overflow-hidden">
    <NavigationBar class="shrink-0" />
    <SettingsLayout class="min-h-0 flex-1">
      <RouterView />
    </SettingsLayout>
  </Page>
</template>
