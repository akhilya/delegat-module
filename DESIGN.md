---
name: Delegat.Module Instance
description: Neutral operational catalog UI — Notion/Linear density, semantic status color only
colors:
  background: "oklch(1 0 0)"
  foreground: "oklch(0.145 0 0)"
  primary: "oklch(0.205 0 0)"
  primary-foreground: "oklch(0.985 0 0)"
  secondary: "oklch(0.97 0 0)"
  secondary-foreground: "oklch(0.205 0 0)"
  muted: "oklch(0.97 0 0)"
  muted-foreground: "oklch(0.556 0 0)"
  accent: "oklch(0.97 0 0)"
  accent-foreground: "oklch(0.205 0 0)"
  card: "oklch(1 0 0)"
  border: "oklch(0.76676 0.00009 271.152)"
  input: "oklch(0.76676 0.00009 271.152)"
  ring: "oklch(0.708 0 0)"
  destructive: "oklch(0.577 0.245 27.325)"
  warning: "oklch(0.769 0.188 70.08)"
  warning-foreground: "oklch(0.425 0.111 55)"
  warning-muted: "oklch(0.92 0.08 70)"
  success: "oklch(0.62924 0.13932 161.221)"
  success-foreground: "oklch(0.36 0.1 161)"
  success-muted: "oklch(0.92 0.06 161)"
  sidebar: "oklch(0.985 0 0)"
  sidebar-border: "oklch(0.922 0 0)"
typography:
  display:
    fontFamily: "Onest, Helvetica Neue, Arial, Liberation Sans, sans-serif"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "-0.018em"
  headline:
    fontFamily: "Onest, Helvetica Neue, Arial, Liberation Sans, sans-serif"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "-0.018em"
  title:
    fontFamily: "Onest, Helvetica Neue, Arial, Liberation Sans, sans-serif"
    fontSize: "1rem"
    fontWeight: 500
    lineHeight: 1.35
    letterSpacing: "-0.005em"
  body:
    fontFamily: "Onest, Helvetica Neue, Arial, Liberation Sans, sans-serif"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: 1.55
    letterSpacing: "-0.005em"
    fontFeature: "tnum"
  label:
    fontFamily: "Onest, Helvetica Neue, Arial, Liberation Sans, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 500
    lineHeight: 1.4
    letterSpacing: "-0.005em"
rounded:
  control: "calc(1rem - 2px)"
  sm: "calc(0.5rem - 4px)"
  md: "calc(0.5rem - 2px)"
  lg: "0.5rem"
  xl: "calc(0.5rem + 4px)"
spacing:
  control-h: "2.25rem"
  control-h-sm: "2rem"
  control-h-lg: "2.5rem"
  page-pad-mobile: "0.75rem"
  page-pad-desktop: "4rem"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-foreground}"
    rounded: "{rounded.md}"
    padding: "0.5rem 1rem"
    height: "{spacing.control-h}"
    typography: "{typography.label}"
  button-primary-hover:
    backgroundColor: "color-mix(in oklch, {colors.primary} 90%, transparent)"
  button-secondary:
    backgroundColor: "{colors.secondary}"
    textColor: "{colors.secondary-foreground}"
    rounded: "{rounded.md}"
    height: "{spacing.control-h}"
  button-outline:
    backgroundColor: "{colors.background}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.md}"
    height: "{spacing.control-h}"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.foreground}"
    rounded: "{rounded.md}"
    height: "{spacing.control-h}"
  button-destructive:
    backgroundColor: "{colors.destructive}"
    textColor: "{colors.primary-foreground}"
    rounded: "{rounded.md}"
    height: "{spacing.control-h}"
  input-default:
    backgroundColor: "{colors.background}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.control}"
    height: "{spacing.control-h}"
    padding: "0.5rem 0.75rem"
---

# Design System: Delegat.Module Instance

## 1. Overview

**Creative North Star: "The Catalog Workbench"**

This is an operational tool, not a brochure. The visual system behaves like a well-lit workbench: neutral surfaces, sharp type hierarchy, and status color only where the job demands attention. Density serves long sessions of list/edit/sync work; breathing room prevents fatigue without drifting into marketing whitespace.

The system rejects playful chrome, gradient-glass SaaS theater, and any decoration that competes with catalog data. Structure comes from type weight, whitespace, and consistent shadcn-vue (new-york / neutral) patterns — not from ornamental cards or glow.

