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
    Object.assign(confirmState, {
      open: true,
      title: opts.title ?? t("common.confirmAction"),
      description: opts.description ?? "",
      confirmLabel: opts.confirmLabel ?? t("common.delete"),
      cancelLabel: opts.cancelLabel ?? t("common.cancel"),
      destructive: opts.destructive !== false,
      _resolve: resolve,
    });
  });
}

export function resolveConfirm(accepted) {
  if (!confirmState._resolve) return;
  const resolve = confirmState._resolve;
  confirmState._resolve = null;
  confirmState.open = false;
  resolve(accepted);
}
