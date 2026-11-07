import { clsx } from "clsx";
import { extendTailwindMerge } from "tailwind-merge";

const twMerge = extendTailwindMerge({
  extend: { theme: { radius: ["control"] } },
});

export function cn(...inputs) {
  return twMerge(clsx(inputs));
}
