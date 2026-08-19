import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  noteId,
  notePreviewTitle,
  noteImportanceClass,
  noteImportanceLabel,
  noteListCaption,
  sortNotesNewestFirst,
} from "./notesUi.js";

describe("notesUi", () => {
  it("reads camelCase or PascalCase ids", () => {
    assert.equal(noteId({ id: 3 }), 3);
    assert.equal(noteId({ Id: 8 }), 8);
  });

  it("uses the first line as the list title", () => {
    assert.equal(notePreviewTitle({ text: "  Hello\nWorld  " }, "Note"), "Hello");
    assert.equal(notePreviewTitle({ text: "   " }, "Note"), "Note");
  });

  it("colors high importance as status, not decoration", () => {
    assert.equal(noteImportanceClass("High"), "text-warning");
    assert.equal(noteImportanceClass("Emergency"), "text-destructive");
    assert.equal(noteImportanceClass("Low"), "text-muted-foreground");
  });

  it("resolves importance labels through i18n keys", () => {
    const t = (key) => key;
    assert.equal(
      noteImportanceLabel("Critical", t),
      "components.note.importanceCritical",
    );
  });

  it("omits product facts on an embedded product caption", () => {
    const caption = noteListCaption(
      {
        productName: "Shirt",
        productSku: "SKU-1",
        authorUserName: "Ada",
        updatedAt: "2026-08-19T10:00:00Z",
      },
      {
        includeProduct: false,
        formatDateTime: () => "19 Aug",
        t: () => "unknown",
      },
    );
    assert.equal(caption, "Ada · 19 Aug");
  });

  it("sorts by last edit, newest first", () => {
    const sorted = sortNotesNewestFirst([
      { id: 1, updatedAt: "2026-01-01T00:00:00Z" },
      { id: 2, updatedAt: "2026-08-01T00:00:00Z" },
    ]);
    assert.deepEqual(
      sorted.map((n) => n.id),
      [2, 1],
    );
  });
});
