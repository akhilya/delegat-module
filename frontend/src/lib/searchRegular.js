/**
 * MingCute Core Regular search (Apache-2.0) — hollow lens.
 * Inlined so Vite does not prebundle the whole core-regular package.
 */
import { defineComponent, h } from "vue";

export const SearchRegular = defineComponent({
  name: "SearchRegular",
  inheritAttrs: false,
  setup(_, { attrs }) {
    return () =>
      h(
        "svg",
        {
          xmlns: "http://www.w3.org/2000/svg",
          viewBox: "0 0 24 24",
          fill: "none",
          ...attrs,
        },
        [
          h("path", {
            fill: "none",
            stroke: "currentColor",
            "stroke-linecap": "round",
            "stroke-width": "2",
            d: "m15.879 15.879 4.242 4.242M18 10.5a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0Z",
          }),
        ],
      );
  },
});
