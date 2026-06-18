<script setup>
import Button from "@/components/ui/button/Button.vue";
import { PanelLeft } from "lucide-vue-next";
import { useSidebar } from "./sidebar";

const { toggleSidebar } = useSidebar();
</script>

<template>
  <Button variant="ghost" size="icon" @click="toggleSidebar">
    <PanelLeft class="size-4" />
    <span class="sr-only">Переключить меню</span>
  </Button>
</template>
