/**
 * Control Plane–aligned Hugeicons wrappers (loaders and shared names).
 * Prefer lucide-compatible names from `@/lib/icons` in app code.
 */
import { defineComponent, h } from "vue";
import { HugeiconsIcon } from "@hugeicons/vue";
import { Loading03Icon as Loading03Data } from "@hugeicons/core-free-icons";

function createHugeIcon(icon, name) {
  return defineComponent({
    name,
    inheritAttrs: true,
    setup(_, { attrs }) {
      return () => h(HugeiconsIcon, { icon, ...attrs });
    },
  });
}

/** Same spinner glyph as Control Plane `Spinner`. */
export const Loading03Icon = createHugeIcon(Loading03Data, "Loading03Icon");