**Key Characteristics:**
- Single family: Onest (Cyrillic-safe), no `system-ui`
- Restrained OKLCH neutrals; semantic color for status only
- Flat-by-default elevation; borders and tonal layers over heavy shadows
- Light default + full `.dark` token swap
- shadcn-vue primitives only; no parallel custom control kits
- Apple Settings language for catalogs: inset grouped lists (one paper group, equal rows, one shared ring)

## 2. Colors

A near-achromatic neutral palette with ink primary. Chroma appears only for destructive, warning, success, and chart series.

### Primary
- **Ink Primary** (`oklch(0.205 0 0)`): Primary actions, strong text on light, inverted on dark. Carries ≤10% of any screen as filled chrome; rarity is intentional.

### Neutral
- **Paper** (`oklch(1 0 0)`): App background and cards (light).
- **Ink** (`oklch(0.145 0 0)`): Body text (light).
- **Mist** (`oklch(0.97 0 0)`): Secondary / muted / accent fills.
- **Quiet** (`oklch(0.556 0 0)`): Muted foreground, helper text.
- **Rule** (`oklch(0.76676 0.00009 271.152)`): Borders and inputs.
- **Sidebar Mist** (`oklch(0.985 0 0)`): Sidebar surface, slightly cooler than pure paper.

### Semantic (status only)
- **Alert Red** (`oklch(0.577 0.245 27.325)`): Destructive actions and errors.
- **Caution Amber** (`oklch(0.769 0.188 70.08)`): Warnings; muted/foreground companions for badges.
- **Confirm Green** (`oklch(0.62924 0.13932 161.221)`): Success / healthy sync.

### Named Rules
**The Semantic-Only Rule.** Hue is reserved for status and irreversible actions. Never paint marketing gradients, brand purple, or decorative accent washes across operational chrome. The Control Plane button sheen is a short overlay (white on ink fills, dark on inverted paper fills), not a hue wash.

**The One Voice Rule.** Filled primary occupies ≤10% of a given screen. If every control shouts primary, none of them do.

Dark theme swaps the same roles via `.dark` in `frontend/src/style.css` (ink↔paper inversion; status tokens recalibrated for contrast).

## 3. Typography

**Display Font:** Onest (Helvetica Neue, Arial, Liberation Sans)
**Body Font:** Onest (same stack)
**Label/Mono Font:** Onest; tabular numerals via `font-feature-settings: "tnum"` / `font-variant-numeric: tabular-nums` on number inputs

**Character:** One humanist sans for everything. Cross-OS consistency matters more than display flair; `system-ui` is forbidden because it fragments Cyrillic rendering.

### Hierarchy
- **Display / Headline** (600, line-height 1.2, tracking −0.018em): Page titles and section heads via `h1`–`h6` + `--font-display`.
- **Title** (500, ~1rem): Card titles, dialog titles, table section labels.
- **Body** (400, 14px mobile / 16px ≥640px, line-height 1.55, tracking −0.005em): Default reading and form copy. Cap line length near 65–75ch for prose; tables may exceed.
- **Label** (500, 0.875rem): Buttons, field labels, compact chrome.

### Named Rules
**The Single-Family Rule.** Never introduce a second display face for “marketing polish” inside the instance. Onest is the source of truth (loaded in `index.html`).

**The Tabular Digits Rule.** Prices, IDs, and quantities use tabular numerals so columns align.

## 4. Elevation

Surfaces stay flat: depth is tonal (sidebar vs paper, muted fills) and 1px borders — not stacked card shadows. Buttons are the exception (same as Control Plane): a short white top sheen and a 1px drop shadow. Dialogs/sheets rely on overlay scrim + surface tokens, not dramatic drop shadows. The scrim fades; the panel fades and zooms.

### Shadow Vocabulary
- **Button lift** (`0 1px 2px oklch(0 0 0 / 0.1)`): Hairline drop shadow on `default`, `outline`, `secondary`, and `destructive`. Ghost and link have none. Grouped segments (`ButtonGroup`) share one plane — no per-segment shadow.
- **Button sheen:** `linear-gradient(180deg, white 8%, transparent ~40%)` on default, outline, secondary, destructive in **light**. Not a page-wash and not hue. Ghost / link have no sheen. In `.dark`, inverted primary is paper (light fill); sheen is a short **dark** overlay (`oklch(0 0 0 / 0.12)`). Do not pin light-theme `--primary` on `html` (branding only inlines a custom hex).
- **No ambient card stack:** Cards are bordered/tonal, not floated marketing tiles.

