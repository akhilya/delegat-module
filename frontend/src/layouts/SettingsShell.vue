<script setup>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import SettingsLayout from "@/components/SettingsLayout.vue";
</script>

<template>
  <!-- No h-svh / overflow lock: SidebarInset scrolls the page. Fill-height routes use meta.fillHeight. -->
  <Page class="flex flex-col">
    <NavigationBar class="shrink-0" />
    <SettingsLayout>
      <RouterView />
    </SettingsLayout>
  </Page>
</template>
