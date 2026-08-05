<script setup>
import { onMounted, ref } from "vue";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import ProductFileManager from "@/components/products/ProductFileManager.vue";
import { EmptyState } from "@/components/ui/empty-state";
import { http } from "@/main";
import { displaySonnerError } from "@/store/sonnerHelper";
import { t } from "@/i18n";

const files = ref([]);
const isLoading = ref(false);
const breadcrumbs = [{ label: t("pages.storeStoragePage.breadcrumb") }];

async function fetchFiles() {
  isLoading.value = true;
  try {
    const response = await http.get("/store-storage/files", { cache: false });
    files.value = response.data?.value || response.data || [];
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isLoading.value = false;
  }
}

onMounted(fetchFiles);
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <PageSection class="flex min-h-[calc(100vh-12rem)] flex-1 flex-col overflow-hidden rounded-xl border bg-card p-0">
      <EmptyState
        v-if="isLoading"
        loading
        :framed="false"
        class="min-h-[24rem]"
      />
      <ProductFileManager
        v-else
        scope="store"
        :storage-label="t('pages.storeStoragePage.storageLabel')"
        :files="files"
        @filesChanged="fetchFiles"
      />
    </PageSection>
  </AppPageChrome>
</template>
