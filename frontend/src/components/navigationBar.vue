<script>
// Page-level toolbar: available panel width decides whether search stacks.
import { ref } from "vue";
import { SidebarTrigger } from "@/components/ui/sidebar";
import FabGroup from "@/components/FabGroup.vue";
import { useResizeObserver } from "@vueuse/core";

import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";

export default {
  emits: ["resize"],
  setup(props, { emit }) {
    const navbarElement = ref(null);
    useResizeObserver(navbarElement, (entries) => {
      const entry = entries[0];
      if (entry) emit("resize", entry.target.getBoundingClientRect().height);
    });
    return { navbarElement };
  },
  props: {
    /** When true, render the center slot (search/tabs). Do not infer from $slots alone. */
    hasCenter: { type: Boolean, default: false },
  },
  components: {
    SidebarTrigger,
    FabGroup,
  },
  data() {
    return {
      me: undefined,
    };
  },
  methods: {
    ...mapActions(["getMe"]),
  },
  async mounted() {
    await this.getMe({
      onSuccess: (response) => {
        this.me = response.data.value;
      },
      onError: (error) => {
        displaySonnerError(error);
      },
    });
  },
};
</script>

<template>
  <div
    ref="navbarElement"
    data-slot="app-navbar"
    class="app-navbar-shell min-h-[var(--app-navbar-row)] min-w-0 w-full shrink-0 bg-background"
  >
    <div class="app-navbar-grid" :class="{ 'has-center': hasCenter }">
      <div
        data-slot="navbar-title"
        class="app-navbar-title flex min-w-0 items-center gap-1.5"
      >
        <div class="md:hidden shrink-0">
          <SidebarTrigger />
        </div>
        <div v-if="$slots.left" class="min-w-0 flex-1">
          <slot name="left" />
        </div>
      </div>

      <div
        v-show="hasCenter"
        data-slot="navbar-tools"
        class="app-navbar-tools min-w-0 w-full"
      >
        <slot name="center" />
      </div>

      <div
        data-slot="navbar-actions"
        class="app-navbar-actions flex shrink-0 items-center justify-end gap-0.5"
      >
        <div
          v-if="$slots.right"
          class="mr-1 hidden shrink-0 items-center gap-2 md:flex"
        >
          <slot name="right" />
        </div>
        <FabGroup :ai-enabled="me?.aiEnabled === true" />
      </div>
    </div>
    <div
      v-if="$slots.tabs"
      data-slot="navbar-tabs"
      class="min-w-0 overflow-x-auto pb-2.5"
    >
      <div class="flex w-max min-w-full items-center justify-center gap-2">
        <slot name="tabs" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.app-navbar-shell {
  container-type: inline-size;
}

.app-navbar-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  column-gap: 0.75rem;
}

.app-navbar-title {
  grid-column: 1;
  grid-row: 1;
  min-height: var(--app-navbar-row);
}

.app-navbar-actions {
  grid-column: 2;
  grid-row: 1;
  min-height: var(--app-navbar-row);
}

.app-navbar-tools {
  display: flex;
  justify-content: safe center;
  grid-column: 1 / -1;
  grid-row: 2;
  padding-bottom: 0.625rem;
}

.app-navbar-tools :deep([data-slot="tabs"]) {
  min-width: 0;
  max-width: 100%;
  overflow-x: auto;
}

.app-navbar-tools :deep([data-slot="page-list-toolbar"]) {
  margin-inline: auto;
}

/* Available panel width, not viewport width: a resized sidebar also matters. */
@container (min-width: 64rem) {
  .app-navbar-grid.has-center {
    grid-template-columns: minmax(0, 1fr) minmax(0, min(40rem, 50%)) minmax(
        0,
        1fr
      );
  }

  .has-center .app-navbar-tools {
    grid-column: 2;
    grid-row: 1;
    padding-bottom: 0;
  }

  .has-center .app-navbar-actions {
    grid-column: 3;
    justify-self: end;
  }
}
</style>
