/**
 * Build lucide-compatible MingCute wrappers and validate export names.
 * Run: node scripts/build-icons.mjs
 *
 * Default: Core Filled. Search: Core Regular (hollow lens).
 * Loaders stay Hugeicons Loading03 (Control Plane Spinner).
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import * as mingcuteFilled from "@mingcute/vue/core-filled";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

/**
 * Lucide export name → MingCute Vue Core Filled export.
 * Prefer uncontained / circular / organic glyphs: cabinet --radius is 1rem,
 * so square badges read as almost-circles and fight the chrome.
 */
const MAP = {
  Activity: "HeartbeatFilled",
  AlertCircle: "AlertOctagonFilled",
  AlertTriangle: "AlertFilled",
  ArrowDown: "ArrowDownFilled",
  ArrowDownToLine: "ArrowToDownFilled",
  ArrowLeft: "ArrowLeftFilled",
  ArrowLeftRight: "TransferHorizontalFilled",
  ArrowRight: "ArrowRightFilled",
  ArrowRightLeft: "TransferHorizontalFilled",
  ArrowUp: "ArrowUpFilled",
  ArrowUpDown: "TransferVerticalFilled",
  ArrowUpToLine: "ArrowToUpFilled",
  Ban: "ForbidCircleFilled",
  Banknote: "BillFilled",
  BarChart2: "ChartBarFilled",
  Barcode: "BarcodeFilled",
  Binary: "CodeFilled",
  BookOpen: "Book2Filled",
  Bot: "RobotFilled",
  Boxes: "CubeFilled",
  Braces: "BracesFilled",
  Cable: "PluginFilled",
  Check: "CheckFilled",
  CheckCircle: "CheckCircleFilled",
  CheckCircle2: "CheckCircleFilled",
  ChevronDown: "DownSmallFilled",
  ChevronLeft: "LeftSmallFilled",
  ChevronRight: "RightSmallFilled",
  ChevronUp: "UpSmallFilled",
  ChevronsLeft: "ArrowsLeftFilled",
  ChevronsRight: "ArrowsRightFilled",
  ChevronsUpDown: "SelectorVerticalFilled",
  Circle: "RoundFilled",
  CircleHelp: "QuestionFilled",
  Clock: "ClockFilled",
  Clock3: "Clock2Filled",
  Coins: "Coin2Filled",
  Copy: "Copy2Filled",
  CornerLeftUp: "CornerUpLeftFilled",
  Database: "ServerFilled",
  Dices: "CubeFilled",
  DollarSign: "CurrencyDollarFilled",
  Download: "Download2Filled",
  EllipsisVertical: "More2Filled",
  Equal: "AlignHorizontalCenterFilled",
  ExternalLink: "ArrowRightUpFilled",
  Eye: "EyeFilled",
  EyeOff: "EyeCloseFilled",
  FileCode2: "FileCodeFilled",
  FileIcon: "FileFilled",
  FileSpreadsheet: "XLSFilled",
  FileText: "Document3Filled",
  Files: "DocumentsFilled",
  Filter: "FilterFilled",
  Folder: "FolderFilled",
  FolderOpen: "FolderOpenFilled",
  FolderTree: "SitemapFilled",
  Gauge: "DashboardFilled",
  GitBranch: "GitBranchFilled",
  Globe: "EarthFilled",
  Globe2: "Globe2Filled",
  GripVertical: "DotsFilled",
  HardDrive: "StorageFilled",
  Hash: "HashtagFilled",
  History: "HistoryAnticlockwiseFilled",
  Home: "Home1Filled",
  House: "Home1Filled",
  Image: "PicFilled",
  ImageDown: "PicFilled",
  ImageOff: "FileForbidFilled",
  Inbox: "InboxFilled",
  KeyRound: "Key1Filled",
  Fingerprint: "FingerprintFilled",
  Shield: "ShieldFilled",
  Languages: "TranslateFilled",
  Layers: "LayersFilled",
  LayoutGrid: "ClassifyFilled",
  Columns3: "Columns3Filled",
  Rows3: "Rows3Filled",
  Link: "LinkFilled",
  Link2: "Link3Filled",
  List: "ListCheckFilled",
  ListChecks: "ListCheckFilled",
  ClipboardList: "ClipboardFilled",
  LogOut: "ExitDoorFilled",
  MapPin: "LocationFilled",
  MessageSquare: "Chat2Filled",
  Minus: "MinimizeFilled",
  Monitor: "DisplayFilled",
  Moon: "MoonFilled",
  MoreHorizontal: "More1Filled",
  Network: "SitemapFilled",
  NotebookPen: "PencilFilled",
  NotebookText: "NotebookFilled",
  Package: "PackageFilled",
  PackagePlus: "PackageFilled",
  Paintbrush: "PaintBrushFilled",
  Palette: "PaletteFilled",
  PanelLeft: "LayoutLeftbarOpenFilled",
  Pause: "PauseFilled",
  Pencil: "PencilFilled",
  Play: "PlayFilled",
  Plug: "PluginFilled",
  Plus: "AddFilled",
  Puzzle: "Plugin2Filled",
  RefreshCw: "Refresh1Filled",
  Ruler: "RulerFilled",
  Save: "CheckFilled",
  ScrollText: "ScrollableListFilled",
  SearchCheck: "SearchFilled",
  SearchIcon: "SearchFilled",
  Settings: "Settings1Filled",
  Settings2: "Settings2Filled",
  Share2: "Share2Filled",
  ShoppingBag: "ShoppingBag1Filled",
  SlidersHorizontal: "Settings2Filled",
  Sparkles: "SparklesFilled",
  Square: "StopCircleFilled",
  Store: "StoreFilled",
  Sun: "SunFilled",
  Tag: "TagFilled",
  Target: "TargetFilled",
  TestTube: "TestTubeFilled",
  ThumbsDown: "ThumbDownFilled",
  ThumbsUp: "ThumbUpFilled",
  ToggleLeft: "ToggleLeftFilled",
  Trash2: "DeleteFilled",
  Truck: "TruckFilled",
  Type: "FontFilled",
  Undo2: "RefreshAnticlockwise1Filled",
  Unlink: "UnlinkFilled",
  Upload: "Upload2Filled",
  User: "User3Filled",
  UserCheck: "UserFollowFilled",
  UserCog: "UserSettingFilled",
  UserMinus: "UserRemoveFilled",
  UserPlus: "UserAddFilled",
  UserRound: "User4Filled",
  Users: "GroupFilled",
  Wand2: "Magic1Filled",
  Wrench: "ToolFilled",
  X: "CloseFilled",
  XCircle: "CloseCircleFilled",
  Zap: "LightningFilled",
};

