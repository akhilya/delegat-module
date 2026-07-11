<script setup>
import { useRoute } from "vue-router";
import { useStore } from "vuex";
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuItem,
  SidebarMenuButton,
} from "@/components/ui/sidebar";
import { isActiveRoute } from "@/lib/isActiveRoute";

const props = defineProps({
  label: { type: String, required: true },
  items: { type: Array, required: true },
});

const route = useRoute();
const store = useStore();

function handleAction(action) {
  if (action === "toggleAi") store.commit("aiChat/toggle");
  if (action === "toggleOps") store.dispatch("operations/togglePanel");
}
</script>

<template>
  <SidebarGroup>
    <SidebarGroupLabel>{{ label }}</SidebarGroupLabel>
    <SidebarGroupContent>
      <SidebarMenu>
        <SidebarMenuItem v-for="item in items" :key="item.id">
          <SidebarMenuButton
            v-if="item.type === 'action'"
            @click="handleAction(item.action)"
          >
            <component :is="item.icon" />
            <span>{{ item.label }}</span>
          </SidebarMenuButton>
          <SidebarMenuButton
            v-else
            as-child
            :is-active="isActiveRoute(route, item.to)"
          >
            <RouterLink :to="item.to">
              <component :is="item.icon" />
              <span>{{ item.label }}</span>
            </RouterLink>
          </SidebarMenuButton>
        </SidebarMenuItem>
      </SidebarMenu>
    </SidebarGroupContent>
  </SidebarGroup>
</template>