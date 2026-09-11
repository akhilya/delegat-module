/** Adapter maturity. Stable is the default and is not labelled. Beta maps to experimental. */
export const ADAPTER_STAGES = {
  stable: "stable",
  experimental: "experimental",
};

export function adapterStage(adapterOrStability) {
  const raw =
    typeof adapterOrStability === "string"
      ? adapterOrStability
      : adapterOrStability?.stability ??
        adapterOrStability?.Stability ??
        "";
  const stage = String(raw).trim().toLowerCase();
  if (stage === ADAPTER_STAGES.stable) return ADAPTER_STAGES.stable;
  if (
    stage === ADAPTER_STAGES.experimental ||
    stage === "beta" ||
    stage === "deprecated"
  ) {
    return ADAPTER_STAGES.experimental;
  }
  return ADAPTER_STAGES.stable;
}

export function isExperimentalAdapter(adapterOrStability) {
  return adapterStage(adapterOrStability) === ADAPTER_STAGES.experimental;
}

export function adapterStageLabelKey(adapterOrStability) {
  return isExperimentalAdapter(adapterOrStability)
    ? "common.stageExperimental"
    : "";
}

export function findAdapterByKey(adapters, adapterKey) {
  const key = adapterKey ?? "";
  return (adapters || []).find((item) => (item.key ?? item.Key) === key) || null;
}

export function isExperimentalConnection(connection, adapters) {
  const key = connection?.adapterKey ?? connection?.AdapterKey;
  return isExperimentalAdapter(findAdapterByKey(adapters, key));
}
