<script setup>
import { LogOut, Moon, Sun } from "lucide-vue-next";
import {
  SidebarFooter,
  SidebarGroupLabel,
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
        <SidebarMenuButton
          :tooltip="isDark ? 'Светлая тема' : 'Тёмная тема'"
          @click="emit('toggle-theme')"
        >
          <Sun v-if="isDark" />
          <Moon v-else />
          <span>{{ isDark ? "Светлая тема" : "Тёмная тема" }}</span>
        </SidebarMenuButton>
      </SidebarMenuItem>
      <SidebarMenuItem>
        <SidebarMenuButton tooltip="Выйти" @click="emit('logout')">
          <LogOut />
          <span>Выйти</span>
        </SidebarMenuButton>
      </SidebarMenuItem>
      <SidebarMenuItem v-if="version">
        <SidebarGroupLabel class="h-auto py-1 text-[10px]">
          v{{ version }}
        </SidebarGroupLabel>
      </SidebarMenuItem>
    </SidebarMenu>
  </SidebarFooter>
</template>