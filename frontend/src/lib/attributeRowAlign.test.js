import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  alignAttributeRows,
  normalizeAttributeName,
  scoreAttributePair,
} from "./attributeRowAlign.js";

describe("normalizeAttributeName", () => {
  it("drops filler words and maps color synonyms", () => {
    assert.equal(normalizeAttributeName("Цвет товара"), "color");
    assert.equal(normalizeAttributeName("Цвет"), "color");
    assert.equal(normalizeAttributeName("color"), "color");
  });

  it("maps height with изделие/предмет fillers", () => {
    assert.equal(normalizeAttributeName("Высота предмета"), "height");
    assert.equal(normalizeAttributeName("Высота"), "height");
  });

  it("keeps title tokens so Название still matches", () => {
    assert.equal(normalizeAttributeName("Название товара"), "название");
    assert.equal(normalizeAttributeName("Name"), "name");
  });
});

describe("scoreAttributePair", () => {
  it("scores Цвет and Цвет товара as a full match", () => {
    assert.equal(
      scoreAttributePair({ name: "Цвет" }, { name: "Цвет товара" }),
      1,
    );
  });

  it("scores color against Цвет as a full match", () => {
    assert.equal(scoreAttributePair({ name: "color" }, { name: "Цвет" }), 1);
  });
});

describe("alignAttributeRows", () => {
  it("puts similar color and height fields on the same rows", () => {
    const rows = alignAttributeRows([
      {
        attributes: [
          { id: 1, name: "Цвет", type: "select", sortOrder: 0 },
          { id: 2, name: "Высота", type: "number", sortOrder: 1 },
        ],
      },
      {
        attributes: [
          { id: 11, name: "Цвет товара", type: "select", sortOrder: 0 },
          { id: 12, name: "Высота предмета", type: "number", sortOrder: 1 },
        ],
      },
    ]);

    assert.equal(rows.length, 2);
    assert.equal(rows[0][0].id, 1);
    assert.equal(rows[0][1].id, 11);
    assert.equal(rows[1][0].id, 2);
    assert.equal(rows[1][1].id, 12);
  });

  it("aligns three marketplace groups", () => {
    const rows = alignAttributeRows([
      { attributes: [{ id: 1, name: "Цвет", type: 3, sortOrder: 0 }] },
      { attributes: [{ id: 2, name: "Цвет товара", type: 3, sortOrder: 0 }] },
      { attributes: [{ id: 3, name: "color", type: "select", sortOrder: 0 }] },
    ]);

    assert.equal(rows.length, 1);
    assert.deepEqual(
      rows[0].map((cell) => cell.id),
      [1, 2, 3],
    );
  });

  it("leaves unmatched fields as their own row", () => {
    const rows = alignAttributeRows([
      {
        attributes: [
          { id: 1, name: "Цвет", type: "select", sortOrder: 0 },
          { id: 2, name: "Состав", type: "text", sortOrder: 1 },
        ],
      },
      {
        attributes: [
          { id: 11, name: "Цвет товара", type: "select", sortOrder: 0 },
        ],
      },
    ]);

    assert.equal(rows.length, 2);
    const color = rows.find((row) => row[0]?.id === 1);
    const leftover = rows.find((row) => row[0]?.id === 2);
    assert.equal(color[1].id, 11);
    assert.equal(leftover[1], null);
  });

  it("returns an empty list when there are no groups", () => {
    assert.deepEqual(alignAttributeRows([]), []);
  });

  it("prefers the same attribute type when scores are close", () => {
    const rows = alignAttributeRows([
      {
        attributes: [{ id: 1, name: "Цвет", type: "select", sortOrder: 0 }],
      },
      {
        attributes: [
          { id: 11, name: "Цвет", type: "number", sortOrder: 0 },
          { id: 12, name: "Цвет товара", type: "select", sortOrder: 1 },
        ],
      },
    ]);

    assert.equal(rows.length, 2);
    const color = rows.find((row) => row[0]?.id === 1);
    assert.equal(color[1].id, 12);
  });

  it("sorts multi-column matches before leftovers by sortOrder", () => {
    const rows = alignAttributeRows([
      {
        attributes: [
          { id: 1, name: "Цвет", type: "select", sortOrder: 0 },
          { id: 2, name: "Состав", type: "text", sortOrder: 1 },
          { id: 3, name: "Артикул поставщика", type: "text", sortOrder: 2 },
        ],
      },
      {
        attributes: [
          { id: 11, name: "Цвет товара", type: "select", sortOrder: 0 },
        ],
      },
    ]);

    assert.equal(rows.length, 3);
    assert.equal(rows[0][0].id, 1);
    assert.equal(rows[0][1].id, 11);
    assert.equal(rows[1][0].id, 2);
    assert.equal(rows[2][0].id, 3);
  });
});
