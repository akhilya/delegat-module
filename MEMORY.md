# Delegat.Module (instance) — project MEMORY

Living decision log for agents and humans. Prefer this file + `docs/` over ad-hoc chat memory.
If the human explicitly changes canon, update **this file in the same change**.

Chat with the human may be Russian; **everything written into this repo stays English** (except vue-i18n locale string values).

---

## Product & scope

- **Product:** Delegat.Module — per-tenant catalog / marketplace control panel (instance).
- **This repo:** `dashboard` / `delegat-module` (GitHub: `akhilya/delegat-module`).
- **Sibling Control Plane:** local `C:\Users\ARTEM\projects\delegat-module-platform` — billing, provision, Ops, Platform Link **server**, public docs site. Do **not** implement CP features here.
- **Platform Link client** lives in this instance; contract canon is CP `docs/platform-link.md` (instance stub: [`docs/platform/platform-link.md`](docs/platform/platform-link.md)).
- **Canon docs (instance):** [`docs/`](docs/README.md) — capabilities, releasing, Public API, adapters.
- **Design canon:** [`PRODUCT.md`](PRODUCT.md), [`DESIGN.md`](DESIGN.md), [`DESIGN.json`](DESIGN.json).

---

## Non-negotiables

1. **English in repo** — code, identifiers, comments, commits, PRs, docs (i18n locale values may be Russian).

2. **EF Core migrations only via `dotnet ef` CLI — never hand-write migration classes.**
   - A valid migration is a **pair**: `YYYYMMDDHHMMSS_Name.cs` + `YYYYMMDDHHMMSS_Name.Designer.cs`.
   - The Designer **must** carry `[Migration("YYYYMMDDHHMMSS_Name")]`. Without it, EF does **not** discover the migration and startup `MigrateAsync` will skip it (silent miss). Also updates `Migrations/DataContextModelSnapshot.cs`.
   - **Add a migration:**
     ```bash
     cd backend
     # Load MYSQL_* (and friends) from .env.dev into the shell if DesignTime needs them
     dotnet ef migrations add <Name> --output-dir Migrations
     ```
   - Design-time factory: [`backend/Infrastructure/Data/DesignTimeDbContextFactory.cs`](backend/Infrastructure/Data/DesignTimeDbContextFactory.cs) (reads `MYSQL_*` env).
   - **Apply:** API applies pending migrations on startup, or `dotnet ef database update` with the same env.
   - **Never** edit already-applied migrations. **Never** invent a migration `.cs` by hand (missing Designer / `[Migration]` has bitten us more than once).
   - If the API process locks `Delegat.dll`, stop it before `dotnet ef` / rebuild.

