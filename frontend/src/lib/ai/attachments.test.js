import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  MAX_FOLDER_FILES,
  applyStagedIds,
  attachmentMetaList,
  attachmentPreview,
  filesFromDirectoryInput,
  mergeAttachedFiles,
} from "./attachments.js";

function fakeFile(name, relativePath, size = 10) {
  return { name, webkitRelativePath: relativePath, size };
}

describe("ai attachments", () => {
  it("builds relative paths from a directory FileList and drops parent segments", () => {
    const files = filesFromDirectoryInput([
      fakeFile("SKU-HELLO.jpg", "photos/SKU-HELLO.jpg", 120),
      fakeFile("secret.txt", "../secret.txt", 1),
      fakeFile("ok.png", "ok.png", 4),
    ]);
    assert.deepEqual(
      files.map((f) => f.relativePath),
      ["ok.png", "photos/SKU-HELLO.jpg"],
    );
  });

  it("uses the file name when the picker did not supply a folder path", () => {
    const files = filesFromDirectoryInput([
      { name: "SKU-HELLO.jpg", size: 40 },
    ]);
    assert.deepEqual(files, [
      {
        relativePath: "SKU-HELLO.jpg",
        fileName: "SKU-HELLO.jpg",
        size: 40,
        file: { name: "SKU-HELLO.jpg", size: 40 },
      },
    ]);
  });

  it("caps the manifest size", () => {
    const many = Array.from({ length: MAX_FOLDER_FILES + 20 }, (_, i) =>
      fakeFile(`f${i}.jpg`, `f${i}.jpg`),
    );
    assert.equal(filesFromDirectoryInput(many).length, MAX_FOLDER_FILES);
  });

  it("merges extra files and folders without exceeding the cap", () => {
    const a = filesFromDirectoryInput([fakeFile("a.jpg", "a.jpg")]);
    const b = filesFromDirectoryInput([fakeFile("b.jpg", "folder/b.jpg")]);
    const merged = mergeAttachedFiles(a, b);
    assert.deepEqual(
      merged.map((f) => f.relativePath),
      ["a.jpg", "folder/b.jpg"],
    );
    const many = Array.from({ length: MAX_FOLDER_FILES }, (_, i) => ({
      relativePath: `f${i}.jpg`,
      fileName: `f${i}.jpg`,
      size: 1,
      file: fakeFile(`f${i}.jpg`, `f${i}.jpg`),
    }));
    assert.equal(mergeAttachedFiles(many, a).length, MAX_FOLDER_FILES);
  });

  it("stores names without the File blob", () => {
    const files = filesFromDirectoryInput([
      fakeFile("SKU-HELLO.jpg", "photos/SKU-HELLO.jpg", 120),
    ]);
    assert.deepEqual(attachmentMetaList(files), [
      { relativePath: "photos/SKU-HELLO.jpg", fileName: "SKU-HELLO.jpg", size: 120 },
    ]);
    const preview = attachmentPreview(
      Array.from({ length: 8 }, (_, i) => ({
        relativePath: `f${i}.jpg`,
        fileName: `f${i}.jpg`,
      })),
    );
    assert.equal(preview.shown.length, 6);
    assert.equal(preview.hidden, 2);
  });

  it("keeps staging ids on meta and merges them from the upload response", () => {
    const withId = attachmentMetaList([
      {
        relativePath: "photos/SKU-HELLO.jpg",
        fileName: "SKU-HELLO.jpg",
        size: 120,
        id: 9,
      },
    ]);
    assert.deepEqual(withId, [
      {
        relativePath: "photos/SKU-HELLO.jpg",
        fileName: "SKU-HELLO.jpg",
        size: 120,
        id: 9,
      },
    ]);
    const merged = applyStagedIds(
      [
        {
          relativePath: "photos/SKU-HELLO.jpg",
          fileName: "SKU-HELLO.jpg",
          size: 120,
        },
      ],
      [{ id: 9, relativePath: "photos/SKU-HELLO.jpg" }],
    );
    assert.equal(merged[0].id, 9);
  });
});
