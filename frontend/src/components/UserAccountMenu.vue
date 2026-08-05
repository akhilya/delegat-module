<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { useRouter } from "vue-router";
import { useStore } from "vuex";
import {
  ChevronsUpDown,
  Languages,
  LogOut,
  Moon,
  Sun,
  UserCog,
  UserRound,
  BookOpen,
} from "lucide-vue-next";
import { setLocale, SUPPORTED_LOCALES } from "@/i18n";
import { PAGE_SETTINGS_USER } from "@/router";
import { getDocsPublicUrl } from "@/utils/docsUrl";
import { isDark, toggleDark } from "@/theme";
import LocaleFlag from "@/components/LocaleFlag.vue";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { SidebarMenuButton } from "@/components/ui/sidebar";

const props = defineProps({
  userName: { type: String, default: "" },
  version: { type: String, default: "" },
  /** navbar = top bar; sidebar = footer */
  variant: {
    type: String,
    default: "navbar",
    validator: (value) => ["navbar", "sidebar"].includes(value),
  },
  /** DropdownContent side - sidebar footer usually opens upward */
  side: { type: String, default: undefined },
  align: { type: String, default: undefined },
});

const { t, locale } = useI18n();
const router = useRouter();
const store = useStore();

const languageOptions = SUPPORTED_LOCALES.map((code) => ({
  code,
  labelKey: `language.${code}`,
}));

const currentLocale = computed({
  get: () => locale.value,
  set: (value) => setLocale(value),
});

const displayName = computed(
  () => props.userName || t("components.userAccountMenu.account"),
);

const contentSide = computed(
  () => props.side ?? (props.variant === "sidebar" ? "top" : "bottom"),
);
const contentAlign = computed(
  () => props.align ?? (props.variant === "sidebar" ? "start" : "end"),
);

function goToUserSettings() {
  router.push(PAGE_SETTINGS_USER);
}

function onLogout() {
  store.dispatch("postLogOut");
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <SidebarMenuButton
        v-if="variant === 'sidebar'"
        class="data-[state=open]:bg-sidebar-accent data-[state=open]:text-sidebar-accent-foreground"
        :tooltip="displayName"
      >
        <UserRound class="size-4" />
        <span class="truncate font-medium">{{ displayName }}</span>
        <ChevronsUpDown class="ml-auto size-4" />
      </SidebarMenuButton>
      <Button
        v-else
        variant="ghost"
        class="h-9 gap-1.5"
      >
        <UserRound class="size-4" />
        <span class="hidden sm:inline max-w-[120px] truncate">{{
          displayName
        }}</span>
      </Button>
    </DropdownMenuTrigger>

    <DropdownMenuContent
      class="w-56"
      :side="contentSide"
      :align="contentAlign"
      :side-offset="4"
    >
      <DropdownMenuLabel>{{ displayName }}</DropdownMenuLabel>
      <DropdownMenuSeparator />

      <DropdownMenuItem @select="goToUserSettings">
        <UserCog />
        {{ t("components.navigationBar.mySettings") }}
      </DropdownMenuItem>

      <DropdownMenuItem as-child>
        <a
          :href="getDocsPublicUrl(locale)"
          target="_blank"
          rel="noopener noreferrer"
        >
          <BookOpen />
          {{ t("components.userAccountMenu.documentation") }}
        </a>
      </DropdownMenuItem>

      <DropdownMenuSub>
        <DropdownMenuSubTrigger>
          <Languages />
          <span>{{ t("language.label") }}</span>
        </DropdownMenuSubTrigger>
        <DropdownMenuSubContent>
          <DropdownMenuRadioGroup v-model="currentLocale">
            <DropdownMenuRadioItem
              v-for="opt in languageOptions"
              :key="opt.code"
              :value="opt.code"
            >
              <LocaleFlag :code="opt.code" />
              {{ t(opt.labelKey) }}
            </DropdownMenuRadioItem>
          </DropdownMenuRadioGroup>
        </DropdownMenuSubContent>
      </DropdownMenuSub>

      <DropdownMenuItem @select="toggleDark()">
        <Sun v-if="isDark" />
        <Moon v-else />
        {{ isDark ? t("theme.light") : t("theme.dark") }}
      </DropdownMenuItem>

      <DropdownMenuSeparator />

      <DropdownMenuItem variant="destructive" @select="onLogout">
        <LogOut />
        {{ t("auth.signOut") }}
      </DropdownMenuItem>

      <template v-if="version">
        <DropdownMenuSeparator />
        <div
          class="text-muted-foreground px-2 py-1 text-center text-[10px] select-none"
        >
          v{{ version }}
        </div>
      </template>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
