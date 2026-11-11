import {
  PAGE_PRODUCTS,
  PAGE_PRODUCT_CREATION,
  PAGE_PRODUCT_GROUPS,
  PAGE_CATEGORIES,
  PAGE_STORE_STORAGE,
  PAGE_MARKETPLACE,
  PAGE_STORE_ATTRIBUTES,
  PAGE_ORDERS,
  PAGE_TOOLS,
} from "@/router";
import {
  Boxes,
  Package,
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
  ClipboardList,
} from "lucide-vue-next";
import { getSettingsSidebarSubItems } from "./settingsNav";
import { getLocale, t, toBcp47 } from "@/i18n";
import { canBrowse, hasPermission, PERMS } from "@/lib/permissions";
import { canShowNavLink, uniqueByNavTarget } from "@/lib/navAccess";
import { adapterIconComponent } from "@/lib/adapterIcons";
import { adapterCanGetOrders } from "@/lib/adapterCapabilities";
import { isExperimentalAdapter, findAdapterByKey } from "@/lib/adapterStage";

/**
 * @typedef {'link'|'action'} NavItemType
 * @typedef {{ id: string, label: string, type: NavItemType, to?: string, icon?: import('vue').Component, action?: string, visible?: (ctx: SidebarContext) => boolean, children?: NavItem[] }} NavItem
 * @typedef {{ id: string, label: string, icon: import('vue').Component, defaultOpen?: boolean, items: NavItem[]|((ctx: SidebarContext) => NavItem[]), visible?: (ctx: SidebarContext) => boolean }} NavGroup
 * @typedef {{ me: object|null, isAdmin: boolean, hasStore?: boolean, hasPermission?: (p: string) => boolean, connections: object[], adapters: object[], quotaOverAttention?: boolean }} SidebarContext
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

function hasGetOrdersConnection(ctx) {
  const adapters = ctx.adapters || [];
  return (ctx.connections || []).some((connection) => {
    if (!isConnectionActive(connection)) return false;
    const adapterKey = connection.adapterKey ?? connection.AdapterKey;
    const adapter = adapters.find(
      (item) => (item.key ?? item.Key) === adapterKey,
    );
    return adapterCanGetOrders(adapter);
  });
}

function getConnectionNavLabel(connection, adapters) {
  const name = String(connection.name ?? connection.Name ?? "").trim();
  if (name) return name;
  return getAdapterDisplayName(
    connection.adapterKey ?? connection.AdapterKey,
    adapters,
  );
}

/** @type {NavGroup[]} */
const SIDEBAR_GROUPS = [
  {
    id: "orders",
    labelKey: "nav.orders",
    icon: ClipboardList,
    experimental: true,
    defaultOpen: true,
    visible: (ctx) =>
      Boolean(ctx.hasStore) &&
      canBrowseNav(ctx, PERMS.OrdersManage) &&
      hasGetOrdersConnection(ctx),
    items: (ctx) => {
      const activeConnections = (ctx.connections || [])
        .filter((connection) => {
          if (!isConnectionActive(connection)) return false;
          const adapterKey = connection.adapterKey ?? connection.AdapterKey;
          const adapter = (ctx.adapters || []).find(
            (item) => (item.key ?? item.Key) === adapterKey,
          );
          return adapterCanGetOrders(adapter);
        })
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
          id: "orders-assembly",
          labelKey: "nav.ordersAssembly",
          type: "link",
          to: PAGE_ORDERS,
          icon: Package,
        },
        ...activeConnections.map((conn) => {
          const connId = conn.id ?? conn.Id;
          const adapterKey = conn.adapterKey ?? conn.AdapterKey;
          return {
            id: `orders-conn-${connId}`,
            label: getConnectionNavLabel(
              { ...conn, adapterKey },
              ctx.adapters,
              activeConnections.map((item) => ({
                ...item,
                adapterKey: item.adapterKey ?? item.AdapterKey,
              })),
            ),
            type: "link",
            to: `/orders/${connId}`,
            icon: adapterIconComponent(adapterKey, ClipboardList),
          };
        }),
      ];
    },
  },
  {
    id: "products",
    labelKey: "nav.catalog",
    icon: Boxes,
    defaultOpen: true,
    visible: (ctx) => Boolean(ctx.hasStore),
    items: [
      {
        id: "products",
        labelKey: "nav.products",
        type: "link",
        to: PAGE_PRODUCTS,
        icon: Boxes,
      },
      {
        id: "new-product",
        labelKey: "nav.newProduct",
        type: "link",
        to: PAGE_PRODUCT_CREATION,
        icon: PackagePlus,
      },
      {
        id: "groups",
        labelKey: "nav.groups",
        type: "link",
        to: PAGE_PRODUCT_GROUPS,
        icon: Layers,
      },
      {
        id: "trash",
        labelKey: "nav.trash",
        type: "link",
        to: `${PAGE_PRODUCTS}?trash=1`,
        icon: Trash2,
      },
      {
        id: "categories",
        labelKey: "nav.categories",
        type: "link",
        to: PAGE_CATEGORIES,
        icon: FolderOpen,
      },
    ],
  },
  {
    id: "storage",
    labelKey: "nav.storage",
    icon: HardDrive,
    defaultOpen: false,
    visible: (ctx) =>
      Boolean(ctx.hasStore) && canShowNavLink(ctx.me, PAGE_STORE_STORAGE),
    items: [
      {
        id: "storage",
        labelKey: "nav.storage",
        type: "link",
        to: PAGE_STORE_STORAGE,
        icon: HardDrive,
      },
    ],
  },
  {
    id: "integrations",
    labelKey: "nav.integrations",
    icon: ShoppingBag,
    defaultOpen: true,
    visible: (ctx) =>
      Boolean(ctx.hasStore) && canBrowseNav(ctx, PERMS.MarketplaceManage),
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
            experimental: isExperimentalAdapter(
              findAdapterByKey(ctx.adapters, adapterKey),
            ),
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
        visible: (ctx) => canBrowseNav(ctx, PERMS.OperationsManage),
      },
      {
        id: "tools",
        labelKey: "nav.tools",
        type: "link",
        to: PAGE_TOOLS,
        icon: Wand2,
      },
      {
        id: "attributes",
        labelKey: "nav.globalAttributes",
        type: "link",
        to: PAGE_STORE_ATTRIBUTES,
        icon: Globe,
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
        attention: s.attention === true,
        attentionTooltipKey: s.attentionTooltipKey,
      })),
  },
];

function can(ctx, permission) {
  if (typeof ctx.hasPermission === "function")
    return ctx.hasPermission(permission);
  return hasPermission(ctx.me, permission);
}

function canBrowseNav(ctx, permission) {
  return canBrowse(ctx.me, permission);
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
    const rawItems =
      typeof group.items === "function" ? group.items(ctx) : group.items;
    const resolvedItems = uniqueByNavTarget(
      rawItems
        .filter((item) => {
          if (item.visible && !item.visible(ctx)) return false;
          if (
            item.type === "link" &&
            item.to &&
            !canShowNavLink(ctx.me, item.to)
          ) {
            return false;
          }
          return true;
        })
        .map((item) => ({ ...item, label: resolveLabel(item) })),
    );
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
