<script setup>
import { computed, onMounted, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsPageHeader from "@/components/settings/SettingsPageHeader.vue";
import SettingsSection from "@/components/settings/SettingsSection.vue";
import SettingsNavCard from "@/components/settings/SettingsNavCard.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import Input from "@/components/ui/input/Input.vue";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  PAGE_CATEGORIES,
  PAGE_MARKETPLACE,
  PAGE_STORE_ATTRIBUTES,
  PAGE_TOOLS,
} from "@/router";
import {
  FolderOpen,
  Globe,
  Loader2,
  Save,
  ShoppingBag,
  Wand2,
} from "lucide-vue-next";

const me = ref(null);
const memberships = ref([]);
const isLoading = ref(false);
const isSaving = ref(false);
const storeNameDraft = ref("");

const selectedStore = computed(() =>
  memberships.value.find((store) => store.id === me.value?.selectedStoreId),
);

const canManageSelectedStore = computed(() =>
  me.value?.isAdmin || selectedStore.value?.isStoreAdmin,
);

const hasStoreChanges = computed(() =>
  Boolean(selectedStore.value) && storeNameDraft.value.trim() !== (selectedStore.value?.name || ""),
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
  <div>
    <SettingsPageHeader
      title="Магазин"
      description="Параметры текущего магазина и его рабочие разделы"
    />

    <PageSection :loading="isLoading" class="!min-h-0">
      <SettingsSection
        title="Текущий магазин"
        description="Базовая конфигурация магазина, выбранного в сайдбаре."
      >
        <div class="grid gap-4">
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
            <Badge variant="secondary">{{ selectedStore?.id || "—" }}</Badge>
          </div>
          <p v-if="selectedStore && !canManageSelectedStore" class="text-xs text-muted-foreground">
            Для изменения настроек нужен администратор магазина.
          </p>
          <p v-if="!selectedStore" class="text-xs text-muted-foreground">
            Выберите магазин в сайдбаре, чтобы увидеть его настройки.
          </p>
        </div>
        <template #footer>
          <span v-if="hasStoreChanges" class="mr-auto text-sm text-muted-foreground">
            Есть несохранённые изменения
          </span>
          <Button
            :disabled="!selectedStore || !canManageSelectedStore || !hasStoreChanges || isSaving"
            @click="saveStoreSettings"
          >
            <Loader2 v-if="isSaving" class="mr-2 size-4 animate-spin" />
            <Save v-else class="mr-2 size-4" />
            Сохранить
          </Button>
        </template>
      </SettingsSection>

      <SettingsSection
        title="Разделы магазина"
        description="Всё, что относится к текущему магазину: адаптеры, категории, атрибуты и инструменты."
      >
        <div class="grid gap-2">
          <SettingsNavCard
            v-for="section in storeSections"
            :key="section.title"
            :title="section.title"
            :description="section.description"
            :to="section.to"
            :icon="section.icon"
          />
        </div>
      </SettingsSection>
    </PageSection>
  </div>
</template>