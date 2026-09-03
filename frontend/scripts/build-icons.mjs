/**
 * Build lucide-compatible Hugeicons wrappers and validate export names exist.
 * Run: node scripts/build-icons.mjs
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import * as huge from "@hugeicons/core-free-icons";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

/** Lucide export name → Hugeicons core-free export name */
const MAP = {
  Activity: "Activity01Icon",
  AlertCircle: "AlertCircleIcon",
  AlertTriangle: "Alert02Icon",
  ArrowDown: "ArrowDown01Icon",
  ArrowDownToLine: "ArrowDown02Icon",
  ArrowLeft: "ArrowLeft01Icon",
  ArrowLeftRight: "ArrowLeftRightIcon",
  ArrowRight: "ArrowRight01Icon",
  ArrowRightLeft: "ArrowDataTransferHorizontalIcon",
  ArrowUp: "ArrowUp01Icon",
  ArrowUpDown: "ArrowUpDownIcon",
  ArrowUpToLine: "ArrowUp02Icon",
  Ban: "UnavailableIcon",
  Banknote: "Money01Icon",
  BarChart2: "ChartBarLineIcon",
  Barcode: "BarcodeIcon",
  Binary: "BinaryCodeIcon",
  BookOpen: "BookOpen01Icon",
  Bot: "RobotIcon",
  Boxes: "Package01Icon",
  Braces: "SourceCodeIcon",
  Cable: "CableIcon",
  Check: "Tick02Icon",
  CheckCircle: "CheckmarkCircle02Icon",
  CheckCircle2: "CheckmarkCircle02Icon",
  ChevronDown: "ArrowDown01Icon",
  ChevronLeft: "ArrowLeft01Icon",
  ChevronRight: "ArrowRight01Icon",
  ChevronUp: "ArrowUp01Icon",
  ChevronsLeft: "ArrowLeftDoubleIcon",
  ChevronsRight: "ArrowRightDoubleIcon",
  ChevronsUpDown: "UnfoldMoreIcon",
  Circle: "CircleIcon",
  CircleHelp: "HelpCircleIcon",
  Clock: "Clock01Icon",
  Clock3: "Clock03Icon",
  Coins: "Coins01Icon",
  Copy: "Copy01Icon",
  CornerLeftUp: "ArrowTurnBackwardIcon",
  Database: "DatabaseIcon",
  Dices: "DiceIcon",
  DollarSign: "DollarCircleIcon",
  Download: "Download01Icon",
  EllipsisVertical: "MoreVerticalIcon",
  Equal: "EqualSignIcon",
  ExternalLink: "ExternalLinkIcon",
  Eye: "ViewIcon",
  EyeOff: "ViewOffIcon",
  FileCode2: "SourceCodeIcon",
  FileIcon: "File01Icon",
  FileSpreadsheet: "FileSpreadsheetIcon",
  FileText: "File02Icon",
  Files: "File01Icon",
  Filter: "FilterIcon",
  Folder: "Folder01Icon",
  FolderOpen: "FolderOpenIcon",
  FolderTree: "FolderLibraryIcon",
  Gauge: "DashboardSpeed01Icon",
  GitBranch: "GitBranchIcon",
  Globe: "Globe02Icon",
  Globe2: "Globe02Icon",
  GripVertical: "DragDropVerticalIcon",
  HardDrive: "HardDriveIcon",
  Hash: "HashtagIcon",
  History: "Clock01Icon",
  Home: "Home01Icon",
  House: "Home01Icon",
  Image: "Image01Icon",
  ImageDown: "ImageDownloadIcon",
  ImageOff: "ImageNotFound01Icon",
  Inbox: "InboxIcon",
  KeyRound: "Key01Icon",
  Fingerprint: "FingerPrintIcon",
  Shield: "Shield01Icon",
  Languages: "LanguageCircleIcon",
  Layers: "Layers01Icon",
  LayoutGrid: "DashboardSquare01Icon",
  Columns3: "LayoutThreeColumnIcon",
  Rows3: "LayoutThreeRowIcon",
  Link: "Link01Icon",
  Link2: "Link02Icon",
  List: "Menu01Icon",
  ListChecks: "CheckListIcon",
  ClipboardList: "CheckListIcon",
  Loader: "Loading03Icon",
  Loader2: "Loading03Icon",
  LogOut: "Logout01Icon",
  MapPin: "Location01Icon",
  MessageSquare: "Message01Icon",
  Minus: "MinusSignIcon",
  Monitor: "ComputerIcon",
  Moon: "Moon02Icon",
  MoreHorizontal: "MoreHorizontalIcon",
  Network: "HierarchyIcon",
  NotebookPen: "NoteEditIcon",
  NotebookText: "NoteIcon",
  Package: "Package01Icon",
  PackagePlus: "PackageAddIcon",
  Paintbrush: "PaintBrush01Icon",
  Palette: "PaintBoardIcon",
  PanelLeft: "SidebarLeft01Icon",
  Pause: "PauseIcon",
  Pencil: "Edit01Icon",
  Play: "PlayIcon",
  Plug: "Plug01Icon",
  Plus: "PlusSignIcon",
  Puzzle: "PuzzleIcon",
  RefreshCw: "RefreshIcon",
  Ruler: "RulerIcon",
  Save: "FloppyDiskIcon",
  ScrollText: "ScrollIcon",
  Search: "Search01Icon",
  SearchCheck: "SearchFocusIcon",
  SearchIcon: "Search01Icon",
  Settings: "Settings01Icon",
  Settings2: "Settings02Icon",
  Share2: "Share08Icon",
  ShoppingBag: "ShoppingBag01Icon",
  SlidersHorizontal: "SlidersHorizontalIcon",
  Sparkles: "SparklesIcon",
  Square: "SquareIcon",
  Store: "Store01Icon",
  Sun: "Sun03Icon",
  Tag: "Tag01Icon",
  Target: "Target01Icon",
  TestTube: "TestTubeIcon",
  ThumbsDown: "ThumbsDownIcon",
  ThumbsUp: "ThumbsUpIcon",
  ToggleLeft: "ToggleOffIcon",
  Trash2: "Delete02Icon",
  Truck: "DeliveryTruck01Icon",
  Type: "TextIcon",
  Undo2: "UndoIcon",
  Unlink: "Unlink01Icon",
  Upload: "Upload01Icon",
  User: "UserIcon",
  UserCheck: "UserCheck01Icon",
  UserCog: "UserSettings01Icon",
  UserMinus: "UserRemove01Icon",
  UserPlus: "UserAdd01Icon",
  UserRound: "UserIcon",
  Users: "UserMultipleIcon",
  Wand2: "MagicWand01Icon",
  Wrench: "Wrench01Icon",
  X: "Cancel01Icon",
  XCircle: "CancelCircleIcon",
  Zap: "ZapIcon",
};

