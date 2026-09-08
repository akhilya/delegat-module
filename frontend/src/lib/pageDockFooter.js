/**
 * Pin the catalog/mailbox footer to the inset bottom only when the page
 * actually overflows. Short tables keep the footer in flow under the last
 * row — leftover space stays *below* the footer (the page is min-h-full).
 *
 * When the footer is in flow, leftoverBelowFooter is the scroller's bottom
 * minus the footer's bottom. A positive value means there is visible space
 * under the footer; do not pin even if min-h-full makes scrollHeight ≈ clientHeight.
 * When the footer is already in the dock, leftover is ignored and overflow
 * of the scroller alone decides.
 */
export function shouldPinPageFooter({
  scrollHeight,
  clientHeight,
  leftoverBelowFooter = 0,
  pinned = false,
}) {
  if (!pinned && leftoverBelowFooter > 1) return false;
  return scrollHeight > clientHeight + 1;
}
