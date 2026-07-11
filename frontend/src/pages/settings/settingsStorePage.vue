<script setup>
import { computed, onMounted, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsPageHeader from "@/components/settings/SettingsPageHeader.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import InlineEditableValue from "@/components/settings/InlineEditableValue.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Loader2, Save } from "lucide-vue-next";

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
      description="Параметры текущего магазина, выбранного в сайдбаре"
    />

    <PageSection :loading="isLoading" class="!min-h-0">
      <SettingsFormSection
        title="Текущий магазин"
        description="Базовая конфигурация выбранного магазина."
      >
        <SettingsFormField
          label="Название"
          :changed="hasStoreChanges"
        >
          <InlineEditableValue
            :model-value="storeNameDraft"
            :disabled="!selectedStore || !canManageSelectedStore || isSaving"
            placeholder="Магазин не выбран"
            empty-label="Магазин не выбран"
            @update:model-value="storeNameDraft = $event"
          />
          <template #description>
            <span v-if="selectedStore && !canManageSelectedStore">
              Для изменения нужен администратор магазина.
            </span>
            <span v-else-if="!selectedStore">
              Выберите магазин в сайдбаре.
            </span>
            <span v-else>
              Отображается в интерфейсе и в списке магазинов.
            </span>
          </template>
        </SettingsFormField>

        <SettingsFormField label="ID магазина">
          <Badge variant="secondary">{{ selectedStore?.id || "—" }}</Badge>
          <template #description>
            Служебный идентификатор магазина в системе.
          </template>
        </SettingsFormField>

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
      </SettingsFormSection>
    </PageSection>
  </div>
</template>