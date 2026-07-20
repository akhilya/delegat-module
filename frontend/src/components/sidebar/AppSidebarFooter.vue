<script setup>
import { computed } from "vue";
import {
  SidebarFooter,
  SidebarMenu,
  SidebarMenuItem,
  useSidebar,
} from "@/components/ui/sidebar";
import UserAccountMenu from "@/components/UserAccountMenu.vue";

defineProps({
  userName: { type: String, default: "" },
  version: { type: String, default: "" },
});

const { isMobile, state } = useSidebar();

const menuSide = computed(() => {
  if (isMobile.value) return "bottom";
  if (state.value === "collapsed") return "right";
  return "top";
});
</script>

<template>
  <SidebarFooter>
    <SidebarMenu>
      <SidebarMenuItem>
        <UserAccountMenu
          variant="sidebar"
          :user-name="userName"
          :version="version"
          :side="menuSide"
          align="start"
        />
      </SidebarMenuItem>
    </SidebarMenu>
  </SidebarFooter>
</template>
