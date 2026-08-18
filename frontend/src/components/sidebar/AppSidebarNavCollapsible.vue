<script setup>
import { computed, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { useStore } from "vuex";
import { AlertTriangle, ChevronRight } from "lucide-vue-next";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  SidebarGroup,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
} from "@/components/ui/sidebar";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { resolveActiveNavItemId } from "@/lib/isActiveRoute";
import {
  getSidebarGroupOpen,
  setSidebarGroupOpen,
} from "@/lib/sidebarNavState.js";
import { t } from "@/i18n";

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

/** Flatten only when the lone child is the same named destination as the group (Storage). */
const singleLink = computed(() => {
  if (props.items.length !== 1) return null;
  const item = props.items[0];
  if (item.type === "action" || !item.to) return null;
  if (item.label !== props.label) return null;
  return item;
});

const groupHasAttention = computed(() =>
  props.items.some((item) => item.attention === true),
);

function isItemActive(item) {
  if (item.type === "action" || !item.to) return false;
  return item.id === activeItemId.value;
}

function handleAction(action) {
  if (action === "toggleAi") store.commit("aiChat/toggle");
  if (action === "toggleOps") store.dispatch("operations/togglePanel");
}

function attentionLabel(item) {
  if (item.attentionTooltipKey) return t(item.attentionTooltipKey);
  return t("nav.quotaOverageAttention");
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
  <SidebarGroup class="p-0">
    <div
      class="overflow-hidden rounded-xl bg-card p-1 ring-1 ring-sidebar-border group-data-[collapsible=icon]:rounded-lg group-data-[collapsible=icon]:bg-transparent group-data-[collapsible=icon]:p-0 group-data-[collapsible=icon]:ring-0"
    >
      <SidebarMenu>
        <SidebarMenuItem v-if="singleLink">
          <SidebarMenuButton
            as-child
            :tooltip="label"
            :is-active="isItemActive(singleLink)"
            :aria-label="
              groupHasAttention
                ? `${label}. ${t('nav.quotaOverageAttention')}`
                : undefined
            "
          >
            <RouterLink :to="singleLink.to">
              <span
                class="relative inline-flex size-4 shrink-0 items-center justify-center [&>svg]:size-4"
              >
                <component :is="icon" />
                <span
                  v-if="groupHasAttention"
                  class="absolute -right-0.5 -top-0.5 size-1.5 rounded-full bg-destructive ring-2 ring-card group-data-[collapsible=icon]:ring-sidebar"
                  aria-hidden="true"
                />
              </span>
              <span>{{ label }}</span>
              <TooltipProvider v-if="singleLink.attention" :delay-duration="300">
                <Tooltip>
                  <TooltipTrigger as-child>
                    <span
                      class="ml-auto inline-flex shrink-0"
                      :aria-label="attentionLabel(singleLink)"
                    >
                      <AlertTriangle class="size-3.5 text-destructive" />
                    </span>
                  </TooltipTrigger>
                  <TooltipContent side="right">
                    {{ attentionLabel(singleLink) }}
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            </RouterLink>
          </SidebarMenuButton>
        </SidebarMenuItem>

        <Collapsible
          v-else
          v-model:open="open"
          as-child
          class="group/collapsible"
        >
          <SidebarMenuItem>
            <CollapsibleTrigger as-child>
              <SidebarMenuButton
                :tooltip="label"
                :aria-label="
                  groupHasAttention
                    ? `${label}. ${t('nav.quotaOverageAttention')}`
                    : undefined
                "
              >
                <span
                  class="relative inline-flex size-4 shrink-0 items-center justify-center [&>svg]:size-4"
                >
                  <component :is="icon" />
                  <span
                    v-if="groupHasAttention"
                    class="absolute -right-0.5 -top-0.5 size-1.5 rounded-full bg-destructive ring-2 ring-card group-data-[collapsible=icon]:ring-sidebar"
                    aria-hidden="true"
                  />
                </span>
                <span>{{ label }}</span>
                <ChevronRight
                  class="ml-auto size-3.5 text-muted-foreground transition-transform duration-200 ease-out group-data-[state=open]/collapsible:rotate-90 motion-reduce:transition-none"
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
                      <TooltipProvider
                        v-if="item.attention"
                        :delay-duration="300"
                      >
                        <Tooltip>
                          <TooltipTrigger as-child>
                            <span
                              class="ml-auto inline-flex shrink-0"
                              :aria-label="attentionLabel(item)"
                            >
                              <AlertTriangle class="size-3.5 text-destructive" />
                            </span>
                          </TooltipTrigger>
                          <TooltipContent side="right">
                            {{ attentionLabel(item) }}
                          </TooltipContent>
                        </Tooltip>
                      </TooltipProvider>
                    </RouterLink>
                  </SidebarMenuSubButton>
                </SidebarMenuSubItem>
              </SidebarMenuSub>
            </CollapsibleContent>
          </SidebarMenuItem>
        </Collapsible>
      </SidebarMenu>
    </div>
  </SidebarGroup>
</template>
