<script>
import { Button } from "@/components/ui/button";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Plus } from "lucide-vue-next";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import AttributeGroupIcon from "@/components/products/AttributeGroupIcon.vue";
import { ADAPTER_ICON_URLS } from "@/lib/adapterIconAssets";
import {
  GROUP_LUCIDE_ICON_NAMES,
  groupIconForAdapter,
} from "@/lib/attributeGroupIcon";
import * as LucideIcons from "lucide-vue-next";

const LUCIDE_MAP = Object.fromEntries(
  GROUP_LUCIDE_ICON_NAMES.map((name) => [name, LucideIcons[name]]).filter(
    ([, component]) => component,
  ),
);

export default {
  name: "AttributeGroupIconPicker",
  components: {
    Button,
    Popover,
    PopoverContent,
    PopoverTrigger,
    Plus,
    AdapterIcon,
    AttributeGroupIcon,
  },
  props: {
    modelValue: { type: String, default: "" },
    groupName: { type: String, default: "" },
  },
  emits: ["update:modelValue"],
  data() {
    return {
      open: false,
      lucideNames: GROUP_LUCIDE_ICON_NAMES.filter((name) => LUCIDE_MAP[name]),
      adapterKeys: Object.keys(ADAPTER_ICON_URLS),
      LucideIcons: LUCIDE_MAP,
    };
  },
  methods: {
    pick(value) {
      this.$emit("update:modelValue", value);
      this.open = false;
    },
    adapterToken(key) {
      return groupIconForAdapter(key);
    },
  },
};
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <Button
        type="button"
        variant="outline"
        size="icon"
        class="size-9 shrink-0"
        :aria-label="$t('pages.categoryEditPage.groupIconLabel')"
      >
        <AttributeGroupIcon
          v-if="modelValue || groupName"
          :group-name="groupName"
          :group-icon="modelValue"
        />
        <Plus v-else class="size-4 text-muted-foreground" />
      </Button>
    </PopoverTrigger>
    <PopoverContent align="start" class="w-64 p-3">
      <p class="text-xs font-medium text-muted-foreground mb-2">
        {{ $t("pages.categoryEditPage.groupIconMarketplace") }}
      </p>
      <div class="grid grid-cols-6 gap-1 mb-3">
        <Button
          v-for="key in adapterKeys"
          :key="key"
          type="button"
          variant="ghost"
          size="icon"
          class="size-8"
          :aria-label="key"
          @click="pick(adapterToken(key))"
        >
          <AdapterIcon :adapter-key="key" />
        </Button>
      </div>
      <p class="text-xs font-medium text-muted-foreground mb-2">
        {{ $t("pages.categoryEditPage.groupIconSymbols") }}
      </p>
      <div class="grid grid-cols-6 gap-1">
        <Button
          v-for="name in lucideNames"
          :key="name"
          type="button"
          variant="ghost"
          size="icon"
          class="size-8"
          :aria-label="name"
          @click="pick(name)"
        >
          <component :is="LucideIcons[name]" class="size-4" />
        </Button>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        class="mt-3 w-full"
        @click="pick('')"
      >
        {{ $t("pages.categoryEditPage.groupIconNone") }}
      </Button>
    </PopoverContent>
  </Popover>
</template>
