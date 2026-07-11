<script setup>
import { LogOut, Moon, Sun } from "lucide-vue-next";
import {
  SidebarFooter,
  SidebarMenu,
  SidebarMenuItem,
  SidebarMenuButton,
} from "@/components/ui/sidebar";

defineProps({
  isDark: { type: Boolean, default: false },
  version: { type: String, default: "" },
});

const emit = defineEmits(["toggle-theme", "logout"]);
</script>

<template>
  <SidebarFooter>
    <SidebarMenu>
      <SidebarMenuItem>
        <SidebarMenuButton @click="emit('toggle-theme')">
          <Sun v-if="isDark" />
          <Moon v-else />
          <span>{{ isDark ? "Светлая тема" : "Тёмная тема" }}</span>
        </SidebarMenuButton>
      </SidebarMenuItem>
      <SidebarMenuItem>
        <SidebarMenuButton @click="emit('logout')">
          <LogOut />
          <span>Выйти</span>
        </SidebarMenuButton>
      </SidebarMenuItem>
      <SidebarMenuItem v-if="version">
        <span class="px-2 text-[10px] text-muted-foreground">v{{ version }}</span>
      </SidebarMenuItem>
    </SidebarMenu>
  </SidebarFooter>
</template>