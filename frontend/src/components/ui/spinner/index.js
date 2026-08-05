import { cva } from "class-variance-authority";

export { default as Spinner } from "./Spinner.vue";

/**
 * Formal loader size scale — prefer `size` prop over ad-hoc `size-*` classes.
 * Do not pass competing `size-*` utilities via `class`.
 */
export const spinnerVariants = cva("shrink-0 animate-spin", {
  variants: {
    size: {
      xs: "size-3",
      sm: "size-3.5",
      default: "size-4",
      lg: "size-5",
      xl: "size-6",
      "2xl": "size-8",
    },
  },
  defaultVariants: {
    size: "default",
  },
});
