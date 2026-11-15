// Auto-matching for dictionary option mappings (optionMap editor): pairs each
// source option with the best target option by normalized name. Three passes,
// each only accepting unambiguous results so a bad guess is worse than none:
// exact normalized equality, then containment, then a short Levenshtein hop.

const norm = (v) =>
  String(v ?? "")
    .toLowerCase()
    .replace(/ё/g, "е")
    .replace(/[^a-zа-я0-9]+/g, " ")
    .trim()
    .replace(/\s+/g, " ");

const lev = (a, b) => {
  const n = b.length;
  let prev = Array.from({ length: n + 1 }, (_, i) => i);
  for (let i = 1; i <= a.length; i++) {
    const cur = [i];
    for (let j = 1; j <= n; j++) {
      cur[j] = Math.min(
        prev[j] + 1,
        cur[j - 1] + 1,
        prev[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1),
      );
    }
    prev = cur;
  }
  return prev[n];
};

// Match by label first; the raw value is dictionary-local (id "1" can mean
// "Red" in one dictionary and "Blue" in another), so it only counts when the
// option has no label of its own.
const keys = (o) => {
  const k = norm(o?.label) || norm(o?.value);
  return k ? [k] : [];
};

function bestMatch(source, targets) {
  const sKeys = keys(source);
  if (!sKeys.length) return null;

  // Pass 1: exact normalized label/value equality.
  const exact = targets.filter((t) => {
    const tKeys = keys(t);
    return sKeys.some((s) => tKeys.includes(s));
  });
  if (exact.length === 1) return exact[0];
  if (exact.length > 1) {
    // Prefer the shortest candidate on ties ("Красный" over "Красный металлик").
    exact.sort((a, b) => String(a.label).length - String(b.label).length);
    return exact[0];
  }

  // Pass 2: one normalized key contains the other (needs 3+ chars).
  const contained = targets.filter((t) => {
    const tKeys = keys(t);
    return sKeys.some(
      (s) =>
        s.length >= 3 &&
        tKeys.some((k) => k.length >= 3 && (k.includes(s) || s.includes(k))),
    );
  });
  if (contained.length === 1) return contained[0];

  // Pass 3: fuzzy - unique best Levenshtein within a conservative bound.
  // Keys shorter than 3 chars are skipped: numeric/value ids would tie on
  // single-letter edits and kill the uniqueness check.
  const fuzzy = (o) => keys(o).filter((k) => k.length >= 3);
  const sFuzzy = fuzzy(source);
  let best = null;
  let bestD = Infinity;
  let secondD = Infinity;
  for (const t of targets) {
    const d = Math.min(
      ...sFuzzy.flatMap((s) => fuzzy(t).map((k) => lev(s, k))),
    );
    if (d < bestD) {
      secondD = bestD;
      bestD = d;
      best = t;
    } else if (d < secondD) {
      secondD = d;
    }
  }
  const limit = Math.max(
    1,
    Math.floor(Math.min(...sFuzzy.map((s) => s.length)) / 4),
  );
  return best && bestD <= limit && secondD > bestD ? best : null;
}

/**
 * @param {Array} sourceOptions - {value,label} options of the source attribute.
 * @param {Array} targetOptions - {value,label} options of the adapter field.
 * @param {Set<string>} [skipFrom] - source values already mapped (kept as-is).
 * @returns {Array<{from:string,to:string}>} new mapping rows to append.
 */
export function autoMapOptions(sourceOptions, targetOptions, skipFrom) {
  const rows = [];
  if (!sourceOptions?.length || !targetOptions?.length) return rows;
  const used = new Set([...(skipFrom || [])].map(String));
  for (const s of sourceOptions) {
    const from = String(s.value);
    if (used.has(from)) continue;
    const hit = bestMatch(s, targetOptions);
    if (hit) {
      rows.push({ from, to: String(hit.value) });
      used.add(from);
    }
  }
  return rows;
}
