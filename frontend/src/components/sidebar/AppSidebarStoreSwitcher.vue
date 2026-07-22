<script setup>
import { computed } from "vue";
import { RouterLink } from "vue-router";
import { t } from "@/i18n";
import { PAGE_ADMIN_STORES } from "@/router";
import {
  SidebarMenu,
  SidebarMenuItem,
} from "@/components/ui/sidebar";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const props = defineProps({
  memberships: { type: Array, default: () => [] },
  selectedStoreId: { type: [Number, String, null], default: null },
  /** When memberships are empty: "no-stores" | "no-membership" | null (loading). */
  emptyMode: {
    type: [String, null],
    default: null,
    validator: (v) => v == null || ["no-membership", "no-stores"].includes(v),
  },
});

const emit = defineEmits(["select-store"]);

const hasMemberships = computed(() => props.memberships.length > 0);
const isNoStoresAdmin = computed(() => props.emptyMode === "no-stores");
const showEmptyState = computed(() => !hasMemberships.value && props.emptyMode != null);
</script>

<template>
  <SidebarMenu v-if="hasMemberships">
    <SidebarMenuItem class="p-0">
      <Select
        :key="selectedStoreId != null ? String(selectedStoreId) : 'none'"
        :model-value="selectedStoreId != null ? String(selectedStoreId) : ''"
        @update:model-value="emit('select-store', Number($event))"
      >
        <SelectTrigger class="h-9 w-full bg-background">
          <SelectValue :placeholder="t('components.appSidebarStoreSwitcher.storePlaceholder')" />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectLabel>{{ t('components.appSidebarStoreSwitcher.storesLabel') }}</SelectLabel>
            <SelectItem
              v-for="store in memberships"
              :key="store.id"
              :value="String(store.id)"
            >
              {{ store.name }}
            </SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
    </SidebarMenuItem>
  </SidebarMenu>

  <div
    v-else-if="showEmptyState"
    class="rounded-md border border-dashed bg-muted/30 px-2.5 py-2 group-data-[collapsible=icon]:hidden"
  >
    <p class="text-xs font-medium leading-snug text-foreground">
      {{
        isNoStoresAdmin
          ? t("components.appSidebarStoreSwitcher.noStoresTitle")
          : t("components.appSidebarStoreSwitcher.noMembershipTitle")
      }}
    </p>
    <p class="mt-1 text-[11px] leading-snug text-muted-foreground">
      {{
        isNoStoresAdmin
          ? t("components.appSidebarStoreSwitcher.noStoresBody")
          : t("components.appSidebarStoreSwitcher.noMembershipBody")
      }}
    </p>
    <RouterLink
      v-if="isNoStoresAdmin"
      :to="PAGE_ADMIN_STORES"
      class="mt-2 inline-block text-[11px] font-medium text-primary underline-offset-2 hover:underline"
    >
      {{ t("components.appSidebarStoreSwitcher.noStoresLink") }}
    </RouterLink>
  </div>
</template>
