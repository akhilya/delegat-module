<script setup>
import { computed, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { useStore } from "vuex";
import { ChevronRight } from "lucide-vue-next";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
} from "@/components/ui/sidebar";
import { resolveActiveNavItemId } from "@/lib/isActiveRoute";
import {
  getSidebarGroupOpen,
  setSidebarGroupOpen,
} from "@/lib/sidebarNavState.js";

const props = defineProps({
  groupId: { type: String, required: true },
  label: { type: String, required: true },
  icon: { type: [Object, Function], required: true },
  items: { type: Array, required: true },
  defaultOpen: { type: Boolean, default: false },
});

const route = useRoute();
const store = useStore();
const savedOpen = getSidebarGroupOpen(props.groupId);
const open = ref(savedOpen ?? props.defaultOpen);

const activeItemId = computed(() => resolveActiveNavItemId(route, props.items));

function isItemActive(item) {
  if (item.type === "action" || !item.to) return false;
  return item.id === activeItemId.value;
}

function handleAction(action) {
  if (action === "toggleAi") store.commit("aiChat/toggle");
  if (action === "toggleOps") store.dispatch("operations/togglePanel");
}

watch(open, (isOpen) => {
  setSidebarGroupOpen(props.groupId, isOpen);
});

watch(
  () => route.path,
  () => {
    if (getSidebarGroupOpen(props.groupId) !== undefined) return;
    if (props.items.some((item) => isItemActive(item))) {
      open.value = true;
    }
  },
  { immediate: true },
);
</script>

<template>
  <SidebarGroup class="p-1">
    <SidebarMenu>
      <Collapsible v-model:open="open" as-child class="group/collapsible">
        <SidebarMenuItem>
          <CollapsibleTrigger as-child>
            <SidebarMenuButton :tooltip="label">
              <component :is="icon" />
              <span>{{ label }}</span>
              <ChevronRight
                class="ml-auto transition-transform duration-200 group-data-[state=open]/collapsible:rotate-90"
              />
            </SidebarMenuButton>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <SidebarMenuSub>
              <SidebarMenuSubItem v-for="item in items" :key="item.id">
                <SidebarMenuSubButton
                  v-if="item.type === 'action'"
                  @click="handleAction(item.action)"
                >
                  <component :is="item.icon" />
                  <span>{{ item.label }}</span>
                </SidebarMenuSubButton>
                <SidebarMenuSubButton
                  v-else
                  as-child
                  :is-active="isItemActive(item)"
                >
                  <RouterLink :to="item.to">
                    <component :is="item.icon" />
                    <span>{{ item.label }}</span>
                  </RouterLink>
                </SidebarMenuSubButton>
              </SidebarMenuSubItem>
            </SidebarMenuSub>
          </CollapsibleContent>
        </SidebarMenuItem>
      </Collapsible>
    </SidebarMenu>
  </SidebarGroup>
</template>