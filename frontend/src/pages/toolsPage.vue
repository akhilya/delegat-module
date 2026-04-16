<script>
import NavigationBar from "@/components/navigationBar.vue";
import Page from "@/components/page.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Card } from "@/components/ui/card";
import { Wrench } from "lucide-vue-next";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    Card,
    Wrench,
  },
  data() {
    return {
      breadcrumbs: [{ label: "Инструменты" }],
    };
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <div class="mb-4">
      <h1 class="text-2xl font-semibold">Инструменты</h1>
      <p class="text-muted-foreground text-sm mt-1">
        Дополнительные функции и настройки
      </p>
    </div>

    <Card class="h-full w-full grow mb-4 p-4 gap-0">
      <div class="flex flex-col items-center justify-center h-full text-center text-muted-foreground">
        <Wrench class="size-12 mb-4 opacity-50" />
        <p>Пока нет дополнительных инструментов</p>
        <p class="text-sm">Категории и атрибуты доступны через меню пользователя</p>
      </div>
    </Card>
  </Page>
</template>
