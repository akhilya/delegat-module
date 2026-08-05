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
    rounded: "{rounded.md}"
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
**The Semantic-Only Rule.** Hue is reserved for status and irreversible actions. Never paint marketing gradients, brand purple, or decorative accent washes across operational chrome.

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

Flat by default. Depth is tonal (sidebar vs paper, muted fills) and 1px borders — not stacked soft shadows. Outline buttons may use `shadow-xs`; dialogs/sheets rely on overlay scrim + surface tokens, not dramatic drop shadows.

### Shadow Vocabulary
- **Hairline lift** (`shadow-xs` on outline controls): Barely-there separation for stroked buttons.
- **No ambient card stack:** Cards are bordered/tonal, not floated marketing tiles.

### Named Rules
**The Flat-By-Default Rule.** Surfaces rest flat. Shadows appear only as light state affordances (outline button, focused ring), never as decorative hierarchy.

**The Portal Stack Rule.** Overlays follow the z-index scale in `style.css` (`--z-sticky` … `--z-alert`). Popovers must sit above dialogs; toasts above popovers; blocking alerts at the ceiling.

## 5. Components

Primitives live under `frontend/src/components/ui` (shadcn-vue new-york, Lucide). Prefer `pnpm`-style add flows; do not invent parallel controls.

### Buttons
- **Shape:** Medium radius (`rounded-md`, tied to `--radius` 0.5rem)
- **Primary:** Ink fill + primary-foreground; hover at 90% primary
- **Secondary / Outline / Ghost / Link / Destructive:** Standard CVA variants in `button/index.js`
- **Sizes:** default `h-9`, sm `h-8`, lg `h-10`, compact `xs` / icon sizes
- **Focus:** `focus-visible:ring-ring/50` + ring-3; invalid uses destructive ring
- **Touch:** On coarse pointers, `[data-slot="button"]` min-height 2.75rem

### Cards / Containers
- **Corner Style:** Radius from card primitive (aligned to `--radius`)
- **Background:** `{colors.card}` / paper
- **Shadow Strategy:** Flat; border via `{colors.border}`
- **Use:** Group related form sections or auth content — not decorative marketing tiles in the app shell

### Inputs / Fields
- **Style:** Border `{colors.input}`, height `h-9`, `rounded-md`, text `{colors.foreground}`
- **Focus:** Ring token; match `color-scheme` to light/dark
- **Error:** `aria-invalid` → destructive border/ring
- **Password:** Eye toggle in muted → foreground on hover

### Navigation
- **Sidebar:** `{colors.sidebar}` surface, quiet borders, primary for active emphasis sparingly
- **App page chrome:** Catalog and settings shells use `AppPageChrome` (`Page` + `adaptive-padding` + sticky `NavigationBar` + `Breadcrumbs`). Page titles come from breadcrumbs, not `PageHeader`. Do not nest a second `Page` inside settings route children; do not override breadcrumb `mb-4` locally.
- **Auth shell:** Centered brand + card form (`AuthLayout`); language switcher corner; no marketing hero

### Status chips / badges
- Use success / warning / destructive (and muted companions) for sync and marketplace state — never rainbow decoration

## 6. Do's and Don'ts

### Do:
- **Do** keep neutrals dominant; use semantic color only for status and irreversible actions.
- **Do** reuse shadcn-vue patterns with the same spacing and visual weight across pages.
- **Do** load Onest and keep the Cyrillic-safe fallback stack; avoid `system-ui`.
- **Do** honor WCAG AA, visible focus, keyboard paths, and `prefers-reduced-motion`.
- **Do** favor tables, forms, and dialogs that optimize for sustained catalog work.

### Don't:
- **Don't** apply playful, colorful, or marketing-heavy aesthetics to operational tooling.
- **Don't** build decorative dashboards that prioritize "wow" over legibility.
- **Don't** use gradient-and-glass SaaS landing-page styling inside the instance.
- **Don't** introduce purple-on-white / purple-to-indigo themes, neon glow, or emoji chrome.
- **Don't** use side-stripe borders (`border-left`/`border-right` >1px) as colored accents.
- **Don't** use gradient text (`background-clip: text`).
- **Don't** invent a second component kit beside `components/ui`.
