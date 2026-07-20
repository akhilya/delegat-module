<script setup>
import { onMounted, ref } from "vue";
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import ProductFileManager from "@/components/products/ProductFileManager.vue";
import { PageHeader } from "@/components/ui/page-header";
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
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      :title="t('pages.storeStoragePage.title')"
      :description="t('pages.storeStoragePage.description')"
    />

    <PageSection class="flex min-h-[calc(100vh-12rem)] flex-1 flex-col overflow-hidden rounded-xl border bg-card p-0">
      <div v-if="isLoading" class="flex min-h-[24rem] items-center justify-center text-sm text-muted-foreground">
        {{ t("pages.storeStoragePage.loading") }}
      </div>
      <ProductFileManager
        v-else
        scope="store"
        :storage-label="t('pages.storeStoragePage.storageLabel')"
        :files="files"
        @filesChanged="fetchFiles"
      />
    </PageSection>
  </Page>
</template>
