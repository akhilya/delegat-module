/**
 * Align category attributes from several groups (marketplace columns) into rows
 * of semantically similar fields (Цвет / Цвет товара / color).
 */

const MATCH_THRESHOLD = 0.7;

const FILLER = new Set([
  "товара",
  "товар",
  "товаров",
  "предмета",
  "предмет",
  "изделия",
  "изделие",
  "характеристики",
  "характеристика",
  "значение",
  "product",
  "item",
  "value",
  "the",
  "of",
  "a",
  "an",
  "in",
  "on",
  "for",
  "from",
  "with",
  "\u0432",
  "\u043d\u0430",
  "\u0434\u043b\u044f",
  "\u0438\u0437",
  "\u0441",
  "\u043e\u0442",
  "\u043f\u043e",
]);

/** Canonical concept for a token (Cyrillic and English marketplace labels). */
const SYNONYMS = {
  "\u0446\u0432\u0435\u0442": "color",
  color: "color",
  colour: "color",
  "\u0432\u044b\u0441\u043e\u0442\u0430": "height",
  height: "height",
  "\u0448\u0438\u0440\u0438\u043d\u0430": "width",
  width: "width",
  "\u0432\u0435\u0441": "weight",
  weight: "weight",
  "\u0431\u0440\u0435\u043d\u0434": "brand",
  brand: "brand",
  "\u043c\u0430\u0442\u0435\u0440\u0438\u0430\u043b": "material",
  material: "material",
  "\u0434\u043b\u0438\u043d\u0430": "length",
  length: "length",
  "\u0433\u043b\u0443\u0431\u0438\u043d\u0430": "depth",
  depth: "depth",
};

export function normalizeAttributeName(name) {
  return String(name ?? "")
    .toLowerCase()
    .replace(/\u0451/g, "\u0435")
    .replace(/[^\p{L}\p{N}\s]+/gu, " ")
    .split(/\s+/)
    .filter(Boolean)
    .filter((token) => !FILLER.has(token))
    .map((token) => SYNONYMS[token] ?? token)
    .join(" ");
}

function typeKey(type) {
  if (type == null || type === "") return "";
  const s = String(type).toLowerCase();
  if (s === "0" || s === "text") return "text";
  if (s === "1" || s === "number") return "number";
  if (s === "2" || s === "boolean") return "boolean";
  if (s === "3" || s === "select") return "select";
  if (s === "4" || s === "multiselect") return "multiselect";
  if (s === "5" || s === "barcode") return "barcode";
  return s;
}

function sameType(a, b) {
  const ka = typeKey(a?.type);
  const kb = typeKey(b?.type);
  if (!ka || !kb) return true;
  return ka === kb;
}

export function scoreAttributePair(a, b) {
  const na = normalizeAttributeName(a?.name);
  const nb = normalizeAttributeName(b?.name);
  if (!na || !nb) return 0;
  if (na === nb) return 1;

  if (na.includes(nb) || nb.includes(na)) {
    return sameType(a, b) ? 0.92 : 0.78;
  }

  const ta = new Set(na.split(/\s+/).filter(Boolean));
  const tb = new Set(nb.split(/\s+/).filter(Boolean));
  let inter = 0;
  for (const token of ta) {
    if (tb.has(token)) inter += 1;
  }
  const union = ta.size + tb.size - inter;
  let score = union === 0 ? 0 : inter / union;
  if (sameType(a, b) && score >= 0.4) score = Math.min(1, score + 0.1);
  else if (!sameType(a, b)) score = Math.max(0, score - 0.15);
  return score;
}

function sortKey(attr) {
  const order = Number(attr?.sortOrder);
  const id = Number(attr?.id);
  return [Number.isFinite(order) ? order : 0, Number.isFinite(id) ? id : 0];
}

function pickSeed(unused) {
  let best = null;
  let bestIndex = 0;
  let bestOrd = Infinity;
  let bestId = Infinity;
  for (let i = 0; i < unused.length; i++) {
    for (const attr of unused[i]) {
      const [order, id] = sortKey(attr);
      if (order < bestOrd || (order === bestOrd && id < bestId)) {
        best = attr;
        bestIndex = i;
        bestOrd = order;
        bestId = id;
      }
    }
  }
  return { groupIndex: bestIndex, attr: best };
}

const CLOSE_SCORE = 0.15;

function bestMatch(seed, candidates) {
  let best = null;
  let bestScore = -1;
  for (const cand of candidates) {
    const score = scoreAttributePair(seed, cand);
    if (score < MATCH_THRESHOLD) continue;
    if (!best) {
      best = cand;
      bestScore = score;
      continue;
    }
    const close = Math.abs(score - bestScore) <= CLOSE_SCORE;
    const candSame = sameType(seed, cand);
    const bestSame = sameType(seed, best);
    if (close && candSame !== bestSame) {
      if (candSame) {
        best = cand;
        bestScore = score;
      }
      continue;
    }
    if (score > bestScore) {
      best = cand;
      bestScore = score;
    }
  }
  return best;
}

/**
 * @param {{ attributes: object[] }[]} groups column order
 * @returns {(object | null)[][]} rows; each row has groups.length cells
 */
export function alignAttributeRows(groups) {
  if (!Array.isArray(groups) || groups.length === 0) return [];

  const unused = groups.map((group) => new Set(group?.attributes ?? []));

  const rows = [];
  const remaining = () => unused.reduce((sum, set) => sum + set.size, 0);

  while (remaining() > 0) {
    const { groupIndex, attr: seed } = pickSeed(unused);
    if (!seed) break;

    const row = Array.from({ length: groups.length }, () => null);
    row[groupIndex] = seed;
    unused[groupIndex].delete(seed);

    for (let i = 0; i < groups.length; i++) {
      if (i === groupIndex) continue;
      const match = bestMatch(seed, unused[i]);
      if (!match) continue;
      row[i] = match;
      unused[i].delete(match);
    }

    rows.push(row);
  }

  rows.sort((a, b) => {
    const filledA = a.filter(Boolean).length;
    const filledB = b.filter(Boolean).length;
    if (filledB !== filledA) return filledB - filledA;
    const orderA = Math.min(
      ...a.filter(Boolean).map((attr) => sortKey(attr)[0]),
    );
    const orderB = Math.min(
      ...b.filter(Boolean).map((attr) => sortKey(attr)[0]),
    );
    return orderA - orderB;
  });

  return rows;
}
