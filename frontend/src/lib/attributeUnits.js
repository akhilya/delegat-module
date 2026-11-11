/** Common marketplace and seller units. Unknown seller-specific labels remain valid values. */
export const ATTRIBUTE_UNITS = [
  ["mg", "mass", "mg"],
  ["g", "mass", "g"],
  ["kg", "mass", "kg"],
  ["t", "mass", "t"],
  ["oz", "mass", "oz"],
  ["lb", "mass", "lb"],
  ["ct", "mass", "ct"],
  ["mm", "length", "mm"],
  ["um", "length", "μm"],
  ["nm", "length", "nm"],
  ["cm", "length", "cm"],
  ["dm", "length", "dm"],
  ["m", "length", "m"],
  ["km", "length", "km"],
  ["in", "length", "in"],
  ["ft", "length", "ft"],
  ["yd", "length", "yd"],
  ["mm2", "area", "mm²"],
  ["cm2", "area", "cm²"],
  ["m2", "area", "m²"],
  ["km2", "area", "km²"],
  ["ha", "area", "ha"],
  ["ul", "volume", "μl"],
  ["ml", "volume", "ml"],
  ["cl", "volume", "cl"],
  ["dl", "volume", "dl"],
  ["l", "volume", "l"],
  ["cm3", "volume", "cm³"],
  ["m3", "volume", "m³"],
  ["pcs", "count", "pcs"],
  ["pair", "count", "pair"],
  ["set", "count", "set"],
  ["pack", "count", "pack"],
  ["roll", "count", "roll"],
  ["sheet", "count", "sheet"],
  ["s", "time", "s"],
  ["min", "time", "min"],
  ["h", "time", "h"],
  ["day", "time", "day"],
  ["week", "time", "week"],
  ["month", "time", "month"],
  ["year", "time", "year"],
  ["c", "temperature", "°C"],
  ["f", "temperature", "°F"],
  ["k", "temperature", "K"],
  ["v", "voltage", "V"],
  ["mv", "voltage", "mV"],
  ["a", "current", "A"],
  ["ma", "current", "mA"],
  ["w", "power", "W"],
  ["kw", "power", "kW"],
  ["wh", "energy", "Wh"],
  ["kwh", "energy", "kWh"],
  ["j", "energy", "J"],
  ["kj", "energy", "kJ"],
  ["cal", "energy", "cal"],
  ["kcal", "energy", "kcal"],
  ["mah", "electric_charge", "mAh"],
  ["ah", "electric_charge", "Ah"],
  ["ohm", "resistance", "Ω"],
  ["hz", "frequency", "Hz"],
  ["khz", "frequency", "kHz"],
  ["mhz", "frequency", "MHz"],
  ["ghz", "frequency", "GHz"],
  ["pa", "pressure", "Pa"],
  ["kpa", "pressure", "kPa"],
  ["mpa", "pressure", "MPa"],
  ["mmhg", "pressure", "mmHg"],
  ["bar", "pressure", "bar"],
  ["atm", "pressure", "atm"],
  ["n", "force", "N"],
  ["n_m", "torque", "N·m"],
  ["mps", "speed", "m/s"],
  ["kmh", "speed", "km/h"],
  ["rpm", "rotational_speed", "rpm"],
  ["lpm", "volume_flow", "l/min"],
  ["m3h", "volume_flow", "m³/h"],
  ["lm", "luminous_flux", "lm"],
  ["lx", "illuminance", "lx"],
  ["deg", "angle", "°"],
  ["rad", "angle", "rad"],
  ["pct", "ratio", "%"],
  ["kg_m3", "density", "kg/m³"],
  ["g_cm3", "density", "g/cm³"],
  ["bit", "digital_size", "bit"],
  ["byte", "digital_size", "B"],
  ["kb", "digital_size", "KB"],
  ["mb", "digital_size", "MB"],
  ["gb", "digital_size", "GB"],
  ["tb", "digital_size", "TB"],
].map(([code, dimension, symbol]) => ({ code, dimension, symbol }));

const ALIASES = new Map();
for (const unit of ATTRIBUTE_UNITS) {
  ALIASES.set(unit.code.toLowerCase(), unit.code);
  ALIASES.set(unit.symbol.toLowerCase(), unit.code);
}
for (const [alias, code] of Object.entries({
  гр: "g",
  грамм: "g",
  грамма: "g",
  gram: "g",
  grams: "g",
  килограмм: "kg",
  килограмма: "kg",
  кг: "kg",
  kilogram: "kg",
  kilograms: "kg",
  миллиметр: "mm",
  мм: "mm",
  millimeter: "mm",
  millimeters: "mm",
  сантиметр: "cm",
  см: "cm",
  centimeter: "cm",
  centimeters: "cm",
  метр: "m",
  метра: "m",
  meter: "m",
  meters: "m",
  литр: "l",
  литра: "l",
  литров: "l",
  liter: "l",
  liters: "l",
  миллилитр: "ml",
  миллилитров: "ml",
  milliliter: "ml",
  milliliters: "ml",
  штука: "pcs",
  штуки: "pcs",
  штук: "pcs",
  шт: "pcs",
  "шт.": "pcs",
  компл: "set",
  "компл.": "set",
  комплект: "set",
  упак: "pack",
  упаковка: "pack",
  "°с": "c",
  гц: "hz",
  "кг/м3": "kg_m3",
  "г/см3": "g_cm3",
  "н·м": "n_m",
  "newton meter": "n_m",
  "newton-metre": "n_m",
}))
  ALIASES.set(alias, code);