const missing = [];
const resolved = {};
for (const [lucide, candidate] of Object.entries(MAP)) {
  if (huge[candidate]) {
    resolved[lucide] = candidate;
    continue;
  }
  // fuzzy fallback: strip Icon/ try variants
  const alts = [
    candidate.replace(/Icon$/, "FreeIcons"),
    candidate.replace(/0[12]Icon$/, "Icon"),
    candidate,
  ];
  let found = null;
  for (const a of alts) {
    if (huge[a]) {
      found = a;
      break;
    }
  }
  if (found) resolved[lucide] = found;
  else missing.push(`${lucide} -> ${candidate}`);
}

if (missing.length) {
  console.error("Missing mappings:\n" + missing.join("\n"));
  // Still write file with available ones; print suggestions for missing
  for (const line of missing) {
    const lucide = line.split(" -> ")[0];
    const needle = lucide.replace(/([a-z])([A-Z])/g, "$1 $2").split(" ")[0];
    const hits = Object.keys(huge)
      .filter((k) => k.endsWith("Icon") && k.toLowerCase().includes(needle.toLowerCase()))
      .slice(0, 8);
    console.error(`  suggestions for ${lucide}: ${hits.join(", ") || "(none)"}`);
  }
  process.exitCode = 1;
}

const imports = [...new Set(Object.values(resolved))]
  .sort()
  .map((name) => `  ${name} as ${name}Data,`)
  .join("\n");

const exports = Object.entries(resolved)
  .sort(([a], [b]) => a.localeCompare(b))
  .map(([lucide, hugeName]) => {
    const opts =
      lucide === "Loader" || lucide === "Loader2"
        ? ", { strokeWidth: null }"
        : "";
    return `export const ${lucide} = createHugeIcon(${hugeName}Data, "${lucide}"${opts});`;
  })
  .join("\n");

const out = `/**
 * Lucide-compatible icon exports backed by Hugeicons (same kit as Control Plane).
 * Free pack is stroke-rounded; we use a denser stroke as a filled-look experiment.
 * Loaders match Control Plane Spinner (Loading03, default stroke).
 * Generated by scripts/build-icons.mjs — re-run after changing MAP.
 */
import { defineComponent, h } from "vue";
import { HugeiconsIcon } from "@hugeicons/vue";
import {
${imports}
} from "@hugeicons/core-free-icons";

/** Denser stroke ≈ filled presence until Pro solid pack is available. */
const FILLED_STROKE_WIDTH = 2;

function createHugeIcon(icon, name, options = {}) {
  const strokeWidth =
    Object.prototype.hasOwnProperty.call(options, "strokeWidth")
      ? options.strokeWidth
      : FILLED_STROKE_WIDTH;
  return defineComponent({
    name,
    inheritAttrs: true,
    setup(_, { attrs }) {
      return () =>
        h(HugeiconsIcon, {
          icon,
          ...(strokeWidth != null ? { strokeWidth } : {}),
          ...attrs,
        });
    },
  });
}

${exports}
`;

const target = path.resolve(__dirname, "../src/lib/icons.js");
fs.writeFileSync(target, out, "utf8");
console.log(`Wrote ${target} (${Object.keys(resolved).length} icons)`);
