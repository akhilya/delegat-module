<script setup>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import ProductFileManager from "@/components/products/ProductFileManager.vue";
import { t } from "@/i18n";
</script>

<template>
  <AppPageChrome fill-viewport>
    <PageSection class="mb-0 flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl border bg-card p-0">
      <ProductFileManager
        scope="store"
        :storage-label="t('pages.storeStoragePage.storageLabel')"
      />
    </PageSection>
  </AppPageChrome>
</template>