### Named Rules
**The Flat-By-Default Rule.** Surfaces rest flat. The only standing shadow is the Control Plane button lift (plus the focused ring). Do not float cards.

**The Portal Stack Rule.** Overlays follow the z-index scale in `style.css` (`--z-sticky` … `--z-alert`). Popovers must sit above dialogs; toasts above popovers; blocking alerts at the ceiling.

### Motion
One language for the whole instance. Motion shows state (open, press, toggle), not page-load theater.

- **Curve:** `--ease-out-quart` (`cubic-bezier(0.25, 1, 0.5, 1)`). No bounce, no elastic.
- **Press:** `--duration-press` 120ms. `Button` scales to 0.98 on `:active` (not `link`).
- **UI chrome:** `--duration-ui` 150ms. Menus, tooltips, tab content fade, tab list pill (slides to the active trigger; light is paper on muted, dark is muted on `muted/20` — not `input/30`), sidebar nav pill (slides to the current page in a group; switching groups enters from the edge you came from), nav group open/close, switch thumb, inset row hover, chevrons.
- **Overlays:** `--duration-overlay` 200ms in, `--duration-overlay-out` 150ms out. Dialog, confirm, scrim: fade + zoom.
- **Drawers:** `--duration-drawer` 280ms in, `--duration-drawer-out` 210ms out. Sheets slide from the edge and fade (was 500ms; too slow for catalog work).
- **AI chat:** tool and Python script rows stay collapsed. New steps, the latest turn, and status marks fade (and rise 4px) at `--duration-overlay` / `--duration-overlay-out`. No thread stagger, no bounce. Empty thread: title in the middle, three rotating catalog starters docked above the composer (new conversation or page changes the set).
- **Route enter:** same `.page-region-in` as region loaders (`--duration-overlay`, opacity only). No slide, no stagger, no table choreography. Honor `prefers-reduced-motion` (already global in `style.css`).

## 5. Components

Primitives live under `frontend/src/components/ui` (shadcn-vue new-york, Lucide). Prefer `pnpm`-style add flows; do not invent parallel controls.

### Buttons
- **Shape:** Larger radius (`rounded-md` / `rounded-lg`, tied to `--radius` **1rem**). `rounded-full` is only the circular operations and AI FABs (bottom-right). Do not paint chips, icon buttons, or fields as stadiums.
- **Primary:** Ink fill + primary-foreground in light; inverted paper fill in dark. Hover at 90% primary; Control Plane sheen (white on ink, dark on paper) + hairline drop shadow (`style.css`)
- **Secondary / Outline:** Same sheen polarity as primary (white on light theme fills; dark overlay in `.dark`) + the same drop shadow
- **Ghost:** Flat (no sheen, no shadow). **Link:** flat (no sheen, no shadow)
- **Destructive:** Same sheen and drop shadow as primary
- **Sizes:** default `h-9`, sm `h-8`, lg `h-10`, compact `xs` / icon sizes
- **Focus:** `focus-visible` keeps the 3px ring **inside** the control (`--tw-ring-inset: inset` in `style.css`). An outer halo is clipped by shell / navbar / sheet `overflow-hidden`. Lift variants also keep the drop shadow (composite `box-shadow` in `style.css`)
- **Touch:** On coarse pointers, `[data-slot="button"]` min-height 2.75rem
- **Press:** `:active` scale 0.98 at `--duration-press` (not on `link`)
- **Do not** restyle sheen/shadow at call sites; change `style.css`
- **Floating companion buttons:** Documentation, Operations, and AI use opaque token fills in both themes, including hover and open/outline states. Their tooltip delay is 100ms (also for compact navbar companions); do not inherit translucent outline backgrounds.

### Cards / Containers
- **Corner Style:** Radius from card primitive (aligned to `--radius`)
- **Background:** `{colors.card}` / paper
- **Shadow Strategy:** Flat; border via `{colors.border}`
- **Use:** Group related form sections or auth content — not decorative marketing tiles in the app shell

