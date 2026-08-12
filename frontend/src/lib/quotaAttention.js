/**
 * Shared "quota overage needs attention" flag for the app sidebar.
 * True when plan usage is over quota (trim / auto-purge will delete excess).
 */
import { reactive } from "vue";

export const QUOTA_ATTENTION_REFRESH_EVENT = "quota-attention-refresh";

export const quotaAttentionState = reactive({
  /** @type {boolean} */
  isOverQuota: false,
  loaded: false,
});

/**
 * @param {object|null|undefined} snapshot QuotaUsageSnapshotDto-like payload
 */
export function applyQuotaSnapshot(snapshot) {
  quotaAttentionState.isOverQuota = snapshot?.isOverQuota === true;
  quotaAttentionState.loaded = true;
}

export function clearQuotaAttention() {
  quotaAttentionState.isOverQuota = false;
  quotaAttentionState.loaded = false;
}

export function notifyQuotaAttentionRefresh() {
  if (typeof window === "undefined") return;
  window.dispatchEvent(new CustomEvent(QUOTA_ATTENTION_REFRESH_EVENT));
}
