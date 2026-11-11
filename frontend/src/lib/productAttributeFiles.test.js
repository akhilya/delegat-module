import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  ATTRIBUTE_FILE_INITIAL_RESULT_LIMIT,
  ATTRIBUTE_FILE_SEARCH_RESULT_LIMIT,
  attributeFileName,
  filterAttributeFiles,
  limitAttributeFileResults,
  parseAttributeFileIds,
} from "./productAttributeFiles.js";

describe("product attribute file values", () => {
  it("parses ordered persisted IDs and request-local create keys", () => {
    assert.deepEqual(parseAttributeFileIds("[12,34]"), [12, 34]);
    assert.deepEqual(parseAttributeFileIds("[-1,-2]"), [-1, -2]);
  });

  it("treats empty values as cleared attachments", () => {
    assert.deepEqual(parseAttributeFileIds(""), []);
    assert.deepEqual(parseAttributeFileIds("[]"), []);
    assert.deepEqual(parseAttributeFileIds(null), []);
  });

  it("rejects malformed, duplicate, non-integer, zero and unsafe IDs", () => {
    for (const raw of [
      "not-json",
      "12",
      "[1,1]",
      "[0]",
      "[1.5]",
      '["12"]',
      "[9007199254740992]",
    ]) {
      assert.equal(parseAttributeFileIds(raw), null, raw);
    }
  });

  it("searches product files by name and content type", () => {
    const files = [
      { id: 1, fileName: "Manual.PDF", contentType: "application/pdf" },
      { id: 2, displayName: "Front view", contentType: "image/jpeg" },
    ];

    assert.deepEqual(filterAttributeFiles(files, "manual"), [files[0]]);
    assert.deepEqual(filterAttributeFiles(files, "image/jpeg"), [files[1]]);
    assert.equal(attributeFileName({ id: 3 }), "File 3");
  });

  it("bounds default results and keeps selected files visible", () => {
    const files = Array.from({ length: 80 }, (_, index) => ({
      id: index + 1,
      fileName: `Document ${index + 1}.pdf`,
    }));

    const visible = limitAttributeFileResults(files, [75, 80]);

    assert.equal(visible.length, ATTRIBUTE_FILE_INITIAL_RESULT_LIMIT);
    assert.deepEqual(
      visible.slice(0, 2).map((file) => file.id),
      [75, 80],
    );
    assert.ok(visible.some((file) => file.id === 1));
    assert.ok(!visible.some((file) => file.id === 79));
  });

  it("limits large search result sets without dropping the searchable source", () => {
    const files = Array.from({ length: 240 }, (_, index) => ({
      id: index + 1,
      fileName: `Product document ${index + 1}.pdf`,
    }));
    const matches = filterAttributeFiles(files, "product document");
    const visible = limitAttributeFileResults(matches, [], true);

    assert.equal(matches.length, 240);
    assert.equal(visible.length, ATTRIBUTE_FILE_SEARCH_RESULT_LIMIT);
    assert.equal(visible.at(-1).id, 100);
  });
});