### Dialogs / Modals
- Schedule creation/editing uses the standard bar dialog with `SettingsFormSection` / `SettingsFormField`: one full-width field column, no nested inset-list cards or per-field width caps.
- **Title (forms / sheets):** `text-base font-semibold tracking-tight leading-none` on `DialogTitle` / `SheetTitle` — plain text only, no leading decorative icons
- **Title (confirm):** `AlertDialogTitle` is `text-lg font-medium tracking-tight leading-snug`, centered — still plain text, no header icons
- **Header / footer layouts:**
  - **Default** — inside padded content (`DialogContent` `p-6`): bare `DialogHeader` / `DialogFooter`
  - **Bar** — `variant="bar"` when content is `p-0` / `gap-0`: header `border-b px-6 py-4`, footer `border-t px-6 py-4` (built into primitives)
  - **Confirm** — `AlertDialogContent`: `p-8` / `gap-8` / `sm:max-w-md` / `rounded-xl` / `ring-1 ring-foreground/5`. Copy centered. Two actions: equal-width Cancel / Confirm (`size` `lg`, `AlertDialogFooter` `layout="split"`). Three actions: full-width stack (`layout="stack"`) — preferred, then alternative, then Cancel — never three columns
- **Overlay:** Dialog, Sheet, confirm alerts, and the product photo lightbox share `bg-black/50` + `backdrop-blur-xs`. Overlay fades; the dialog/confirm panel also zooms. Do not override at call sites. Lightbox close / prev / next / index sit on paper chips (`outline` + `bg-background`), not ghost ink on the photo. **Exception — AI chat:** no overlay, not modal, does not dismiss on outside click (companion while working in the catalog). Close from the sheet header, Escape, or the FAB.
- **Inset chrome (cabinet sheets):** float 8px off the viewport (`top/right/bottom/left-2`, `sm:left-auto` on a right sheet), `rounded-xl`, 1px hairline at ~18% foreground plus a drop shadow (`8px 20px` on sheets; the desktop sidebar stays the quieter `3px 8px`). Operations, AI, docs, orders, notes, logs, and mapping preview use `SheetContent inset` with `resizable` (capsule drag handle on the free edge). The **desktop sidebar** is the same inset panel + handle (width persisted). The handle is the capsule only (no full-height wash). The mobile sidebar sheet is inset, not resizable, and closes after a nav option. On a touch or pen pointer, tapping a context-menu target opens that menu. AI has no overlay; other inset sheets keep the platform scrim.
- Do not invent per-form-dialog title sizes or ad-hoc `px-6 py-5` / `pt-6 pb-4` header chrome
- Icons stay in buttons, menus, empty states, and inline status — not the modal chrome header

### Menus / popovers
Floating lists (dropdown, context menu, select, searchable select, combobox, popover, tooltip) share one Apple menu material:

- Filter option labels stay on one line (`min-w-0 flex-1 truncate`); keep full labels in `title` and `textValue` so long names do not inflate rows or lose keyboard typeahead.

- **Surface:** `cn-menu-translucent` — paper mixed with blur (`blur(20px) saturate(180%)`), `rounded-lg`, hairline `ring-foreground/5` (not a hard `border`), `shadow-lg`. Solid fill when `prefers-reduced-transparency`
- **Rows:** `rounded-md`, `px-3 py-2`, tonal `muted/80` highlight — same family as inset grouped rows, denser
- **Choice:** radio and checkbox rows in menus, plus select / searchable-select / combobox options, show a trailing check (`Check`) — not a leading radio disc. Leading icons stay on the left of the label
- **Labels:** `text-xs` muted captions. Separators: `bg-border/50`
- **Do not** restyle each call site; change the primitives in `frontend/src/components/ui/{dropdown-menu,context-menu,select,popover,combobox,tooltip}` and `lib/menuSurface.js`
- Nested submenus portal to the document (same as the parent panel). Do not render them inside the frosted parent — `overflow` + `backdrop-filter` clip the nested panel (user menu language, adapter connection submenus)

