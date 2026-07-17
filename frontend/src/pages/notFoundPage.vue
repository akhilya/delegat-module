<script setup>
import { useRouter } from "vue-router";
import { Button } from "@/components/ui/button";
import Page from "@/components/page.vue";
import { PAGE_PRODUCTS } from "@/router";
import { ArrowLeft, Home } from "lucide-vue-next";

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
  <Page class="flex min-h-0 flex-1 flex-col">
    <div class="flex flex-1 flex-col items-center justify-center px-4 py-12">
      <div class="w-full max-w-md text-center">
        <p class="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Ошибка 404
        </p>
        <h1 class="mt-2 text-2xl font-semibold tracking-tight text-foreground">
          Страница не найдена
        </h1>
        <p class="mt-2 text-sm leading-relaxed text-muted-foreground">
          Запрошенный адрес не существует или был перемещён.
        </p>

        <div class="mt-6 flex flex-wrap items-center justify-center gap-2.5">
          <Button variant="outline" @click="goBack">
            <ArrowLeft class="size-4" />
            Вернуться
          </Button>
          <Button as-child>
            <RouterLink :to="PAGE_PRODUCTS">
              <Home class="size-4" />
              Главная
            </RouterLink>
          </Button>
        </div>
      </div>
    </div>
  </Page>
</template>
