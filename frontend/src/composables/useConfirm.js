import { reactive } from "vue";

export const confirmState = reactive({
  open: false,
  title: "",
  description: "",
  confirmLabel: "Удалить",
  cancelLabel: "Отмена",
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
      title: opts.title ?? "Подтвердите действие",
      description: opts.description ?? "",
      confirmLabel: opts.confirmLabel ?? "Удалить",
      cancelLabel: opts.cancelLabel ?? "Отмена",
      destructive: opts.destructive !== false,
      _resolve: resolve,
    });
  });
}

export function resolveConfirm(accepted) {
  confirmState._resolve?.(accepted);
  confirmState.open = false;
  confirmState._resolve = null;
}