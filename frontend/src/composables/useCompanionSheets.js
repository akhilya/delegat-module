import { computed } from "vue";
import { useMediaQuery } from "@vueuse/core";
import { useStore } from "vuex";
import {
  SHEET_WIDTH_DEFAULTS,
  SHEET_WIDTH_STORAGE,
  readStoredSheetWidth,
} from "@/composables/useSheetResize";
import {
  COMPANION_STACK_MIN_WIDTH,
  companionEdgeOffset,
  companionToastRight,
} from "@/lib/companionSheets";

export function companionSnapshotFromState(state) {
  return {
    operations: {
      open: !!state.operations?.panelOpen,
      width:
        Number(state.operations?.panelWidth) ||
        readStoredSheetWidth(
          SHEET_WIDTH_STORAGE.operations,
          SHEET_WIDTH_DEFAULTS.operations,
        ),
    },
    docs: {
      open: !!state.docs?.panelOpen,
      width:
        Number(state.docs?.panelWidth) ||
        readStoredSheetWidth(
          SHEET_WIDTH_STORAGE.docs,
          SHEET_WIDTH_DEFAULTS.docs,
        ),
    },
    ai: {
      open: !!state.aiChat?.isOpen,
      width:
        Number(state.aiChat?.panelWidth) ||
        readStoredSheetWidth(SHEET_WIDTH_STORAGE.ai, SHEET_WIDTH_DEFAULTS.ai),
    },
  };
}

export function useCompanionSheets() {
  const store = useStore();
  const snapshot = computed(() => companionSnapshotFromState(store.state));
  const stack = useMediaQuery(`(min-width: ${COMPANION_STACK_MIN_WIDTH}px)`);
  const layout = computed(() => ({ stack: stack.value }));

  return {
    snapshot,
    stack,
    toastRight: computed(() =>
      companionToastRight(snapshot.value, layout.value),
    ),
    edgeOffset(id) {
      return computed(() =>
        companionEdgeOffset(id, snapshot.value, layout.value),
      );
    },
  };
}
