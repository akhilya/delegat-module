import { ref, computed } from "vue";
import { useStore } from "vuex";
import { getBaseCurrency } from "@/lib/currency";

const memberships = ref([]);
const selectedStoreId = ref(null);
const loaded = ref(false);

export function useSelectedStore() {
  const store = useStore();

  async function loadMemberships() {
    if (loaded.value) return memberships.value;
    await new Promise((resolve) => {
      store.dispatch("getMe", {
        onSuccess: (r) => {
          selectedStoreId.value = r.data.value?.selectedStoreId ?? null;
          resolve();
        },
        onError: () => resolve(),
      });
    });
    return new Promise((resolve) => {
      store.dispatch("getMyMemberships", {
        onSuccess: (response) => {
          memberships.value = response.data.value || [];
          loaded.value = true;
          resolve(memberships.value);
        },
        onError: () => {
          memberships.value = [];
          loaded.value = true;
          resolve([]);
        },
      });
    });
  }

  const selectedStore = computed(() => {
    const id = selectedStoreId.value;
    if (!id) return null;
    return memberships.value.find((s) => s.id === id) || null;
  });

  const extendedPriceParameters = computed(() => Boolean(selectedStore.value?.extendedPriceParameters));
  const storeCurrencies = computed(() => selectedStore.value?.currencies || []);
  const storeBaseCurrency = computed(() => getBaseCurrency(selectedStore.value));
  const storeTimezone = computed(() => selectedStore.value?.timezone || "Europe/Moscow");

  return {
    memberships,
    selectedStore,
    extendedPriceParameters,
    storeCurrencies,
    storeBaseCurrency,
    storeTimezone,
    loadMemberships,
  };
}