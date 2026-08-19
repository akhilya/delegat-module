/**
 * Shared Apple menu / picker chrome. Floating lists (dropdown, context,
 * select, popover, combobox) use one surface so call sites stay consistent.
 */
export const MENU_SURFACE =
  "cn-menu-translucent bg-popover text-popover-foreground rounded-lg ring-1 ring-foreground/5 shadow-lg dark:ring-foreground/10 duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]";

export const MENU_ANIMATION =
  "data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2";

export const MENU_ITEM =
  "relative flex cursor-default items-center gap-2.5 rounded-md px-3 py-2 text-sm outline-hidden select-none " +
  "hover:bg-muted/80 hover:text-foreground focus:bg-muted/80 focus:text-foreground " +
  "data-[highlighted]:bg-muted/80 data-[highlighted]:text-foreground " +
  "data-[state=open]:bg-muted/80 data-[state=open]:text-foreground " +
  "data-[disabled]:pointer-events-none data-[disabled]:opacity-50 " +
  "data-[inset]:pl-8 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4 [&_svg:not([class*='text-'])]:text-muted-foreground " +
  "data-[variant=destructive]:text-destructive data-[variant=destructive]:hover:bg-destructive/10 data-[variant=destructive]:focus:bg-destructive/10 data-[variant=destructive]:focus:text-destructive dark:data-[variant=destructive]:focus:bg-destructive/20 data-[variant=destructive]:*:[svg]:!text-destructive";

/** Exclusive / toggle rows: same as MENU_ITEM, room for a trailing check. */
export const MENU_ITEM_INDICATOR = `${MENU_ITEM} pr-8`;

/** Apple picker check — trailing, vertically centered. */
export const MENU_ITEM_CHECK =
  "pointer-events-none absolute top-1/2 right-2 flex size-4 -translate-y-1/2 items-center justify-center";

export const MENU_LABEL =
  "px-3 py-1.5 text-xs font-medium text-muted-foreground data-[inset]:pl-8";

export const MENU_SEPARATOR = "bg-border/50 -mx-1 my-1 h-px";
