<script>
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import {
  resolveGroupIcon,
  GROUP_LUCIDE_ICON_NAMES,
} from "@/lib/attributeGroupIcon";
import { cn } from "@/lib/utils";
import * as LucideIcons from "lucide-vue-next";

const LUCIDE_MAP = Object.fromEntries(
  GROUP_LUCIDE_ICON_NAMES.map((name) => [name, LucideIcons[name]]).filter(
    ([, component]) => component,
  ),
);

export default {
  name: "AttributeGroupIcon",
  inheritAttrs: false,
  components: { AdapterIcon },
  props: {
    groupName: { type: String, default: "" },
    groupIcon: { type: String, default: "" },
  },
  computed: {
    resolved() {
      return resolveGroupIcon(this.groupName, this.groupIcon);
    },
    lucideComponent() {
      return this.resolved?.type === "lucide"
        ? LUCIDE_MAP[this.resolved.name]
        : null;
    },
    iconClass() {
      return cn("size-4 shrink-0", this.$attrs.class);
    },
  },
};
</script>

<template>
  <AdapterIcon
    v-if="resolved?.type === 'adapter'"
    :adapter-key="resolved.key"
    :class="iconClass"
  />
  <component
    :is="lucideComponent"
    v-else-if="lucideComponent"
    :class="iconClass"
  />
</template>
