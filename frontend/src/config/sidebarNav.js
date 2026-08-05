import {
  PAGE_PRODUCTS,
  PAGE_PRODUCT_CREATION,
  PAGE_PRODUCT_GROUPS,
  PAGE_CATEGORIES,
  PAGE_BARCODES,
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
  Barcode,
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
import { getLocale, t, toBcp47 } from "@/i18n";
import { hasPermission, PERMS } from "@/lib/permissions";
import { adapterIconComponent } from "@/lib/adapterIcons";

/**
 * @typedef {'link'|'action'} NavItemType
 * @typedef {{ id: string, label: string, type: NavItemType, to?: string, icon?: import('vue').Component, action?: string, visible?: (ctx: SidebarContext) => boolean, children?: NavItem[] }} NavItem
 * @typedef {{ id: string, label: string, icon: import('vue').Component, defaultOpen?: boolean, items: NavItem[]|((ctx: SidebarContext) => NavItem[]), visible?: (ctx: SidebarContext) => boolean }} NavGroup
 * @typedef {{ me: object|null, canManageStore: boolean, isAdmin: boolean, hasStore?: boolean, hasPermission?: (p: string) => boolean, connections: object[], adapters: object[] }} SidebarContext
 */

function getAdapterDisplayName(adapterKey, adapters) {
  const adapter = (adapters || []).find(
    (item) => (item.key ?? item.Key) === adapterKey,
  );
  return adapter?.displayName ?? adapter?.DisplayName ?? adapterKey;
}

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
    labelKey: "nav.products",
    icon: Boxes,
    defaultOpen: true,
    visible: (ctx) => Boolean(ctx.hasStore),
    items: [
      { id: "products", labelKey: "nav.products", type: "link", to: PAGE_PRODUCTS, icon: Boxes },
      {
        id: "new-product",
        labelKey: "nav.newProduct",
        type: "link",
        to: PAGE_PRODUCT_CREATION,
        icon: PackagePlus,
        visible: (ctx) => can(ctx, PERMS.CatalogManage),
      },
      {
        id: "groups",
        labelKey: "nav.groups",
        type: "link",
        to: PAGE_PRODUCT_GROUPS,
        icon: Layers,
        visible: (ctx) => can(ctx, PERMS.CatalogManage),
      },
      { id: "trash", labelKey: "nav.trash", type: "link", to: `${PAGE_PRODUCTS}?trash=1`, icon: Trash2 },
      {
        id: "categories",
        labelKey: "nav.categories",
        type: "link",
        to: PAGE_CATEGORIES,
        icon: FolderOpen,
        visible: (ctx) => can(ctx, PERMS.CatalogManage),
      },
      {
        id: "barcodes",
        labelKey: "nav.barcodes",
        type: "link",
        to: PAGE_BARCODES,
        icon: Barcode,
        visible: (ctx) => can(ctx, PERMS.CatalogManage),
      },
    ],
  },
  {
    id: "storage",
    labelKey: "nav.storage",
    icon: HardDrive,
    defaultOpen: false,
    visible: (ctx) => Boolean(ctx.hasStore),
    items: [
      { id: "storage", labelKey: "nav.storage", type: "link", to: PAGE_STORE_STORAGE, icon: HardDrive },
    ],
  },
  {
    id: "integrations",
    labelKey: "nav.integrations",
    icon: ShoppingBag,
    defaultOpen: true,
    visible: (ctx) => Boolean(ctx.hasStore) && can(ctx, PERMS.MarketplaceManage),
    items: (ctx) => {
      const activeConnections = (ctx.connections || [])
        .filter(isConnectionActive)
        .slice()
        .sort((a, b) => {
          const aKey = a.adapterKey ?? a.AdapterKey;
          const bKey = b.adapterKey ?? b.AdapterKey;
          const aName = getAdapterDisplayName(aKey, ctx.adapters);
          const bName = getAdapterDisplayName(bKey, ctx.adapters);
          return aName.localeCompare(bName, toBcp47(getLocale()));
        });

      return [
        {
          id: "adapters",
          labelKey: "nav.adapters",
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
            icon: adapterIconComponent(adapterKey, Plug),
          };
        }),
      ];
    },
  },
  {
    id: "utilities",
    labelKey: "nav.utilities",
    icon: Wand2,
    defaultOpen: false,
    visible: (ctx) => Boolean(ctx.hasStore),
    items: [
      {
        id: "ai",
        labelKey: "nav.aiAssistant",
        type: "action",
        action: "toggleAi",
        icon: Sparkles,
        visible: (ctx) => can(ctx, PERMS.AiUse) && ctx.me?.aiEnabled === true,
      },
      {
        id: "ops",
        labelKey: "nav.backgroundJobs",
        type: "action",
        action: "toggleOps",
        icon: Activity,
        visible: (ctx) => can(ctx, PERMS.OperationsManage),
      },
      {
        id: "tools",
        labelKey: "nav.tools",
        type: "link",
        to: PAGE_TOOLS,
        icon: Wand2,
        visible: (ctx) => can(ctx, PERMS.OperationsManage) || can(ctx, PERMS.CatalogManage),
      },
      {
        id: "attributes",
        labelKey: "nav.globalAttributes",
        type: "link",
        to: PAGE_STORE_ATTRIBUTES,
        icon: Globe,
        visible: (ctx) => can(ctx, PERMS.CatalogManage),
      },
    ],
  },
  {
    id: "settings",
    labelKey: "nav.settings",
    icon: Settings2,
    defaultOpen: false,
    items: (ctx) =>
      getSettingsSidebarSubItems(ctx).map((s) => ({
        id: s.id,
        labelKey: s.labelKey,
        label: s.label,
        type: "link",
        to: s.to,
        icon: s.icon,
      })),
  },
];

function can(ctx, permission) {
  if (typeof ctx.hasPermission === "function") return ctx.hasPermission(permission);
  return hasPermission(ctx.me, permission);
}
function resolveLabel(item) {
  if (item.labelKey) return t(item.labelKey);
  return item.label ?? "";
}

/**
 * @param {SidebarContext} ctx
 * @returns {Array<NavGroup & { resolvedItems: NavItem[] }>}
 */
export function buildSidebarGroups(ctx) {
  return SIDEBAR_GROUPS.map((group) => {
    const rawItems = typeof group.items === "function" ? group.items(ctx) : group.items;
    const resolvedItems = rawItems
      .filter((item) => !item.visible || item.visible(ctx))
      .map((item) => ({ ...item, label: resolveLabel(item) }));
    return {
      ...group,
      label: resolveLabel(group),
      resolvedItems,
    };
  }).filter((group) => {
    if (group.visible && !group.visible(ctx)) return false;
    return group.resolvedItems.length > 0;
  });
}
