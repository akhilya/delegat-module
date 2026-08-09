<script setup>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import ProductFileManager from "@/components/products/ProductFileManager.vue";
import { t } from "@/i18n";

const breadcrumbs = [{ label: t("pages.storeStoragePage.breadcrumb") }];
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <PageSection class="flex min-h-[calc(100vh-12rem)] flex-1 flex-col overflow-hidden rounded-xl border bg-card p-0">
      <ProductFileManager
        scope="store"
        :storage-label="t('pages.storeStoragePage.storageLabel')"
      />
    </PageSection>
  </AppPageChrome>
</template>
