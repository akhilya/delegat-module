/** TOC rail is 15rem; below this the article is squeezed. */
export const DOCS_SHEET_RAIL_MIN_WIDTH = 720;

/** Side catalog when the sheet is wide enough; otherwise a header menu. */
export function docsSheetUsesRail(width) {
  return Number(width) >= DOCS_SHEET_RAIL_MIN_WIDTH;
}
