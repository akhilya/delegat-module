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

const files = ref([]);
const isLoading = ref(false);
const breadcrumbs = [{ label: "Хранилище" }];

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
      title="Хранилище"
      description="Общие файлы магазина, не привязанные к конкретному товару"
    />

    <PageSection class="flex min-h-[calc(100vh-12rem)] flex-1 flex-col overflow-hidden rounded-xl border bg-card p-0">
      <div v-if="isLoading" class="flex min-h-[24rem] items-center justify-center text-sm text-muted-foreground">
        Загрузка хранилища...
      </div>
      <ProductFileManager
        v-else
        scope="store"
        storage-label="Общее хранилище магазина"
        :files="files"
        @filesChanged="fetchFiles"
      />
    </PageSection>
  </Page>
</template>
