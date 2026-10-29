export function radioGroupNextIndex(itemCount, currentIndex, key) {
  if (itemCount < 1) return null;
  if (key === "Home") return 0;
  if (key === "End") return itemCount - 1;

  const direction =
    key === "ArrowRight" || key === "ArrowDown"
      ? 1
      : key === "ArrowLeft" || key === "ArrowUp"
        ? -1
        : 0;
  if (!direction) return null;

  const index =
    currentIndex >= 0 && currentIndex < itemCount ? currentIndex : 0;
  return (index + direction + itemCount) % itemCount;
}

export function handleRadioGroupKeydown(event) {
  if (event.key === " " || event.key === "Enter") {
    event.preventDefault();
    event.currentTarget.click();
    return;
  }

  const group = event.currentTarget.closest('[role="radiogroup"]');
  if (!group) return;

  const items = Array.from(
    group.querySelectorAll(
      '[role="radio"]:not([aria-disabled="true"]):not(:disabled)',
    ),
  );
  const currentIndex = items.indexOf(event.currentTarget);
  const nextIndex = radioGroupNextIndex(items.length, currentIndex, event.key);
  if (nextIndex == null) return;

  event.preventDefault();
  const nextItem = items[nextIndex];
  nextItem.focus();
  nextItem.click();
}
