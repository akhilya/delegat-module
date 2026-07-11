<script setup>
import { computed, onMounted, ref } from "vue";
import PageSection from "@/components/pageSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { PageHeader } from "@/components/ui/page-header";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { FieldGroup } from "@/components/ui/field";
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
  <div class="flex flex-col">
    <PageHeader
      title="Магазин"
      description="Название магазина, выбранного в сайдбаре"
    />

    <PageSection :loading="isLoading" class="!min-h-0">
      <FieldGroup class="max-w-xl gap-5">
        <SettingsFormField label="Название" label-for="store-name">
          <Input
            id="store-name"
            v-model="storeNameDraft"
            :disabled="!selectedStore || !canManageSelectedStore || isSaving"
            placeholder="Магазин не выбран"
          />
          <template v-if="selectedStore && !canManageSelectedStore" #description>
            Для изменения нужен администратор магазина.
          </template>
          <template v-else-if="!selectedStore" #description>
            Выберите магазин в сайдбаре.
          </template>
        </SettingsFormField>

        <div class="flex flex-wrap items-center gap-3">
          <span v-if="hasStoreChanges" class="text-sm text-muted-foreground">
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
        </div>
      </FieldGroup>
    </PageSection>
  </div>
</template>