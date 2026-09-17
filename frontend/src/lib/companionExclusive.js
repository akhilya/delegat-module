import {
  companionCanStack,
  companionKeepWhenCollapsing,
} from "./companionSheets.js";

function viewportWidth() {
  if (typeof window === "undefined") return Number.POSITIVE_INFINITY;
  return window.innerWidth;
}

export function applyCompanionExclusive(store, keep) {
  if (keep !== "ai" && store.state.aiChat?.isOpen) {
    store.commit("aiChat/setOpen", false);
  }
  if (keep !== "operations" && store.state.operations?.panelOpen) {
    store.commit("operations/SET_PANEL_OPEN", false);
  }
  if (keep !== "docs" && store.state.docs?.panelOpen) {
    store.commit("docs/SET_PANEL_OPEN", false);
  }
}

function keepFromMutation(type, payload, state) {
  if (type === "aiChat/setOpen" && payload) return "ai";
  if (type === "aiChat/toggle" && state.aiChat?.isOpen) return "ai";
  if (type === "operations/SET_PANEL_OPEN" && payload) return "operations";
  if (type === "docs/SET_PANEL_OPEN" && payload) return "docs";
  return null;
}

function collapseOpenCompanions(store) {
  const keep = companionKeepWhenCollapsing({
    operations: { open: !!store.state.operations?.panelOpen },
    docs: { open: !!store.state.docs?.panelOpen },
    ai: { open: !!store.state.aiChat?.isOpen },
  });
  if (keep) applyCompanionExclusive(store, keep);
}

/**
 * Below `lg`, only one companion sheet stays open. Opening another closes the
 * rest; shrinking the window keeps the rightmost panel.
 */
export function companionExclusivePlugin(store) {
  let silencing = false;

  function collapse(keep) {
    silencing = true;
    try {
      applyCompanionExclusive(store, keep);
    } finally {
      silencing = false;
    }
  }

  store.subscribe((mutation) => {
    if (silencing) return;
    if (companionCanStack(viewportWidth())) return;
    const keep = keepFromMutation(
      mutation.type,
      mutation.payload,
      store.state,
    );
    if (keep) collapse(keep);
  });

  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return;
  }
  const mq = window.matchMedia(`(min-width: 1024px)`);
  const onChange = () => {
    if (mq.matches || silencing) return;
    silencing = true;
    try {
      collapseOpenCompanions(store);
    } finally {
      silencing = false;
    }
  };
  mq.addEventListener("change", onChange);
}
