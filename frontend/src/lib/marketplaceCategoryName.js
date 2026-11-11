/** Read a marketplace breadcrumb's leaf without splitting names such as AC/DC. */
export function marketplaceCategoryLeaf(name) {
  return (
    String(name || "")
      .trim()
      .split(/\s+\/\s+/)
      .filter(Boolean)
      .at(-1)
      ?.trim() || ""
  );
}
