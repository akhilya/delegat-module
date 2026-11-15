import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { autoMapOptions } from "./autoMapOptions.js";

const src = (value, label) => ({ value, label });
const tgt = (value, label) => ({ value, label });

describe("autoMapOptions", () => {
  it("maps exact normalized labels", () => {
    const rows = autoMapOptions(
      [src("c1", "Красный"), src("c2", "Синий")],
      [tgt("10", "красный"), tgt("11", "СИНИЙ")],
    );
    assert.deepEqual(rows, [
      { from: "c1", to: "10" },
      { from: "c2", to: "11" },
    ]);
  });

  it("normalizes punctuation, ё/е and value fallbacks", () => {
    const rows = autoMapOptions(
      [src("s", "S/M"), src("e", "Зелёный")],
      [tgt("sm", "s - m"), tgt("g", "Зеленый")],
    );
    assert.deepEqual(rows, [
      { from: "s", to: "sm" },
      { from: "e", to: "g" },
    ]);
  });

  it("maps an unambiguous containment and skips ambiguous ones", () => {
    const rows = autoMapOptions(
      [src("a", "Чёрный глянец"), src("b", "Цвет")],
      [tgt("1", "Чёрный"), tgt("2", "Цветной"), tgt("3", "Цвет металлик")],
    );
    assert.deepEqual(rows, [{ from: "a", to: "1" }]);
  });

  it("maps a unique fuzzy hit within bound", () => {
    const rows = autoMapOptions(
      [src("x", "Фиолетовый")],
      [tgt("7", "Фиолетовые"), tgt("8", "Оранжевый")],
    );
    assert.deepEqual(rows, [{ from: "x", to: "7" }]);
  });

  it("does not overwrite already-mapped sources", () => {
    const rows = autoMapOptions(
      [src("a", "Белый"), src("b", "Бежевый")],
      [tgt("1", "Белый"), tgt("2", "Бежевый")],
      new Set(["a"]),
    );
    assert.deepEqual(rows, [{ from: "b", to: "2" }]);
  });

  it("returns nothing without options on either side", () => {
    assert.deepEqual(autoMapOptions([], [tgt("1", "A")]), []);
    assert.deepEqual(autoMapOptions([src("a", "A")], []), []);
  });
});
