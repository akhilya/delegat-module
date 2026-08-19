import { reactive } from "vue";
import { t } from "@/i18n";

export const confirmState = reactive({
  open: false,
  /** True from `confirm()` until resolve — including the deferred open tick. */
  pending: false,
  title: "",
  description: "",
  confirmLabel: "",
  cancelLabel: "",
  saveLabel: "",
  destructive: true,
  _resolve: null,
});

/** Dialog/sheet DismissableLayer must not close while a confirm is opening or open. */
export function isConfirmOverlayActive() {
  return Boolean(confirmState.pending || confirmState.open);
}

/**
 * Programmatic confirmation dialog (ConfirmDialogHost in app.vue).
 * @returns {Promise<boolean | 'save'>} true = confirm, false = cancel, 'save' = saveLabel
 */
export function confirm(options) {
  const opts =
    typeof options === "string" ? { title: options } : (options ?? {});

  return new Promise((resolve) => {
    // If a previous confirm is still pending, cancel it first.
    if (confirmState._resolve) {
      const prev = confirmState._resolve;
      confirmState._resolve = null;
      confirmState.open = false;
      confirmState.pending = false;
      prev(false);
    }

    // Hold parent dialogs closed *before* the deferred open. Opening the
    // AlertDialog on the next tick lets DismissableLayer treat that focus move
    // as "outside" and dismiss the invite (or any) Dialog — delete looks dead.
    confirmState.pending = true;

    // Defer open past the triggering pointer/click (e.g. sidebar RouterLink).
    // Opening synchronously inside onBeforeRouteLeave lets DismissableLayer treat
    // that same click as "outside" and immediately emit update:open(false) —
    // dialog never appears, Promise hangs, navigation stays blocked.
    setTimeout(() => {
      if (!confirmState.pending) {
        resolve(false);
        return;
      }
      Object.assign(confirmState, {
        open: true,
        title: opts.title ?? t("common.confirmAction"),
        description: opts.description ?? "",
        confirmLabel: opts.confirmLabel ?? t("common.delete"),
        cancelLabel: opts.cancelLabel ?? t("common.cancel"),
        saveLabel: opts.saveLabel ?? "",
        destructive: opts.destructive !== false,
        _resolve: resolve,
      });
    }, 0);
  });
}

export function resolveConfirm(accepted) {
  confirmState.pending = false;
  if (!confirmState._resolve) {
    confirmState.open = false;
    return;
  }
  const resolve = confirmState._resolve;
  confirmState._resolve = null;
  confirmState.open = false;
  resolve(accepted);
}
