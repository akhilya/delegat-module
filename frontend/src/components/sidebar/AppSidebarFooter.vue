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
  <SidebarFooter class="border-t border-sidebar-border p-2">
    <div
      class="overflow-hidden rounded-xl bg-card p-1 ring-1 ring-inset ring-sidebar-border group-data-[collapsible=icon]:rounded-lg group-data-[collapsible=icon]:bg-transparent group-data-[collapsible=icon]:p-0 group-data-[collapsible=icon]:ring-0"
    >
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
    </div>
  </SidebarFooter>
</template>