export function normalizeUnitCode(value) {
  const raw = String(value ?? "").trim();
  if (!raw) return null;
  return (
    ALIASES.get(
      raw
        .normalize("NFKC")
        .toLowerCase()
        .replaceAll("ё", "е")
        .replaceAll("μ", "u")
        .replaceAll("µ", "u"),
    ) || raw
  );
}

export function getAttributeUnit(value) {
  const code = normalizeUnitCode(value);
  return ATTRIBUTE_UNITS.find((unit) => unit.code === code) || null;
}

// Canonical transforms to a stable base unit: base = value * scale + offset.
// Calendar months/years and count packages intentionally have no fixed ratio.
const UNIT_TO_BASE = {
  mg: ["mass", 0.001, 0],
  g: ["mass", 1, 0],
  kg: ["mass", 1000, 0],
  t: ["mass", 1_000_000, 0],
  oz: ["mass", 28.349523125, 0],
  lb: ["mass", 453.59237, 0],
  ct: ["mass", 0.2, 0],
  nm: ["length", 1e-9, 0],
  um: ["length", 1e-6, 0],
  mm: ["length", 0.001, 0],
  cm: ["length", 0.01, 0],
  dm: ["length", 0.1, 0],
  m: ["length", 1, 0],
  km: ["length", 1000, 0],
  in: ["length", 0.0254, 0],
  ft: ["length", 0.3048, 0],
  yd: ["length", 0.9144, 0],
  mm2: ["area", 1e-6, 0],
  cm2: ["area", 1e-4, 0],
  m2: ["area", 1, 0],
  km2: ["area", 1e6, 0],
  ha: ["area", 1e4, 0],
  ul: ["volume", 1e-9, 0],
  ml: ["volume", 1e-6, 0],
  cl: ["volume", 1e-5, 0],
  dl: ["volume", 1e-4, 0],
  l: ["volume", 1e-3, 0],
  cm3: ["volume", 1e-6, 0],
  m3: ["volume", 1, 0],
  s: ["time", 1, 0],
  min: ["time", 60, 0],
  h: ["time", 3600, 0],
  day: ["time", 86400, 0],
  week: ["time", 604800, 0],
  c: ["temperature", 1, 0],
  f: ["temperature", 5 / 9, (-32 * 5) / 9],
  k: ["temperature", 1, -273.15],
  v: ["voltage", 1, 0],
  mv: ["voltage", 0.001, 0],
  a: ["current", 1, 0],
  ma: ["current", 0.001, 0],
  w: ["power", 1, 0],
  kw: ["power", 1000, 0],
  j: ["energy", 1, 0],
  kj: ["energy", 1000, 0],
  wh: ["energy", 3600, 0],
  kwh: ["energy", 3_600_000, 0],
  cal: ["energy", 4.184, 0],
  kcal: ["energy", 4184, 0],
  ah: ["electric_charge", 1, 0],
  mah: ["electric_charge", 0.001, 0],
  pa: ["pressure", 1, 0],
  kpa: ["pressure", 1000, 0],
  mpa: ["pressure", 1_000_000, 0],
  mmhg: ["pressure", 133.322387415, 0],
  bar: ["pressure", 100_000, 0],
  atm: ["pressure", 101_325, 0],
  n: ["force", 1, 0],
  mps: ["speed", 1, 0],
  kmh: ["speed", 1 / 3.6, 0],
  rad: ["angle", 1, 0],
  deg: ["angle", Math.PI / 180, 0],
  pct: ["ratio", 0.01, 0],
  kg_m3: ["density", 1, 0],
  g_cm3: ["density", 1000, 0],
  bit: ["digital_size", 1, 0],
  byte: ["digital_size", 8, 0],
  kb: ["digital_size", 8000, 0],
  mb: ["digital_size", 8_000_000, 0],
  gb: ["digital_size", 8_000_000_000, 0],
  tb: ["digital_size", 8_000_000_000_000, 0],
};

/** Return a reversible conversion, or null for equal/unknown/incompatible units. */
export function getUnitConversion(fromUnit, toUnit) {
  const from = normalizeUnitCode(fromUnit);
  const to = normalizeUnitCode(toUnit);
  if (!from || !to || from.toLowerCase() === to.toLowerCase()) return null;

  const fromTransform = UNIT_TO_BASE[from.toLowerCase()];
  const toTransform = UNIT_TO_BASE[to.toLowerCase()];
  if (!fromTransform || !toTransform || fromTransform[0] !== toTransform[0])
    return null;

  return {
    scale: fromTransform[1] / toTransform[1],
    offset: (fromTransform[2] - toTransform[2]) / toTransform[1],
  };
}

export function unitCodeList() {
  return ATTRIBUTE_UNITS;
}
