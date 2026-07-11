<script setup>
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";

import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";


import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import Input from "@/components/ui/input/Input.vue";
import { PageHeader } from "@/components/ui/page-header";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  PAGE_CATEGORIES,
  PAGE_MARKETPLACE,
  PAGE_STORE_ATTRIBUTES,
  PAGE_TOOLS,
} from "@/router";
import {
  ArrowRight,
  FolderOpen,
  Globe,
  Loader2,
  Save,
  ShoppingBag,
  Wand2,
} from "lucide-vue-next";


const breadcrumbs = [
  { label: "Настройки", to: "/settings" },
  { label: "Магазин" },
];
const me = ref(null);
const memberships = ref([]);
const isLoading = ref(false);
const isSaving = ref(false);
const storeNameDraft = ref("");

const selectedStore = computed(() =>
  memberships.value.find((store) => store.id === me.value?.selectedStoreId)
);

const canManageSelectedStore = computed(() =>
  me.value?.isAdmin || selectedStore.value?.isStoreAdmin
);

const hasStoreChanges = computed(() =>
  Boolean(selectedStore.value) && storeNameDraft.value.trim() !== (selectedStore.value?.name || "")
);

const storeSections = [
  {
    title: "Адаптеры",
    description: "Подключения к маркетплейсам и внешним витринам, экспорт товаров и категорий.",
    to: PAGE_MARKETPLACE,
    icon: ShoppingBag,
  },
  {
    title: "Категории",
    description: "Дерево категорий магазина и связанные свойства товаров.",
    to: PAGE_CATEGORIES,
    icon: FolderOpen,
  },
  {
    title: "Глобальные атрибуты",
    description: "Общие характеристики, которые используются в товарах и маппингах.",
    to: PAGE_STORE_ATTRIBUTES,
    icon: Globe,
  },
  {
    title: "Инструменты",
    description: "Служебные операции текущего магазина.",
    to: PAGE_TOOLS,
    icon: Wand2,
  },
];

async function fetchData() {
  isLoading.value = true;
  try {
    const [meResponse, membershipsResponse] = await Promise.all([
      http.get("/auth/me", { cache: false }),
      http.get("/users/me/memberships", { cache: false }),
    ]);
    me.value = meResponse.data?.value || meResponse.data;
    memberships.value = membershipsResponse.data?.value || membershipsResponse.data || [];
    storeNameDraft.value = selectedStore.value?.name || "";
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isLoading.value = false;
  }
}

async function saveStoreSettings() {
  if (!selectedStore.value) return;
  const name = storeNameDraft.value.trim();
  if (!name) {
    displaySonnerError("Название магазина не может быть пустым");
    return;
  }

  isSaving.value = true;
  try {
    await http.patch(`/stores/${selectedStore.value.id}`, { name }, { cache: false });
    displaySonnerSuccess("Настройки магазина сохранены");
    await fetchData();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isSaving.value = false;
  }
}

onMounted(fetchData);
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      title="Настройки магазина"
      description="Параметры текущего магазина и его рабочие разделы"
    />

    <PageSection :loading="isLoading" class="space-y-5">
      <section class="grid gap-4 lg:grid-cols-[260px_minmax(0,1fr)]">
        <div>
          <h2 class="text-sm font-semibold">Текущий магазин</h2>
          <p class="mt-1 text-sm text-muted-foreground">
            Базовая конфигурация магазина, выбранного в сайдбаре.
          </p>
        </div>

        <div class="grid gap-4">
          <div class="grid gap-3 border-y py-3">
            <div class="grid gap-2">
              <label for="store-settings-name" class="text-sm font-medium">Название</label>
              <Input
                id="store-settings-name"
                v-model="storeNameDraft"
                :disabled="!selectedStore || !canManageSelectedStore || isSaving"
                placeholder="Магазин не выбран"
              />
            </div>
            <div class="flex items-center justify-between text-sm">
              <span class="text-muted-foreground">ID магазина</span>
              <Badge variant="secondary">{{ selectedStore?.id || "-" }}</Badge>
            </div>
            <p v-if="selectedStore && !canManageSelectedStore" class="text-xs text-muted-foreground">
              Для изменения настроек нужен администратор магазина.
            </p>
            <p v-if="!selectedStore" class="text-xs text-muted-foreground">
              Выберите магазин в сайдбаре, чтобы увидеть его настройки.
            </p>
          </div>
          <div class="flex items-center justify-end gap-3">
            <span v-if="hasStoreChanges" class="text-sm text-muted-foreground">
              Есть несохранённые изменения
            </span>
            <Button
              :disabled="!selectedStore || !canManageSelectedStore || !hasStoreChanges || isSaving"
              @click="saveStoreSettings"
            >
              <Loader2 v-if="isSaving" class="mr-2 size-4 animate-spin" />
              <Save v-else class="mr-2 size-4" />
              Сохранить настройки магазина
            </Button>
          </div>
        </div>
      </section>

      <section class="grid gap-4 border-t pt-5 lg:grid-cols-[260px_minmax(0,1fr)]">
          <div>
            <h2 class="text-sm font-semibold">Разделы магазина</h2>
            <p class="mt-1 text-sm text-muted-foreground">
              Всё, что относится к текущему магазину: адаптеры, категории, атрибуты и инструменты.
            </p>
          </div>
          <div class="divide-y border-y">
            <RouterLink
              v-for="section in storeSections"
              :key="section.title"
              :to="section.to"
              class="grid gap-3 py-3 transition-colors hover:bg-muted/40 md:grid-cols-[auto_minmax(0,1fr)_auto]"
            >
              <component :is="section.icon" class="mt-0.5 size-4 text-muted-foreground" />
              <div>
                <div class="text-sm font-medium">{{ section.title }}</div>
                <div class="text-sm text-muted-foreground">{{ section.description }}</div>
              </div>
              <ArrowRight class="mt-0.5 size-4 text-muted-foreground" />
            </RouterLink>
          </div>
      </section>
    </PageSection>
  </Page>
</template>
