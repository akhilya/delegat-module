<script setup>
import { ref, watch } from "vue";
import { useRoute } from "vue-router";
import { ChevronRight } from "lucide-vue-next";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubItem,
  SidebarMenuSubButton,
} from "@/components/ui/sidebar";
import { isActiveRoute } from "@/lib/isActiveRoute";

const props = defineProps({
  label: { type: String, required: true },
  items: { type: Array, required: true },
  defaultOpen: { type: Boolean, default: true },
});

const route = useRoute();
const open = ref(props.defaultOpen);

watch(
  () => route.path,
  () => {
    if (props.items.some((item) => item.to && isActiveRoute(route, item.to))) {
      open.value = true;
    }
  },
  { immediate: true },
);
</script>

<template>
  <SidebarGroup>
    <Collapsible v-model:open="open" class="group/collapsible">
      <CollapsibleTrigger as-child>
        <SidebarGroupLabel class="cursor-pointer select-none hover:text-sidebar-foreground">
          {{ label }}
          <ChevronRight
            class="ml-auto size-3 opacity-60 transition-transform duration-200 group-data-[state=open]/collapsible:rotate-90"
          />
        </SidebarGroupLabel>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <SidebarGroupContent>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuSub>
                <SidebarMenuSubItem v-for="item in items" :key="item.id">
                  <SidebarMenuSubButton
                    as-child
                    :is-active="item.to ? isActiveRoute(route, item.to) : false"
                  >
                    <RouterLink :to="item.to">
                      <component :is="item.icon" />
                      <span>{{ item.label }}</span>
                    </RouterLink>
                  </SidebarMenuSubButton>
                </SidebarMenuSubItem>
              </SidebarMenuSub>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroupContent>
      </CollapsibleContent>
    </Collapsible>
  </SidebarGroup>
</template>