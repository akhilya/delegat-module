<script setup>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import SettingsLayout from "@/components/SettingsLayout.vue";
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <SettingsLayout>
      <RouterView />
    </SettingsLayout>
  </Page>
</template>