/** Outline glyphs (hollow lens). Local wrappers — do not import the core-regular barrel. */
const REGULAR = {
  Search: "SearchRegular",
};

const missing = [];
const resolved = {};
for (const [lucide, exportName] of Object.entries(MAP)) {
  if (mingcuteFilled[exportName]) {
    resolved[lucide] = { exportName, pack: "filled" };
    continue;
  }
  missing.push(`${lucide} -> ${exportName}`);
}
for (const [lucide, exportName] of Object.entries(REGULAR)) {
  resolved[lucide] = { exportName, pack: "regular" };
}

if (missing.length) {
  console.error("Missing mappings:\n" + missing.join("\n"));
  const keys = Object.keys(mingcuteFilled).filter((k) => k.endsWith("Filled"));
  for (const line of missing) {
    const lucide = line.split(" -> ")[0];
    const needle = lucide.replace(/([a-z])([A-Z])/g, "$1$2").slice(0, 6).toLowerCase();
    const hits = keys.filter((k) => k.toLowerCase().includes(needle)).slice(0, 8);
    console.error(`  suggestions for ${lucide}: ${hits.join(", ") || "(none)"}`);
  }
  process.exitCode = 1;
  process.exit(1);
}

const filledImports = [
  ...new Set(
    Object.values(resolved)
      .filter((item) => item.pack === "filled")
      .map((item) => item.exportName),
  ),
]
  .sort()
  .map((name) => `  ${name},`)
  .join("\n");

const regularImportLines = [
  ...new Set(
    Object.values(resolved)
      .filter((item) => item.pack === "regular")
      .map((item) => item.exportName),
  ),
]
  .sort()
  .map((name) => {
    const file = name.slice(0, 1).toLowerCase() + name.slice(1);
    return `import { ${name} } from "./${file}.js";`;
  })
  .join("\n");

const exports = Object.entries(resolved)
  .sort(([a], [b]) => a.localeCompare(b))
  .map(
    ([lucide, { exportName }]) =>
      `export const ${lucide} = createMingCuteIcon(${exportName}, "${lucide}");`,
  )
  .join("\n");

const out = `/**
 * Lucide-compatible icon exports backed by MingCute Core Filled (Apache-2.0).
 * Search uses Core Regular (hollow lens). Loaders stay Hugeicons Loading03.
 * Generated by scripts/build-icons.mjs — re-run after changing MAP.
 */
import { defineComponent, h } from "vue";
import {
${filledImports}
} from "@mingcute/vue/core-filled";
${regularImportLines}
import { Loading03Icon } from "./hugeicons";

function createMingCuteIcon(MgcComponent, name) {
  return defineComponent({
    name,
    inheritAttrs: false,
    setup(_, { attrs }) {
      return () => h(MgcComponent, { ...attrs });
    },
  });
}

${exports}

export const Loader = Loading03Icon;
export const Loader2 = Loading03Icon;
`;

const target = path.resolve(__dirname, "../src/lib/icons.js");
fs.writeFileSync(target, out, "utf8");
console.log(`Wrote ${target} (${Object.keys(resolved).length} icons)`);
