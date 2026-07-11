import {
  PAGE_PRODUCTS,
  PAGE_PRODUCT_CREATION,
  PAGE_PRODUCT_GROUPS,
  PAGE_CATEGORIES,
  PAGE_STORE_STORAGE,
  PAGE_MARKETPLACE,
  PAGE_STORE_ATTRIBUTES,
  PAGE_TOOLS,
  PAGE_SETTINGS,
} from "@/router";
import {
  Boxes,
  PackagePlus,
  Layers,
  Trash2,
  FolderOpen,
  HardDrive,
  ShoppingBag,
  Plug,
  Sparkles,
  Activity,
  Wand2,
  Globe,
  Settings2,
  ChevronRight,
} from "lucide-vue-next";
import { getSettingsSidebarSubItems } from "./settingsNav";

/**
 * @typedef {'link'|'action'} NavItemType
 * @typedef {{ id: string, label: string, type: NavItemType, to?: string, icon?: import('vue').Component, action?: string, visible?: (ctx: SidebarContext) => boolean }} NavItem
 * @typedef {{ id: string, label: string, collapsible?: boolean, defaultOpen?: boolean, items: NavItem[]|((ctx: SidebarContext) => NavItem[]), visible?: (ctx: SidebarContext) => boolean }} NavGroup
 * @typedef {{ me: object|null, canManageStore: boolean, isAdmin: boolean, connections: object[] }} SidebarContext
 */

/** @type {NavGroup[]} */
const SIDEBAR_GROUPS = [
  {
    id: "products",
    label: "Товары",
    items: [
      { id: "products", label: "Товары", type: "link", to: PAGE_PRODUCTS, icon: Boxes },
      { id: "new-product", label: "Новый товар", type: "link", to: PAGE_PRODUCT_CREATION, icon: PackagePlus },
      { id: "groups", label: "Группы", type: "link", to: PAGE_PRODUCT_GROUPS, icon: Layers },
      { id: "trash", label: "Корзина", type: "link", to: `${PAGE_PRODUCTS}?trash=1`, icon: Trash2 },
      {
        id: "categories",
        label: "Категории",
        type: "link",
        to: PAGE_CATEGORIES,
        icon: FolderOpen,
        visible: (ctx) => ctx.canManageStore,
      },
    ],
  },
  {
    id: "storage",
    label: "Хранилище",
    items: [
      { id: "storage", label: "Хранилище", type: "link", to: PAGE_STORE_STORAGE, icon: HardDrive },
    ],
  },
  {
    id: "integrations",
    label: "Интеграции",
    collapsible: true,
    defaultOpen: true,
    items: (ctx) => {
      const base = [
        { id: "adapters", label: "Адаптеры", type: "link", to: PAGE_MARKETPLACE, icon: ShoppingBag },
      ];
      const dynamic = (ctx.connections || [])
        .slice()
        .sort((a, b) => (a.name || "").localeCompare(b.name || "", "ru"))
        .map((conn) => ({
          id: `conn-${conn.id}`,
          label: conn.name || `Подключение #${conn.id}`,
          type: "link",
          to: `/adapter/${conn.id}`,
          icon: Plug,
        }));
      return [...base, ...dynamic];
    },
  },
  {
    id: "utilities",
    label: "Утилиты",
    items: [
      { id: "ai", label: "ИИ ассистент", type: "action", action: "toggleAi", icon: Sparkles },
      { id: "ops", label: "Фоновые процессы", type: "action", action: "toggleOps", icon: Activity },
      { id: "tools", label: "Инструменты", type: "link", to: PAGE_TOOLS, icon: Wand2 },
      { id: "attributes", label: "Глобальные атрибуты", type: "link", to: PAGE_STORE_ATTRIBUTES, icon: Globe },
    ],
  },
  {
    id: "settings",
    label: "Настройки",
    collapsible: true,
    defaultOpen: true,
    items: (ctx) => {
      const subs = getSettingsSidebarSubItems(ctx);
      return [
        { id: "settings-hub", label: "Настройки", type: "link", to: PAGE_SETTINGS, icon: Settings2 },
        ...subs.map((s) => ({
          id: s.id,
          label: s.label,
          type: "link",
          to: s.to,
          icon: s.icon,
        })),
      ];
    },
  },
];

/**
 * @param {SidebarContext} ctx
 * @returns {Array<NavGroup & { resolvedItems: NavItem[] }>}
 */
export function buildSidebarGroups(ctx) {
  return SIDEBAR_GROUPS.map((group) => {
    const rawItems = typeof group.items === "function" ? group.items(ctx) : group.items;
    const resolvedItems = rawItems.filter((item) => !item.visible || item.visible(ctx));
    return { ...group, resolvedItems };
  }).filter((group) => !group.visible || group.visible(group));
}

export { ChevronRight };