### Inputs / Fields
- **Style:** Border `{colors.input}`, height `h-9`, `rounded-control` (`--radius-control`, matching buttons' `rounded-md`, 14px at the default scale), text `{colors.foreground}`. Text inputs, textareas, number fields, input groups, select / searchable-select / multi-select triggers, and search share this radius regardless of height. Do not override form corners at call sites; grouped controls retain their intentional joined edges. `cn()` registers the control radius with Tailwind Merge so grouped `rounded-none` overrides still work. Keep containers and overlays on the existing nearby radius scale, not unrelated local shapes.
- **Focus:** Ring token; match `color-scheme` to light/dark (`:root` light, `.dark` dark) so Chrome scrollbars follow the theme. Platform bars are 6px, thumb from `--muted-foreground` (not the OS light overlay). `no-scrollbar` still hides (sidebar nav).
- **Error:** `aria-invalid` → destructive border/ring
- **Password:** Eye toggle in muted → foreground on hover
- **Checkbox:** only the shared shadcn `Checkbox`. Its 16px visual square keeps proportional `rounded-xs` corners; the actual button is 32px (44px on coarse pointers), with button-family corners and visible focus. Do not shrink the hit area, translate the control or add per-page checkbox offsets.
- **Forms:** every labeled stack = `FieldGroup` + `Field` (prefer `SettingsFormField`); validated fields wrap with vee-validate `FormField`. No raw `Label`+`Input` / `FormItem` alone without Field. `SettingsFormSection` already supplies FieldSet + FieldGroup. Same-rank section titles (two columns, picker + form) use `SettingsFormSection` / `InsetGroupLabel` — muted `text-xs` with `px-1`, not a `text-sm` heading and not a browser `<legend>` next to that caption. `FieldLegend` must match `InsetGroupLabel`. Stacked field labels are the same `text-xs` caption (not a heading under the section title). Inline / horizontal field labels stay `text-sm`.

### Navigation
- **Sidebar:** `{colors.sidebar}` mist surface, floated 8px off the viewport (`rounded-xl`, same hairline + quiet drop shadow as inset sheets) with the capsule resize handle on the free edge. Nav groups are inset grouped lists (paper fill, one shared hairline ring, equal-height rows). The current page is a primary (ink) pill with semibold label; inside a group the pill slides at `--duration-ui`. Switching groups, it enters from the first or last row of the destination (the direction you came from). Hover on other rows stays tonal. Quiet borders; no second nested outline.
- **App page chrome:** Catalog and settings shells use `AppPageChrome` (`Page` + `adaptive-padding` + `NavigationBar` + `Breadcrumbs`). The bar teleports to `#app-navbar-host` above `[data-slot="page-scroll"]` and never scrolls with the page. Page titles come from breadcrumbs, not `PageHeader`. Do not nest a second `Page` inside settings route children or override breadcrumb `mb-4` locally. Navbar and content use the same `adaptive-padding` gutters. Below `lg`, the trail is the current page only. Layout follows available panel width: below 64rem, tools occupy a centered row below title/actions; above it, equal side tracks center the middle within the padded area, with up to 40rem / 50% available for tabs. Search has its own narrower cap. `ResizeObserver` reports actual bar height to `AppPageChrome`. Optional `nav-tabs` occupy a separate horizontally scrollable navbar row. Page Help is always available as a ghost `CircleHelp` button; its label appears only with at least 88rem of navbar content width.
- **List search and filters:** `PageListToolbar` composes `NavSearchField`, an optional standard outline filter button, and compact actions. It is capped at 28rem and shrinks on phones. Only navbar placement centers it; in-page toolbars and tabs stay left-aligned. Search uses the same `h-9`, `rounded-control`, input hairline, and leading magnifier everywhere. Filters use existing shadcn-vue `DropdownMenu` primitives, radio items/submenus (`FilterMenuGroup`), and a normal Reset menu item. No custom popup header/footer, form panel, or floating counter badge. Reset preserves the search query. Preserve existing server/local search and submission behavior. Settings log pages use `SettingsNavbarTools` into the shell-owned host; API log filters stay in-page below the primary tabs. On adapter connections, Settings / Mappings / Products remain in the main navbar center; product-view tabs and search are left-aligned above the product list.
- **Page scrolling and gutters:** `Page` is `min-w-0 w-full`. Catalog products fill the pane under the bar; `[data-slot="table-scroll"]` is the table's XY scroller (`overflow-auto`, `thead` sticky `top: 0`). Connection Products uses the same fill pane; settings/mappings grow with the page. Page gutters are **8vw** above 2000px (right at least FAB clearance), with tighter fixed gutters at Full HD and below. AI/background-job FABs are bottom-right from `lg` up; below `lg` they are compact navbar icons without the FAB gutter. Pagination/bulk footers (`PageDockFooter`) sit under the last row when the table fits and pin to the inset bottom only on page overflow. Footers have no top separator, ring, or rounding; the table owns the outline.
- **Auth shell:** Full-viewport canvas (`AuthLayout`), same family as first-run Setup: large title, no card, language switcher in the corner; no store logo (many stores). **Unavailable** (`ServiceLayout`) is the same canvas without brand marks or a tab icon.
- **API navigation:** Keys / Request logs are primary navbar-center tabs; request-log search and filters stay left-aligned above the log list, not in place of the primary tabs.
- **Theme boot:** `index.html` applies the saved `vueuse-color-scheme` before first paint and paints the document canvas with matching background fallbacks. Keep these aligned with `theme.js` and the light/dark `--background` tokens so reloading never flashes white in dark mode.

### Tables
- All cabinet tables have one flat, rounded outline: `1px solid var(--border)` and `var(--radius)`. Shared CSS targets `table-container`, `table-scroll`, and `table-frame`; do not add per-page border variants or nested outlines.
- Edge cells and headers share 24px outer padding. A leading selection column is 80px wide with centered checkboxes and 16px side padding, including fixed-layout and catalog tables. Never zero checkbox-cell padding or compress the selection track for density.
- File managers already live in a framed parent. Their internal scroll owner opts out with `data-table-outline="none"`; keep one outer outline and preserve the file pane's scrolling.
- Put the outline on the existing scroll owner. Action footers stay outside the outline and have no top separator: the table's bottom border supplies that boundary. This includes `PageDockFooter`, `LogTableFrame`, adapter products, and file actions. Page-scrolling tables clip their corners without introducing an overflow ancestor, preserving sticky headers; catalog, adapter, file, and SQL-result panes retain their existing scroll behavior. Loading/empty regions without a table stay unframed.
- Adapter product lists use explicit `colgroup` widths and fixed layout, with minimum widths of 78rem (adapter products) and 64rem (products to publish). Keep names readable, numerical columns right-aligned, 16px horizontal cell padding, and rows at least 64px high. Narrow panes scroll horizontally rather than squeezing columns; headers remain sticky in the same scroll owner.

### Empty and unavailable states
- Empty-state icons are bare muted glyphs, not bordered tiles. Use one short title and at most one useful sentence; omit descriptions that only repeat the title or an obvious action.
- `/unavailable` shows one short heading, one recovery sentence and one retry button. Automatic polling and return navigation remain active without a second status paragraph or speculative technical explanations.

### File drops and assistant attachments
- The file manager and assistant share `FileDropOverlay`: an inset, neutral dashed target, bare upload icon, short instruction and optional hint, with a quiet opacity transition. The overlay does not intercept pointer events or move focus while dragging.
- File attribute fields use the standard `h-9` shadcn-vue outline trigger. Keep the product file inventory in its anchored shadcn-vue popover, with search for larger inventories and a bounded scroll list; never render the full inventory inline in the product form. Multiple selections stay neutral rather than using the single-current-item primary fill.
- The assistant uses the shared paperclip icon for attachment picking. Individual files can be dropped anywhere in the chat sheet when file permissions, an enabled model and an idle composer allow it. Drops add to the same draft attachment list as the picker; they never upload or send automatically. Folder drops are rejected with a localized hint; text and link drags keep native editing behavior.
- Hide the assistant model selector when exactly one enabled model is available. Preserve automatic/default model resolution and the no-model state; show the selector for two or more choices.

### Inset grouped lists
iOS Settings / macOS source-list geometry on Catalog Workbench tokens. Primitive: `InsetGroup` / `InsetGroupItem` / `InsetGroupLabel` (`frontend/src/components/ui/inset-group`).

- Product-import category choices use native inset row buttons with a trailing check for the single selected mode (existing / create / none), not radio discs. Render the selected mode's picker or name field below the group, never as an interactive descendant of a choice button.

- **Shell:** `rounded-xl bg-card p-1` and one hairline ring (`ring-sidebar-border` on mist/sidebar; `ring-border/70` on paper pages). Space **between** groups, not between rows inside a group.
- **Rows:** equal padding, inner `rounded-lg`, hover tonal. A hairline gap (`gap-0.5`) inside the group keeps those inner corners visible so adjacent rows do not fuse. Do not give each row its own card, border, or nested outline. Leading icon sits in the row (no bordered tile). Navigation rows use a trailing chevron; the whole row is the control.
- **Current / selected:** primary ink fill + semibold only for a **single** current item (sidebar page, tools picker). Multi-select (roles, permission grants, store members) stays quiet paper + a trailing check — do not paint every chosen row primary.
- **Selectable catalog lists:** groups, categories, and attribute definitions share `ListSelectAllRow` as the first row inside the inset group. `ListBulkActions` sits below the group: selected count, ghost clear, optional outline Edit, destructive Delete. No separate selection toolbar above the list. Keep rows and unsaved forms mounted after mutations; patch local state instead of reloading the page.
- **Row delete controls:** use `Button reveal-on-row-hover` for row-scoped trash actions. Shared CSS reveals them on row hover or keyboard focus only when the pointer supports desktop hover; touch keeps them visible. Preserve their layout space. Bulk actions, context menus, and explicit danger-zone actions remain visible. Custom row containers use `data-row-actions`; inset rows and table rows are recognized automatically.
- **Orders:** sidebar lists each GetOrders connection; status mailboxes are navbar tabs (hidden when that adapter has no such step). `/orders` is four all-marketplace tabs (To pack / Being packed / In delivery / Delivered) with the same Mail-style inset cells (overlapping thumbs, product name, order id, amount; muted status). The mailbox table uses the shared rounded outline plus a bar sheet. Packing overview and mailboxes scroll with the page; catalog products scroll inside the table. Mailbox table headers stick at the top of the page scroller; pagination / bulk footers sit under the last row when the table fits, and pin to the inset bottom only when the page overflows (no extra separator or ring). Product cells are identity rows (thumb + two truncated lines); photo size is Small / Medium / Large from the table menu (shared with the catalog and the packing overview thumb stack). Missing or 404 marketplace photos use the empty thumb placeholder, not alt text on a broken image. Search by order number looks up that order, shows that row in the table (not the rest of the mailbox), and opens the mailbox that holds it. Sheet status buttons are only seller actions that marketplace allows (Ozon/WB cannot mark delivered). The barcode / packing-label button is only shown when that marketplace can print it (Ozon FBS Ready to ship; Wildberries in a supply or in delivery; Yandex while packing). On Wildberries the pack confirm picks a supply and optional box on that same sheet (no separate supplies page); Ready to ship can download the box barcode, and ship hands over the whole supply. Four or more lines collapse behind Show N more. **Store notes** and the product **Notes** tab use the same inset grouped list (row opens a bar sheet). Importance is a quiet trailing status word — semantic color only for High / Critical / Emergency. Product **Sizes** is one inset group per size: size, SKU, price, and stock are labeled fields with input chrome (not transparent trailing text); Add size is the last group. Product **Adapters** is one inset group per section. Each connection is a single row (icon, name, quiet caption, status, ⋯). Left-click and right-click on the row body open the actions menu at the cursor. Mapping and marketplace live in that menu; remote sizes disclose from a trailing control when the category has sizes.
- **Do not** present a catalog of tools or settings as a grid of identical icon + title + CTA cards.

**The Inset Grouped List Rule.** Settings, tools, and pickers use inset grouped lists: mist or paper page, one paper group, one shared hairline ring, equal-height rows. Not a grid of separate cards and not a nested outline on children. Catalog products scroll inside the table; packing overview and order mailboxes scroll with the page; their footers sit under the last row unless the page overflows.

### Toasts (Sonner)
- Mount **`Toaster` from `@/components/ui/sonner`** (themed wrapper), not raw `vue-sonner`
- **Surface:** opaque `{colors.popover}` + hairline border and quiet floating shadow; `--radius` corners. No tinted error panel or glass effect.
- **Structure:** leading 20px product status icon, compact 13px semibold title, optional 12px muted hint, and an inset 28px dismiss control. Preserve native Sonner actions, swipe, focus and reduced motion.
- **Copy:** localized semantic API code only; never append raw response details, JSON, stack traces or validation dumps. HTTP 5xx uses one short server-error title and retry-later hint; concurrent server failures update one notification. Field errors remain on the form.
- **Defaults:** 5 seconds, up to three visible notifications, localized dismiss label. Keep success messages single-line when possible.
- **Placement:** on desktop (`lg+`), bottom-right toasts stay to the left of the companion buttons with a 16px gap, following `useCompanionSheets().fabRight` and the same horizontal transition as the buttons. Keep the normal bottom offset; do not lift notifications above the cluster. Mobile placement is unchanged.
- **Semantics:** default toasts stay neutral; icons carry success/error. Optional `richColors` maps to success/error/warning muted tokens — do not invent Sonner HSL defaults
- **Stack:** `z-index: var(--z-toast)`

### Status chips / badges
- Product gallery labels (Main / Video) use the shared shadcn-vue `Badge` with default / secondary variants; keep badge typography and geometry in the primitive rather than local mini-pills.
- Product editing uses `ProductGallerySidebar` for both the properties-page gallery and the Media tab. `ProductMediaEditor` composes the same tiles in a responsive grid (12rem minimum, shrinking to the available width on narrow panes) for selected and available storage media. Photo thumbnails fill the square with `object-cover`; the full preview uses an explicitly viewport-sized `object-contain` stage, including upscaling smaller sources without cropping. Do not add a separate image-only editor or thumbnail style. Media order and selection share the product save flow; product deletion remains on Properties only.
- Media action buttons use opaque project surfaces, not translucent fills. In the full photo preview, empty object-contain margins dismiss the overlay just like the outer backdrop; clicking the painted photo does not.
- Use success / warning / destructive (and muted companions) for sync and marketplace state — never rainbow decoration
- Technical identifier tags, including AI Model ID, use the shared `Badge`, not locally styled code blocks. Constrain long values with a truncated child and preserve the full identifier in `title`. Model tables keep a readable minimum width and scroll on narrow panes rather than collapsing identifier columns.

### Loaders / Spinner
- **Component:** only `Spinner` (`frontend/src/components/ui/spinner`) — Hugeicons `Loading03`, default stroke
- **UI icons:** MingCute Core Filled behind Lucide import names (`frontend/src/lib/icons.js`). The search magnifier is Core Regular (`SearchRegular`) — hollow lens, not a filled blob. Do not paint a fake fill with stroke width. Do not map to `*Square` badges (`CloseSquare`, `LayoutGrid`, `ExternalLink` window): `--radius` is 1rem, so those squircles fight the chrome. Checks use `CheckFilled`; grids use `ClassifyFilled` (circles).
- **Save actions:** the shared `Save` alias uses the plain `CheckFilled` glyph instead of a floppy disk; keep existing localized button labels and loading states.
- **Size prop (formal scale):** `xs` 12 · `sm` 14 · `default` 16 · `lg` 20 · `xl` 24 · `2xl` 32
- **Default:** actions (buttons, menus, forms) use `default` (omit prop)
- **Region loading:** EmptyState / full pane / products table → `2xl`; the loading EmptyState has **no** card ring. Spinner and first paint of the page body fade in at `--duration-overlay` (`.page-region-in`). Overlays / mid-block (dialogs) → `xl`; panel chrome (sidebars, sheets) → `lg`; actions → default
- **Do not** invent parallel loaders with raw `Loader2` + ad-hoc `size-*` (except dynamic status *icon* slots that need a Lucide-compatible component export)

## 6. Do's and Don'ts

### Do:
- **Do** keep neutrals dominant; use semantic color only for status and irreversible actions.
- **Do** reuse shadcn-vue patterns with the same spacing and visual weight across pages.
- **Do** load Onest and keep the Cyrillic-safe fallback stack; avoid `system-ui`.
- **Do** honor WCAG AA, visible focus, keyboard paths, and `prefers-reduced-motion`.
- **Do** favor tables, forms, and dialogs that optimize for sustained catalog work.
- **Do** use inset grouped lists for tools, pickers, and settings-style catalogs.

### Don't:
- **Don't** apply playful, colorful, or marketing-heavy aesthetics to operational tooling.
- **Don't** build decorative dashboards that prioritize "wow" over legibility.
- **Don't** use gradient-and-glass SaaS landing-page styling inside the instance.
- **Don't** introduce purple-on-white / purple-to-indigo themes, neon glow, or emoji chrome.
- **Don't** use side-stripe borders (`border-left`/`border-right` >1px) as colored accents.
- **Don't** use gradient text (`background-clip: text`).
- **Don't** invent a second component kit beside `components/ui`.
- **Don't** present a catalog of tools or settings as a grid of identical cards.
