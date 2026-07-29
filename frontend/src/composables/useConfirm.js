import { reactive } from "vue";
import { t } from "@/i18n";

export const confirmState = reactive({
  open: false,
  title: "",
  description: "",
  confirmLabel: "",
  cancelLabel: "",
  destructive: true,
  _resolve: null,
});

/**
 * Programmatic confirmation dialog (ConfirmDialogHost in app.vue).
 * @returns {Promise<boolean>}
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
      prev(false);
    }

    // Defer open past the triggering pointer/click (e.g. sidebar RouterLink).
    // Opening synchronously inside onBeforeRouteLeave lets DismissableLayer treat
    // that same click as "outside" and immediately emit update:open(false) —
    // dialog never appears, Promise hangs, navigation stays blocked.
    setTimeout(() => {
      Object.assign(confirmState, {
        open: true,
        title: opts.title ?? t("common.confirmAction"),
        description: opts.description ?? "",
        confirmLabel: opts.confirmLabel ?? t("common.delete"),
        cancelLabel: opts.cancelLabel ?? t("common.cancel"),
        destructive: opts.destructive !== false,
        _resolve: resolve,
      });
    }, 0);
  });
}

export function resolveConfirm(accepted) {
  if (!confirmState._resolve) return;
  const resolve = confirmState._resolve;
  confirmState._resolve = null;
  confirmState.open = false;
  resolve(accepted);
}
