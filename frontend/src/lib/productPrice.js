/** @returns {number|null} Discount percent 0-99 from old (base) and current selling price */
export function computeDiscountPercent(oldPrice, sellingPrice) {
  if (oldPrice == null || sellingPrice == null || oldPrice <= 0) return null;
  const pct = Math.round((1 - sellingPrice / oldPrice) * 100);
  return Math.min(99, Math.max(0, pct));
}