/** Suggest an automatic reuse only when the best match is clearly ahead. */
export function defaultImportAttributeDecisions(fields) {
  return Object.fromEntries(
    fields.flatMap((field) => {
      const candidates = field.isBaseField
        ? []
        : field.potentialDuplicates || [];
      const [best, next] = candidates;
      if (!best) return [];
      if (
        next &&
        (!Number.isFinite(best.score) ||
          !Number.isFinite(next.score) ||
          best.score - next.score < 5)
      )
        return [];
      return [[field.key, { action: "reuse", existingAttributeId: best.id }]];
    }),
  );
}
