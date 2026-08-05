<script setup>
import { useRouter } from "vue-router";
import { Button } from "@/components/ui/button";
import AppPageChrome from "@/components/AppPageChrome.vue";
import { PAGE_PRODUCTS } from "@/router";
import { ArrowLeft, Home } from "lucide-vue-next";
import { t } from "@/i18n";

const router = useRouter();

function goBack() {
  if (window.history.length > 1) {
    router.back();
    return;
  }
  router.push(PAGE_PRODUCTS);
}
</script>

<template>
  <AppPageChrome>
    <div class="flex flex-1 flex-col items-center justify-center px-4 py-12">
      <div class="w-full max-w-md text-center">
        <p class="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          {{ t("pages.notFoundPage.errorCode") }}
        </p>
        <h1 class="mt-2 text-2xl font-semibold tracking-tight text-foreground">
          {{ t("pages.notFoundPage.title") }}
        </h1>
        <p class="mt-2 text-sm leading-relaxed text-muted-foreground">
          {{ t("pages.notFoundPage.description") }}
        </p>

        <div class="mt-6 flex flex-wrap items-center justify-center gap-2.5">
          <Button variant="outline" @click="goBack">
            <ArrowLeft class="size-4" />
            {{ t("pages.notFoundPage.goBack") }}
          </Button>
          <Button as-child>
            <RouterLink :to="PAGE_PRODUCTS">
              <Home class="size-4" />
              {{ t("pages.notFoundPage.home") }}
            </RouterLink>
          </Button>
        </div>
      </div>
    </div>
  </AppPageChrome>
</template>