3. **CHANGELOG** — user-visible / ops / security changes update `## [Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md). Process: [`docs/platform/releasing.md`](docs/platform/releasing.md). Canonical version is root [`VERSION`](VERSION).

4. **Public API sync** — partner-facing behavior changes → update `backend/Controllers/PublicApi/` **and** [`docs/api/public-v1.md`](docs/api/public-v1.md) (+ Swagger/XML) in the same change. Prefer additive `/api/v1`.

5. **No vibecode artifacts in git** — do not commit `.cursor/`, `.claude/`, `AGENTS.md`, `CLAUDE.md`, `.mcp.json`, skills caches, etc. (see `.gitignore`). Local Cursor rules stay local. Product guidance for agents that must be shared → **this MEMORY.md** and/or `docs/`.

6. **Env** — Dev: `.env.dev` · Prod: `.env` · Template: `.env.example`. No secrets in git.

7. **Icons** — Hugeicons (same family as Control Plane). Keep Lucide-compatible import names; Vite aliases `lucide-vue-next` → [`frontend/src/lib/icons.js`](frontend/src/lib/icons.js). Rebuild map with `node frontend/scripts/build-icons.mjs`. Free pack is stroke-rounded; wrappers use denser stroke for a filled look (except **loaders**: Control Plane `Loading03` / `Spinner`, default stroke). True solid needs Hugeicons Pro.

8. **Radius** — `--radius: 1rem` (aligned with Control Plane DESIGN).

9. **Loaders** — only [`Spinner`](frontend/src/components/ui/spinner) (`Loading03`). Prefer `size` prop over ad-hoc `size-*` classes:

| `size` | px | Use |
|--------|----|-----|
| `xs` | 12 | Dense chips, nested AI/ops status |
| `sm` | 14 | Compact toolbars / dense controls |
| `default` | 16 | Buttons, menus, form actions |
| `lg` | 20 | Panel / list placeholders |
| `xl` | 24 | Overlays / mid-block waiting |
| `2xl` | 32 | EmptyState / full-region loading (products table, PageSection, storage, etc.) |

Layout extras (`mr-2`, `text-muted-foreground`) go in `class`. Do not use raw `Loader2` for loaders (exception: dynamic status *icon* components that must spin as a Lucide-compatible export).

**Page vs panel:** any loader that replaces a full list/table/page body uses `EmptyState loading` or `Spinner size="2xl"` — never `lg`/`default` for those. Overlays → `xl`; side panels / sheet chrome → `lg`; buttons/menus → default/`sm`/`xs`.

10. **Menus** — every `DropdownMenuItem` / `ContextMenuItem` (and submenu triggers) should have a leading Hugeicons icon when it is an action. Data-picker lists (template fields, long option catalogs) may omit per-row icons. Destructive actions use `variant="destructive"` (same as Sign out), not ad-hoc `class="text-destructive"`. Icon-only delete/revoke/unlink triggers use `text-destructive hover:bg-destructive/10 hover:text-destructive`. Confirm buttons use `AlertDialogAction variant="destructive"`.

11. **Checkboxes** — only shadcn `Checkbox` (`rounded-xs`). Never native `<input type="checkbox">` or hand-drawn `div` boxes (MultiSelect historically used `rounded-sm`, which looked nearly round at `--radius` 1rem).

12. **Dialog chrome** — one title scale and two header/footer layouts only:
    - **Title:** `DialogTitle` / `AlertDialogTitle` / `SheetTitle` = `text-base font-semibold tracking-tight leading-none`. Text only — no decorative leading icons in the header.
    - **Default:** `DialogHeader` / `DialogFooter` inside padded `DialogContent` (`p-6`) — no local `px/py/border` overrides.
    - **Bar (`variant="bar"`):** for `DialogContent` with `p-0` / `gap-0` (scroll / tall editors). Header = `border-b px-6 py-4`; footer = `border-t px-6 py-4`. Do not re-specify those classes at call sites; only add layout extras (`sm:justify-between`, `gap-3` for step rows).
    - Description stays `text-sm text-muted-foreground` from the primitive.

13. **Forms** — every labeled field stack uses shadcn `FieldGroup` + `Field` (prefer [`SettingsFormField`](frontend/src/components/settings/SettingsFormField.vue) which wraps Field/Label/Description/Error). Validated fields: vee-validate `FormField` wrapping that Field stack. Do **not** use `FormItem`/`FormLabel` alone without Field, and do **not** invent raw `Label` + `Input`/`div.space-y-*` form stacks. `SettingsFormSection` already provides `FieldSet` + `FieldGroup`. Exception: dense mapping inspector / modifier param chrome may stay compact; still prefer Field primitives when adding new controls there.

14. **Toasts** — mount [`Toaster`](frontend/src/components/ui/sonner/Sonner.vue) from `@/components/ui/sonner` only (not raw `vue-sonner`). Flat popover surface + border, compact `--radius-md`, no soft shadow; description = muted-foreground. Keep `richColors` off unless a screen needs muted semantic fills.

---

- Theme no longer jumps from light Setup to dark Products: color scheme boots once (`theme.js`, light default) instead of applying system dark only when the account menu mounts.
- Products table shows loading from first paint (was empty while categories/attributes loaded before the products request).

- **Runtime** branding: Settings → Branding modal and first-run Setup → Branding step (name, description, logo, favicon, primary/accent).
- Singleton table `instanceBrandingSettings`; API under `/api/settings/branding` (GET is anonymous).
- **Defaults:** no logo (name-only until upload); favicon = Control Plane square `/logo-square-favicon.webp` (copied into `frontend/public`).
- Legacy static paths `/logo@0,25x.webp` and `/logo_sm@0,15x.webp` resolve as “no logo” / CP favicon.
- **Seed once** from env: `BRAND_NAME`, `BRAND_DESCRIPTION`, `BRAND_LOGO_URL`, `BRAND_FAVICON_URL`, `BRAND_PRIMARY`, `BRAND_ACCENT`. After first boot, admin UI is source of truth.
- Frontend: [`frontend/src/brand.js`](frontend/src/brand.js); shared form [`BrandingForm.vue`](frontend/src/components/settings/BrandingForm.vue).
- CP may inject empty/filled `BRAND_*` at provision (`BuildEnvFile`); no CP Ops brand UI yet.

---

## Stack pointers

| Area | Where |
|------|--------|
| Backend | ASP.NET Core 8, EF Core + Pomelo MySQL, MinIO |
| Frontend | Vue 3 + Vite + shadcn-vue (reka-ui); icons = **Hugeicons** via [`frontend/src/lib/icons.js`](frontend/src/lib/icons.js) (Lucide import names aliased in Vite) |
| Migrations | `backend/Migrations/` — Baseline squash `20260731183033_Baseline` then additive |
| Public API | `/api/v1`, docs in `docs/api/public-v1.md` |
| Settings toggles | Logging / Public API / Branding singletons + services |

---

## Sibling CP MEMORY

Control Plane living log: `C:\Users\ARTEM\projects\delegat-module-platform\MEMORY.md` (EF there uses `pnpm ef:add` / `pnpm ef:update`).
