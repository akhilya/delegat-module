import { ADAPTER_ICON_URLS, getAdapterIconUrl } from "./adapterIconAssets.js";
import { ATTRIBUTE_GROUP_OPTIONS } from "../constants/attributeGroups.js";

export const ADAPTER_GROUP_ICON_PREFIX = "adapter:";

export const GROUP_LUCIDE_ICON_NAMES = [
  "Layers",
  "Tag",
  "Globe",
  "Ruler",
  "Palette",
  "Settings2",
  "Zap",
  "Package",
  "Boxes",
  "ShoppingBag",
  "Image",
  "Type",
  "Hash",
  "Folder",
  "Sparkles",
  "Wrench",
  "Activity",
  "BookOpen",
  "Circle",
  "Store",
  "CheckCircle2",
  "MoreHorizontal",
];

/**
 * @param {string|null|undefined} icon
 * @returns {string|null}
 */
export function adapterKeyFromGroupIcon(icon) {
  const raw = String(icon ?? "").trim();
  if (!raw.toLowerCase().startsWith(ADAPTER_GROUP_ICON_PREFIX)) return null;
  const key = raw.slice(ADAPTER_GROUP_ICON_PREFIX.length).trim().toLowerCase();
  return key || null;
}

/**
 * @param {string} adapterKey
 */
export function groupIconForAdapter(adapterKey) {
  const key = String(adapterKey ?? "")
    .trim()
    .toLowerCase();
  return key ? `${ADAPTER_GROUP_ICON_PREFIX}${key}` : "";
}

/**
 * @param {string|null|undefined} name
 * @returns {string|null}
 */
export function inferAdapterKeyFromGroupName(name) {
  const n = String(name ?? "")
    .toLowerCase()
    .replace(/[\s_-]+/g, "");
  if (!n) return null;
  for (const key of Object.keys(ADAPTER_ICON_URLS)) {
    const k = key.replace(/[\s_-]+/g, "");
    if (n === k || n.includes(k) || k.includes(n)) return key;
  }
  return null;
}

/**
 * @param {string|null|undefined} groupName
 * @returns {string}
 */
export function defaultLucideIconForGroup(groupName) {
  return (
    ATTRIBUTE_GROUP_OPTIONS.find((option) => option.value === groupName)
      ?.icon ?? ""
  );
}

/**
 * @param {string|null|undefined} groupName
 * @param {string|null|undefined} groupIcon
 * @returns {{ type: "adapter", key: string } | { type: "lucide", name: string } | null}
 */
export function resolveGroupIcon(groupName, groupIcon) {
  const adapterFromStored = adapterKeyFromGroupIcon(groupIcon);
  if (adapterFromStored && getAdapterIconUrl(adapterFromStored)) {
    return { type: "adapter", key: adapterFromStored };
  }

  const lucide = String(groupIcon ?? "").trim();
  if (lucide && GROUP_LUCIDE_ICON_NAMES.includes(lucide)) {
    return { type: "lucide", name: lucide };
  }

  const inferred = inferAdapterKeyFromGroupName(groupName);
  if (inferred && getAdapterIconUrl(inferred)) {
    return { type: "adapter", key: inferred };
  }

  const preset = defaultLucideIconForGroup(groupName);
  if (preset && GROUP_LUCIDE_ICON_NAMES.includes(preset)) {
    return { type: "lucide", name: preset };
  }

  return null;
}

/**
 * Icon to store when the user picks a group name and has not chosen an icon yet.
 * @param {string|null|undefined} groupName
 */
export function suggestedGroupIcon(groupName) {
  const inferred = inferAdapterKeyFromGroupName(groupName);
  if (inferred) return groupIconForAdapter(inferred);
  return defaultLucideIconForGroup(groupName);
}
