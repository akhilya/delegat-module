import {
  PAGE_PRODUCTS,
  PAGE_PRODUCT_CREATION,
  PAGE_PRODUCT_GROUPS,
  PAGE_CATEGORIES,
  PAGE_STORE_STORAGE,
  PAGE_MARKETPLACE,
  PAGE_STORE_ATTRIBUTES,
  PAGE_TOOLS,
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
} from "lucide-vue-next";
import { getSettingsSidebarSubItems } from "./settingsNav";

/**
 * @typedef {'link'|'action'} NavItemType
 * @typedef {{ id: string, label: string, type: NavItemType, to?: string, icon?: import('vue').Component, action?: string, visible?: (ctx: SidebarContext) => boolean, children?: NavItem[] }} NavItem
 * @typedef {{ id: string, label: string, icon: import('vue').Component, defaultOpen?: boolean, items: NavItem[]|((ctx: SidebarContext) => NavItem[]), visible?: (ctx: SidebarContext) => boolean }} NavGroup
 * @typedef {{ me: object|null, canManageStore: boolean, isAdmin: boolean, connections: object[], adapters: object[] }} SidebarContext
 */

/**
 * @param {string} adapterKey
 * @param {object[]} adapters
 */
function getAdapterDisplayName(adapterKey, adapters) {
  const adapter = (adapters || []).find(
    (item) => (item.key ?? item.Key) === adapterKey,
  );
  return adapter?.displayName ?? adapter?.DisplayName ?? adapterKey;
}

/**
 * @param {object} connection
 * @param {object[]} adapters
 * @param {object[]} activeConnections
 */
function isConnectionActive(connection) {
  const active = connection.isActive ?? connection.IsActive;
  return active !== false;
}

function getConnectionNavLabel(connection, adapters, activeConnections) {
  const displayName = getAdapterDisplayName(connection.adapterKey, adapters);
  const sameAdapterCount = activeConnections.filter(
    (item) => item.adapterKey === connection.adapterKey,
  ).length;
  if (sameAdapterCount > 1 && connection.name) {
    return `${displayName} · ${connection.name}`;
  }
  return displayName;
}

/** @type {NavGroup[]} */
const SIDEBAR_GROUPS = [
  {
    id: "products",
    label: "Товары",
    icon: Boxes,
    defaultOpen: true,
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
    icon: HardDrive,
    defaultOpen: false,
    items: [
      { id: "storage", label: "Хранилище", type: "link", to: PAGE_STORE_STORAGE, icon: HardDrive },
    ],
  },
  {
    id: "integrations",
    label: "Интеграции",
    icon: ShoppingBag,
    defaultOpen: true,
    items: (ctx) => {
      const activeConnections = (ctx.connections || [])
        .filter(isConnectionActive)
        .slice()
        .sort((a, b) => {
          const aKey = a.adapterKey ?? a.AdapterKey;
          const bKey = b.adapterKey ?? b.AdapterKey;
          const aName = getAdapterDisplayName(aKey, ctx.adapters);
          const bName = getAdapterDisplayName(bKey, ctx.adapters);
          return aName.localeCompare(bName, "ru");
        });

      return [
        {
          id: "adapters",
          label: "Адаптеры",
          type: "link",
          to: PAGE_MARKETPLACE,
          icon: ShoppingBag,
        },
        ...activeConnections.map((conn) => {
          const connId = conn.id ?? conn.Id;
          const adapterKey = conn.adapterKey ?? conn.AdapterKey;
          return {
            id: `conn-${connId}`,
            label: getConnectionNavLabel(
              { ...conn, adapterKey },
              ctx.adapters,
              activeConnections.map((c) => ({
                ...c,
                adapterKey: c.adapterKey ?? c.AdapterKey,
              })),
            ),
            type: "link",
            to: `/adapter/${connId}`,
            icon: Plug,
          };
        }),
      ];
    },
  },
  {
    id: "utilities",
    label: "Утилиты",
    icon: Wand2,
    defaultOpen: false,
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
    icon: Settings2,
    defaultOpen: false,
    items: (ctx) =>
      getSettingsSidebarSubItems(ctx).map((s) => ({
        id: s.id,
        label: s.label,
        type: "link",
        to: s.to,
        icon: s.icon,
      })),
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
  }).filter((group) => !group.visible || group.visible(ctx));
}